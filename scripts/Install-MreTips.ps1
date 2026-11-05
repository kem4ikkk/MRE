[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$serverRoot = Join-Path $runtimeRoot 'server'
$buildRoot = Join-Path $runtimeRoot 'builds\MreTips'
$sourceDll = Join-Path $buildRoot 'MreTips.dll'
$manifestPath = Join-Path $buildRoot 'build-manifest.json'
$license = Join-Path $projectRoot 'src\MreTips\LICENSE'
$pluginsRoot = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins'
$targetDll = Join-Path $pluginsRoot 'MreTips.dll'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Сначала соберите MreTips.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.plugin -ne 'MreTips' -or $manifest.sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Некорректный манифест MreTips.' }
if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf) -or (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) { throw 'Сборка MreTips не совпадает с манифестом.' }
if (-not (Test-Path -LiteralPath $license -PathType Leaf)) { throw 'Отсутствует лицензия MreTips.' }
if (-not $Apply) {
    [pscustomobject]@{ Plugin='MreTips'; SHA256=$manifest.sha256; Target=$targetDll; Apply=$false }
    return
}
if (-not (Test-Path -LiteralPath $pluginsRoot -PathType Container)) { throw 'Сначала установите сервер и Rocket.' }
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$maintenanceLock = $null
try {
    $maintenanceLock = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'),'OpenOrCreate','ReadWrite','None')
    $serverPrefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    foreach ($processInfo in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $liveProcess = Get-Process -Id $processInfo.ProcessId -ErrorAction SilentlyContinue
        if (-not $liveProcess -or $liveProcess.HasExited) { continue }
        $path = Get-MreProcessPath $processInfo
        if (-not $path) { throw 'Нельзя проверить путь работающего Unturned; установка остановлена.' }
        if ([IO.Path]::GetFullPath($path).StartsWith($serverPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Сначала корректно остановите MRE.' }
    }
    $backupRoot = Join-Path (Join-Path $projectRoot 'backups') ('tips-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    if (Test-Path -LiteralPath $targetDll) { Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backupRoot 'MreTips.dll') }
    $settingsDir = Join-Path $pluginsRoot 'MreTips'
    $installedLicense = Join-Path $settingsDir 'LICENSE'
    if (Test-Path -LiteralPath $installedLicense) { Copy-Item -LiteralPath $installedLicense -Destination (Join-Path $backupRoot 'LICENSE') }
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
    Copy-Item -LiteralPath $license -Destination $installedLicense -Force
    if ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) { throw 'Проверка установленной DLL не прошла; восстановите резервную копию.' }
    Write-Host "MreTips установлен; текущая конфигурация сохранена. Резервная копия: $backupRoot"
    Write-Host 'При первой загрузке Rocket создаст конфигурацию с интервалом 300 секунд.'
}
finally { if ($maintenanceLock) { $maintenanceLock.Dispose() } }
