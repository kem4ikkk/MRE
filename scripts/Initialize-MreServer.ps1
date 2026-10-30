[CmdletBinding()]
param([switch]$UpdateConfiguration)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$serverRoot = Join-Path $projectRoot 'runtime\server'
$instanceRoot = Join-Path $serverRoot 'Servers\MRE'
$maintenancePath = Join-Path $projectRoot 'runtime\.mre-maintenance.lock'
$maintenanceLock = $null

try {
    $maintenanceLock = [IO.File]::Open($maintenancePath, 'OpenOrCreate', 'ReadWrite', 'None')
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'")
    foreach ($processItem in $processes) {
        if (-not $processItem.ExecutablePath) {
            throw 'Не удалось определить путь работающего Unturned. Настройка остановлена.'
        }
        if ([IO.Path]::GetFullPath($processItem.ExecutablePath).StartsWith(
            [IO.Path]::GetFullPath($serverRoot) + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Сначала корректно остановите MRE.'
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $serverRoot 'Unturned.exe'))) {
        throw 'Сначала выполните scripts/Install-MreServer.ps1.'
    }
    $rocketSource = Join-Path $serverRoot 'Extras\Rocket.Unturned'
    $rocketTarget = Join-Path $serverRoot 'Modules\Rocket.Unturned'
    New-Item -ItemType Directory -Path $rocketTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $rocketSource '*') -Destination $rocketTarget -Recurse -Force

    foreach ($directory in @(
        (Join-Path $instanceRoot 'Server'),
        (Join-Path $instanceRoot 'Rocket\Plugins'),
        (Join-Path $projectRoot 'secrets'),
        (Join-Path $projectRoot 'runtime\control\MRE\requests'),
        (Join-Path $projectRoot 'runtime\control\MRE\results'),
        (Join-Path $projectRoot 'logs')
    )) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

    $utf8 = New-Object Text.UTF8Encoding($false)
    $secretsPath = Join-Path $projectRoot 'secrets\mre.local.json'
    if (-not (Test-Path -LiteralPath $secretsPath)) {
        $randomBytes = New-Object byte[] 18
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($randomBytes) } finally { $rng.Dispose() }
        $localSettings = [ordered]@{
            JoinPassword = ([Convert]::ToBase64String($randomBytes) -replace '[+/=]', '')
            OwnerSteamId = ''
            GameServerLoginToken = ''
        }
        [IO.File]::WriteAllText($secretsPath, ($localSettings | ConvertTo-Json), $utf8)
    }
    $localSettings = Get-Content -LiteralPath $secretsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($propertyName in @('JoinPassword', 'OwnerSteamId', 'GameServerLoginToken')) {
        if ([string]$localSettings.$propertyName -match '[\r\n]') {
            throw "Недопустимый перевод строки в $propertyName."
        }
    }
    if ($localSettings.OwnerSteamId -and $localSettings.OwnerSteamId -notmatch '^7656119\d{10}$') {
        throw 'OwnerSteamId должен быть SteamID64.'
    }
    $commandsText = Get-Content -LiteralPath (Join-Path $projectRoot 'config\Commands.dat') -Raw -Encoding UTF8
    if ($localSettings.JoinPassword) { $commandsText += "`nPassword $($localSettings.JoinPassword)`n" }
    if ($localSettings.OwnerSteamId) { $commandsText += "Owner $($localSettings.OwnerSteamId)`n" }
    if ($localSettings.GameServerLoginToken) { $commandsText += "GSLT $($localSettings.GameServerLoginToken)`n" }
    $commandsPath = Join-Path $instanceRoot 'Server\Commands.dat'
    $gameplayPath = Join-Path $instanceRoot 'Config.txt'
    $gameplayText = Get-Content -LiteralPath (Join-Path $projectRoot 'config\Gameplay.txt') -Raw -Encoding UTF8
    $configurationFiles = @(
        @{ Path = $commandsPath; Content = $commandsText },
        @{ Path = $gameplayPath; Content = $gameplayText }
    )
    foreach ($configurationFile in $configurationFiles) {
        $targetPath = $configurationFile.Path
        $alreadyExists = Test-Path -LiteralPath $targetPath
        if ($alreadyExists -and -not $UpdateConfiguration) { continue }
        if ($alreadyExists) {
            $backupName = $targetPath + '.before-setup-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
            Copy-Item -LiteralPath $targetPath -Destination $backupName
        }
        [IO.File]::WriteAllText($targetPath, $configurationFile.Content.TrimEnd() + "`n", $utf8)
    }
    $permissionsPath = Join-Path $instanceRoot 'Rocket\Permissions.config.xml'
    if (-not (Test-Path -LiteralPath $permissionsPath)) {
        Copy-Item -LiteralPath (Join-Path $projectRoot 'config\RocketPermissions.xml') -Destination $permissionsPath
    }

    # Управление сервером доступно только владельцу Windows, SYSTEM и администраторам.
    foreach ($privatePath in @(
        (Join-Path $projectRoot 'runtime\control\MRE'),
        (Join-Path $projectRoot 'secrets')
    )) {
        $privateDirectory = New-Object IO.DirectoryInfo($privatePath)
        $security = $privateDirectory.GetAccessControl([Security.AccessControl.AccessControlSections]::Access)
        $security.SetAccessRuleProtection($true, $false)
        foreach ($existingRule in @($security.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) {
            $security.RemoveAccessRuleSpecific($existingRule)
        }
        $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        foreach ($sid in @($currentSid,
            (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')),
            (New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))) {
            $rule = New-Object Security.AccessControl.FileSystemAccessRule(
                $sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
            $security.AddAccessRule($rule)
        }
        $privateDirectory.SetAccessControl($security)
    }
    Write-Host 'MRE подготовлен. Существующие конфиги и группы прав сохранены; секреты остаются только локально.'
    Write-Host 'Для явного применения шаблонов Commands.dat/Config.txt используйте -UpdateConfiguration на остановленном сервере.'
}
finally {
    if ($maintenanceLock) { $maintenanceLock.Dispose() }
}
