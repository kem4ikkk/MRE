[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Apply, [switch]$InterfaceOnly, [switch]$AdminOnly, [switch]$VaultOnly, [switch]$AirdropOnly)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$serverRoot = Join-Path $runtimeRoot 'server'
$pluginRoot = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins'
$utf8 = New-Object Text.UTF8Encoding($false)
$copies = @()
if (@($InterfaceOnly,$AdminOnly,$VaultOnly,$AirdropOnly | Where-Object { $_ }).Count -gt 1) { throw 'Выберите только один режим установки: InterfaceOnly, AdminOnly, VaultOnly или AirdropOnly.' }
$targetedInstall = $InterfaceOnly -or $AdminOnly -or $VaultOnly -or $AirdropOnly
$names = if ($AirdropOnly) { @('MreAirdrop') } elseif ($VaultOnly) { @('MreVault') } elseif ($InterfaceOnly) { @('MreInterface') } elseif ($AdminOnly) { @('MreInterface','MreModeration','MreInventoryAudit','MreFlight') } else { @('MreEssentials','MreInterface','MreKits','MreModeration','MreInventoryAudit','MreFlight') }
foreach ($name in $names) {
    $buildRoot = Join-Path $runtimeRoot ('builds\' + $name)
    $source = Join-Path $buildRoot ($name + '.dll')
    $manifest = Get-Content -LiteralPath (Join-Path $buildRoot 'build-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.plugin -ne $name -or $manifest.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw "Некорректный manifest $name." }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $manifest.sha256) { throw "SHA256 сборки $name не совпадает." }
    $copies += [pscustomobject]@{Source=$source; Target=(Join-Path $pluginRoot ($name + '.dll')); Name=($name + '.dll'); SHA256=$manifest.sha256}
    $licenseSource=Join-Path $projectRoot ('src\' + $name + '\LICENSE')
    $copies += [pscustomobject]@{Source=$licenseSource; Target=(Join-Path $pluginRoot ($name + '\LICENSE')); Name=($name + '/LICENSE'); SHA256=(Get-FileHash -LiteralPath $licenseSource -Algorithm SHA256).Hash.ToLowerInvariant()}
}
if (-not $targetedInstall) {
$tpaRoot = Join-Path $runtimeRoot 'plugin-builds\Teleportation.MRE'
$tpaManifest = Get-Content -LiteralPath (Join-Path $tpaRoot 'build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$tpaSource = Join-Path $tpaRoot 'Teleportation.dll'
if ($tpaManifest.patch_id -ne 'mre-recipient-guard-v1' -or (Get-FileHash -LiteralPath $tpaSource -Algorithm SHA256).Hash -ne $tpaManifest.sha256) { throw 'Не подтверждена сборка Teleportation.MRE.' }
$copies += [pscustomobject]@{Source=$tpaSource; Target=(Join-Path $pluginRoot 'Teleportation.dll'); Name='Teleportation.dll'; SHA256=$tpaManifest.sha256}
$hudPath = Join-Path $pluginRoot 'MreHud.dll'
if (Test-Path -LiteralPath $hudPath -PathType Leaf) {
    $hudManifestPath = Join-Path $runtimeRoot 'plugin-builds\MreHud\build.json'
    $essentialsCopy = $copies | Where-Object Name -eq 'MreEssentials.dll' | Select-Object -First 1
    $liveEssentials = Join-Path $pluginRoot 'MreEssentials.dll'
    $liveTpa = Join-Path $pluginRoot 'Teleportation.dll'
    $dependenciesUnchanged = (Test-Path -LiteralPath $liveEssentials -PathType Leaf) -and (Test-Path -LiteralPath $liveTpa -PathType Leaf) -and
        (Get-FileHash -LiteralPath $liveEssentials -Algorithm SHA256).Hash -eq $essentialsCopy.SHA256 -and
        (Get-FileHash -LiteralPath $liveTpa -Algorithm SHA256).Hash -eq $tpaManifest.sha256
    $manifestMatches = $false
    if (-not $dependenciesUnchanged -and (Test-Path -LiteralPath $hudManifestPath -PathType Leaf)) {
        $hudManifest = Get-Content -LiteralPath $hudManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $manifestMatches = $hudManifest.plugin -eq 'MreHud' -and (Get-FileHash -LiteralPath $hudPath -Algorithm SHA256).Hash -eq $hudManifest.sha256 -and
            $hudManifest.essentials_sha256 -eq $essentialsCopy.SHA256 -and $hudManifest.teleportation_sha256 -eq $tpaManifest.sha256
    }
    if (-not $dependenciesUnchanged -and -not $manifestMatches) {
        throw 'Сборки MreEssentials или Teleportation не соответствуют установленному MreHud. Требуется согласованная сборка UI.'
    }
}
}
$copies | Select-Object Name,SHA256
if (-not $Apply) { Write-Host 'Сборки проверены. Сервер не изменен.'; return }
if (-not $PSCmdlet.ShouldProcess($pluginRoot,'Установить серверные плагины MRE и согласованные настройки')) { return }

$lockHandle = $null
try {
    $lockHandle = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'),'OpenOrCreate','ReadWrite','None')
    $serverPrefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    foreach ($processItem in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $imagePath=Get-MreProcessPath $processItem
        if (-not $imagePath) { throw 'Не удалось проверить путь работающего Unturned.' }
        if ([IO.Path]::GetFullPath($imagePath).StartsWith($serverPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Сначала корректно остановите MRE.' }
    }
    $essentialsPath = Join-Path $pluginRoot 'MreEssentials\MreEssentials.configuration.xml'
    $homePath = Join-Path $pluginRoot 'MoreHomes\MoreHomes.configuration.xml'
    $commandMapPath = Join-Path (Split-Path -Parent $pluginRoot) 'Commands.config.xml'
    # Существующие переводы, cooldown, кровати, наборы, UI и произвольные настройки не заменяются шаблонами.
    if (-not $targetedInstall) {
        [xml]$essentials = Get-Content -LiteralPath $essentialsPath -Raw -Encoding UTF8
        [xml]$homes = Get-Content -LiteralPath $homePath -Raw -Encoding UTF8
    }
    if (-not ($VaultOnly -or $AirdropOnly)) {
    if (Test-Path -LiteralPath $commandMapPath) { [xml]$commandMap = Get-Content -LiteralPath $commandMapPath -Raw -Encoding UTF8 }
    else { [xml]$commandMap = '<RocketCommands><Commands /></RocketCommands>' }
    }
    function Set-XmlValue([xml]$Document,[string]$Name,[string]$Value) {
        $node=$Document.DocumentElement.SelectSingleNode($Name)
        if (-not $node) { $node=$Document.CreateElement($Name); [void]$Document.DocumentElement.AppendChild($node) }
        $node.InnerText=$Value
    }
    if (-not $targetedInstall) {
        Set-XmlValue $essentials 'MinimumPlayers' '1'
        Set-XmlValue $essentials 'MaxSkillsEnabled' 'true'
        Set-XmlValue $homes 'DefaultMaxHomes' '2'
        Set-XmlValue $homes 'DefaultHomeDelay' '10'
    }
    # Rocket читает приоритет из сохраненного mapping, даже при Register(... High).
    if (-not ($VaultOnly -or $AirdropOnly)) {
    foreach ($mapping in @($commandMap.SelectNodes('/RocketCommands/Commands/Command'))) {
        if ($mapping.InnerText -in @('Rocket.Unturned.Commands.CommandAdmin/admin','Rocket.Unturned.Commands.UnturnedCommands/admin')) {
            $mapping.SetAttribute('Enabled','false')
        }
    }
    foreach ($alias in @('admin','mreadmin')) {
        $typeName='MreInterface.CommandAdmin/' + $alias
        $mapping=@($commandMap.SelectNodes('/RocketCommands/Commands/Command') | Where-Object InnerText -eq $typeName)
        if (-not $mapping.Count) {
            $node=$commandMap.CreateElement('Command'); $node.InnerText=$typeName
            $node.SetAttribute('Name',$alias); [void]$commandMap.DocumentElement.SelectSingleNode('Commands').AppendChild($node)
            $mapping=@($node)
        }
        foreach ($node in $mapping) { $node.SetAttribute('Enabled','true'); $node.SetAttribute('Priority','High') }
    }
    }

    $backupRoot=Join-Path $projectRoot ('backups\features-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $targets=@($copies | ForEach-Object Target)
    if (-not ($VaultOnly -or $AirdropOnly)) { $targets += @($commandMapPath) }
    if (-not $targetedInstall) { $targets += @($essentialsPath,$homePath) }
    foreach ($target in $targets) {
        if (-not (Test-Path -LiteralPath $target)) { continue }
        $relative=if ($target -eq $commandMapPath) { 'Commands.config.xml' } else { $target.Substring($pluginRoot.Length).TrimStart('\') }
        $backupPath=Join-Path $backupRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $backupPath
    }
    foreach ($copy in $copies) {
        # Проверка повторяется после блокировки: соседняя сессия могла пересобрать DLL.
        if ((Get-FileHash -LiteralPath $copy.Source -Algorithm SHA256).Hash -ne $copy.SHA256) { throw "Сборка изменилась во время установки: $($copy.Name)." }
    }
    foreach ($copy in $copies) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $copy.Target) -Force | Out-Null
        $temporary=$copy.Target + '.mre-install.tmp'
        Copy-Item -LiteralPath $copy.Source -Destination $temporary -Force
        if (Test-Path -LiteralPath $copy.Target) { [IO.File]::Replace($temporary,$copy.Target,($copy.Target + '.previous')) }
        else { [IO.File]::Move($temporary,$copy.Target) }
    }
    if (-not $targetedInstall) { $essentials.Save($essentialsPath); $homes.Save($homePath) }
    if (-not ($VaultOnly -or $AirdropOnly)) { $commandMap.Save($commandMapPath) }
    $receipt=[ordered]@{
        installed_at_utc=[DateTime]::UtcNow.ToString('o')
        backup_path=$backupRoot
        files=@($copies | Select-Object Name,SHA256)
        settings=if($targetedInstall){[ordered]@{InterfaceOnly=[bool]$InterfaceOnly;AdminOnly=[bool]$AdminOnly;VaultOnly=[bool]$VaultOnly;AirdropOnly=[bool]$AirdropOnly}}else{[ordered]@{MinimumPlayers=1;MaxSkillsEnabled=$true;DefaultMaxHomes=2;DefaultHomeDelay=10}}
        gameplay_validation='pending'
    }
    [IO.File]::WriteAllText((Join-Path $runtimeRoot 'features-installed.json'),($receipt | ConvertTo-Json -Depth 6),$utf8)
    Write-Host 'DLL установлены. Сохранения плагинов, пользовательский UI и остальные настройки сохранены.'
}
finally { if ($lockHandle) { $lockHandle.Dispose() } }
