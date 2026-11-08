[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$serverRoot = Join-Path $runtimeRoot 'server'
$pluginRoot = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins'
$commandMapPath = Join-Path (Split-Path -Parent $pluginRoot) 'Commands.config.xml'
$buildRoot = Join-Path $runtimeRoot 'builds\MreHelp'
$sourceDll = Join-Path $buildRoot 'MreHelp.dll'
$manifestPath = Join-Path $buildRoot 'build-manifest.json'
$targetDll = Join-Path $pluginRoot 'MreHelp.dll'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Сначала соберите MreHelp.ps1.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.plugin -ne 'MreHelp' -or $manifest.sha256 -notmatch '^[0-9a-f]{64}$' -or
    -not (Test-Path -LiteralPath $sourceDll -PathType Leaf) -or
    (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) {
    throw 'Сборка MreHelp не совпадает с манифестом.'
}
if (-not (Test-Path -LiteralPath $commandMapPath -PathType Leaf)) { throw 'Сначала установите Rocket: Commands.config.xml отсутствует.' }

[xml]$commandMap = Get-Content -LiteralPath $commandMapPath -Raw -Encoding UTF8
if ($commandMap.DocumentElement.Name -ne 'RocketCommands' -or -not $commandMap.SelectSingleNode('/RocketCommands/Commands')) {
    throw 'Неизвестный формат Commands.config.xml; установка остановлена.'
}
$mappings = @($commandMap.SelectNodes('/RocketCommands/Commands/Command'))
foreach ($name in @('help', 'mrehelp', 'menu')) {
    $expectedType = if ($name -eq 'menu') { 'MreHelp.CommandMenu/menu' } else { "MreHelp.CommandHelp/$name" }
    foreach ($mapping in $mappings | Where-Object { $_.GetAttribute('Name') -eq $name }) {
        if ($mapping.InnerText -match '^MreHelp\.(CommandHelp|CommandMenu)/' -and $mapping.InnerText -ne $expectedType) {
            throw "Найден конфликтующий MreHelp mapping для /${name}: $($mapping.InnerText)."
        }
        if ($mapping.GetAttribute('Enabled') -eq 'true' -and $mapping.InnerText -notmatch '^Rocket\.(Core|Unturned)\.') {
            if ($mapping.InnerText -ne $expectedType) { throw "Команду /$name уже назначил другой плагин: $($mapping.InnerText)." }
        }
    }
}
if (-not $Apply) {
    [pscustomobject]@{ Plugin='MreHelp'; SHA256=$manifest.sha256; Menu='MreHelp.CommandMenu/menu'; Help='MreHelp.CommandHelp/help'; Alias='MreHelp.CommandHelp/mrehelp'; Apply=$false }
    return
}

. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$maintenanceLock = $null
$backupRoot = $null
$installedLicense = Join-Path (Join-Path $pluginRoot 'MreHelp') 'LICENSE'
$licenseExisted = Test-Path -LiteralPath $installedLicense -PathType Leaf
$dllExisted = Test-Path -LiteralPath $targetDll -PathType Leaf
try {
    $maintenanceLock = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $serverPrefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    $processInfos = @()
    try {
        $processInfos = @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'" -ErrorAction Stop)
    } catch {
        $processInfos = @(Get-Process -Name Unturned,Unturned_Headless -ErrorAction SilentlyContinue | ForEach-Object {
            [pscustomobject]@{ ProcessId=$_.Id }
        })
    }
    foreach ($processInfo in $processInfos) {
        $liveProcess = Get-Process -Id $processInfo.ProcessId -ErrorAction SilentlyContinue
        if (-not $liveProcess -or $liveProcess.HasExited) { continue }
        $path = Get-MreProcessPath $processInfo
        if (-not $path) { throw 'Нельзя проверить путь работающего Unturned; установка остановлена.' }
        if ([IO.Path]::GetFullPath($path).StartsWith($serverPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Сначала корректно остановите MRE.' }
    }

    $backupRoot = Join-Path $projectRoot ('backups\help-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    Copy-Item -LiteralPath $commandMapPath -Destination (Join-Path $backupRoot 'Commands.config.xml')
    if ($dllExisted) { Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backupRoot 'MreHelp.dll') }
    $helpDirectory = Join-Path $pluginRoot 'MreHelp'
    New-Item -ItemType Directory -Path $helpDirectory -Force | Out-Null
    $license = Join-Path $projectRoot 'src\MreHelp\LICENSE'
    if (Test-Path -LiteralPath $installedLicense) { Copy-Item -LiteralPath $installedLicense -Destination (Join-Path $backupRoot 'LICENSE') }

    foreach ($name in @('help', 'mrehelp', 'menu')) {
        $type = if ($name -eq 'menu') { 'MreHelp.CommandMenu/menu' } else { "MreHelp.CommandHelp/$name" }
        $matches = @($commandMap.SelectNodes('/RocketCommands/Commands/Command') | Where-Object { $_.InnerText -eq $type })
        $mapping = $matches | Select-Object -First 1
        if (-not $mapping) {
            $mapping = $commandMap.CreateElement('Command')
            $mapping.SetAttribute('Name', $name)
            [void]$commandMap.DocumentElement.SelectSingleNode('Commands').AppendChild($mapping)
        }
        $mapping.SetAttribute('Name', $name)
        $mapping.SetAttribute('Enabled', 'true')
        $mapping.SetAttribute('Priority', 'High')
        $mapping.InnerText = $type
    }
    foreach ($mapping in $commandMap.SelectNodes('/RocketCommands/Commands/Command')) {
        if ($mapping.GetAttribute('Name') -in @('help', 'mrehelp') -and $mapping.InnerText -match '^Rocket\.(Core|Unturned)\..*(CommandHelp|UnturnedCommands)/') {
            $mapping.SetAttribute('Enabled', 'false')
        }
    }

    $temporaryDll = $targetDll + '.mre-install.tmp'
    Copy-Item -LiteralPath $sourceDll -Destination $temporaryDll -Force
    if ((Get-FileHash -LiteralPath $temporaryDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) { throw 'Проверка временной DLL MreHelp не пройдена.' }
    if (Test-Path -LiteralPath $targetDll) { [IO.File]::Replace($temporaryDll, $targetDll, (Join-Path $backupRoot 'MreHelp.dll.previous')) }
    else { [IO.File]::Move($temporaryDll, $targetDll) }
    Copy-Item -LiteralPath $license -Destination $installedLicense -Force
    $temporaryMap = $commandMapPath + '.mre-install.tmp'
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $true
    $writer = [System.Xml.XmlWriter]::Create($temporaryMap, $settings)
    try { $commandMap.Save($writer) } finally { $writer.Dispose() }
    [void][xml]([IO.File]::ReadAllText($temporaryMap))
    [IO.File]::Replace($temporaryMap, $commandMapPath, (Join-Path $backupRoot 'Commands.config.xml.previous'))
    Write-Host "MRE menu установлен. При следующем запуске /menu откроет меню, а /help сразу выберет пустую вкладку «Справка». Резервная копия: $backupRoot"
    Write-Host 'Клиентам нужен EffectAsset ID 48625 из пакета MRE HUD + MreHelp. Текстовая подсказка появляется только если сервер не загрузил asset; получение asset клиентом отдельно не подтверждается.'
}
catch {
    if ($backupRoot -and (Test-Path -LiteralPath (Join-Path $backupRoot 'Commands.config.xml'))) { Copy-Item -LiteralPath (Join-Path $backupRoot 'Commands.config.xml') -Destination $commandMapPath -Force }
    if ($backupRoot -and (Test-Path -LiteralPath (Join-Path $backupRoot 'MreHelp.dll'))) { Copy-Item -LiteralPath (Join-Path $backupRoot 'MreHelp.dll') -Destination $targetDll -Force }
    elseif (-not $dllExisted -and (Test-Path -LiteralPath $targetDll)) { Remove-Item -LiteralPath $targetDll -Force }
    if ($licenseExisted -and $backupRoot -and (Test-Path -LiteralPath (Join-Path $backupRoot 'LICENSE'))) { Copy-Item -LiteralPath (Join-Path $backupRoot 'LICENSE') -Destination $installedLicense -Force }
    elseif (-not $licenseExisted -and (Test-Path -LiteralPath $installedLicense)) { Remove-Item -LiteralPath $installedLicense -Force }
    throw
}
finally { if ($maintenanceLock) { $maintenanceLock.Dispose() } }
