[CmdletBinding()]
param([switch]$Apply, [switch]$UpdateConfiguration)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$buildRoot = Join-Path $runtimeRoot 'plugin-builds\MreHud'
$serverRoot = Join-Path $runtimeRoot 'server'
$pluginsRoot = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins'
$sourceDll = Join-Path $buildRoot 'MreHud.dll'
$buildPath = Join-Path $buildRoot 'build.json'
$sourceConfig = Join-Path $projectRoot 'config\plugins\MreHud\MreHud.configuration.xml'
$targetConfig = Join-Path $pluginsRoot 'MreHud\MreHud.configuration.xml'
$targetDll = Join-Path $pluginsRoot 'MreHud.dll'
$installedHarmony = Join-Path $serverRoot 'Servers\MRE\Rocket\Libraries\0Harmony.dll'
$stagedHarmony = Join-Path $runtimeRoot 'plugin-builds\MoreHomes\release\Libraries\0Harmony.dll'

if (-not (Test-Path -LiteralPath $buildPath)) { throw 'Сначала выполните scripts/Build-MreHud.ps1.' }
$build = Get-Content -LiteralPath $buildPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($build.plugin -ne 'MreHud') { throw 'Неизвестная сборка MreHud.' }
if ($build.harmony_sha256 -notmatch '^[0-9a-f]{64}$') { throw 'В manifest отсутствует SHA256 Harmony. Пересоберите MreHud.' }
if (-not (Test-Path -LiteralPath $stagedHarmony -PathType Leaf) -or
    (Get-FileHash -LiteralPath $stagedHarmony -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.harmony_sha256) {
    throw 'Подготовленная 0Harmony.dll отсутствует или отличается от версии сборки MreHud. Проверьте комплект плагинов и пересоберите MreHud.'
}
if ((Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.sha256) {
    throw 'SHA256 MreHud.dll не совпадает с build.json.'
}
$null = [xml](Get-Content -LiteralPath $sourceConfig -Raw -Encoding UTF8)

if (-not $Apply) {
    [pscustomobject]@{ Plugin='MreHud'; SHA256=$build.sha256; Target=$targetDll; Apply=$false; ClientBundle='requires_unity_editor_build' }
    Write-Host 'Проверены DLL и XML. Сервер не изменён; игровой bundle и клиентская приёмка выполняются отдельно.'
    return
}

if (-not (Test-Path -LiteralPath $pluginsRoot)) { throw 'Сначала установите MRE и LDM/Rocket.' }
$maintenanceLock = $null
try {
    $maintenanceLock = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'),'OpenOrCreate','ReadWrite','None')
    $serverExe = [IO.Path]::GetFullPath((Join-Path $serverRoot 'Unturned.exe'))
    foreach ($processInfo in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'")) {
        $liveProcess = Get-Process -Id $processInfo.ProcessId -ErrorAction SilentlyContinue
        if (-not $liveProcess -or $liveProcess.HasExited) { continue }
        if (-not $processInfo.ExecutablePath) { throw 'Нельзя проверить путь работающего Unturned; установка остановлена.' }
        if ([IO.Path]::GetFullPath($processInfo.ExecutablePath) -eq $serverExe) { throw 'Сначала корректно остановите MRE.' }
    }
    if (-not (Test-Path -LiteralPath $installedHarmony -PathType Leaf) -or
        (Get-FileHash -LiteralPath $installedHarmony -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.harmony_sha256) {
        throw 'Установленная 0Harmony.dll отсутствует или отличается от версии сборки MreHud. Сначала установите проверенные зависимости через Install-MrePlugins.ps1.'
    }
    $installedTpa = Join-Path $pluginsRoot 'Teleportation.dll'
    if (-not (Test-Path -LiteralPath $installedTpa) -or
        (Get-FileHash -LiteralPath $installedTpa -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.teleportation_sha256) {
        throw 'Сначала установите ту же сборку Teleportation, по которой собран MreHud.'
    }
    $copyConfig = -not (Test-Path -LiteralPath $targetConfig) -or $UpdateConfiguration
    $installedEssentials = Join-Path $pluginsRoot 'MreEssentials.dll'
    if (-not (Test-Path -LiteralPath $installedEssentials) -or
        (Get-FileHash -LiteralPath $installedEssentials -Algorithm SHA256).Hash.ToLowerInvariant() -ne $build.essentials_sha256) {
        throw 'Сначала установите ту же MreEssentials.dll с VoteUiSnapshot, по которой собран MreHud.'
    }
    if ($copyConfig) {
        $settings = [xml](Get-Content -LiteralPath $sourceConfig -Raw -Encoding UTF8)
        if ($settings.MreHudConfiguration.AssetsVerified -ne 'false' -or $settings.MreHudConfiguration.HideNativeHud -ne 'false') {
            throw 'Первичная установка требует AssetsVerified=false и HideNativeHud=false. Включение после клиентской приёмки.'
        }
    }
    $backupRoot = Join-Path (Join-Path $projectRoot 'backups') ('hud-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    if (Test-Path -LiteralPath $targetDll) { Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backupRoot 'MreHud.dll') }
    if ($copyConfig -and (Test-Path -LiteralPath $targetConfig)) {
        Copy-Item -LiteralPath $targetConfig -Destination (Join-Path $backupRoot 'MreHud.configuration.xml')
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $targetConfig) -Force | Out-Null
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\MreHud\LICENSE') -Destination (Join-Path (Split-Path -Parent $targetConfig) 'LICENSE') -Force
    if ($copyConfig) { Copy-Item -LiteralPath $sourceConfig -Destination $targetConfig -Force }
    $receipt = [ordered]@{
        plugin='MreHud'; installed_at_utc=[DateTime]::UtcNow.ToString('o'); sha256=$build.sha256
        backup=$backupRoot; client_bundle='not_installed_by_this_script'; in_game_verification='pending'
    }
    [IO.File]::WriteAllText((Join-Path $buildRoot 'installed-receipt.json'), ($receipt | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    $effectiveConfig = [xml](Get-Content -LiteralPath $targetConfig -Raw -Encoding UTF8)
    Write-Host ('MreHud установлен. AssetsVerified={0}; HideNativeHud={1}. Bundle и клиентская приёмка проверяются отдельно.' -f $effectiveConfig.MreHudConfiguration.AssetsVerified, $effectiveConfig.MreHudConfiguration.HideNativeHud)
    Write-Host "Откат при остановленном сервере: вернуть файлы из $backupRoot; при первой установке удалить только новый MreHud.dll и каталог его настроек."
}
finally { if ($maintenanceLock) { $maintenanceLock.Dispose() } }
