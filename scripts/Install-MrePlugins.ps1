[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$UpdateConfiguration
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$cacheRoot = Join-Path $runtimeRoot 'plugin-builds'
$serverRoot = Join-Path $runtimeRoot 'server'
$instanceRoot = Join-Path $serverRoot 'Servers\MRE'
$rocketRoot = Join-Path $instanceRoot 'Rocket'
$lockData = Get-Content -LiteralPath (Join-Path $projectRoot 'plugins.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8 = New-Object Text.UTF8Encoding($false)

function Assert-Hash([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Файл отсутствует: $Path" }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected.ToLowerInvariant()) { throw "SHA256 не совпадает: $Path" }
}

function Get-VerifiedFile([string]$Url, [string]$Path, [string]$Sha256) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
    }
    Assert-Hash $Path $Sha256
}

function Get-ContainedPath([string]$Root, [string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative)) { throw 'Абсолютный путь в manifest запрещен.' }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $result = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $result.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Путь выходит за пределы каталога: $Relative"
    }
    return $result
}

New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
$downloaded = @()
foreach ($plugin in $lockData.plugins) {
    $pluginCache = Get-ContainedPath $cacheRoot $plugin.name
    New-Item -ItemType Directory -Path $pluginCache -Force | Out-Null
    $archivePath = Get-ContainedPath $pluginCache $plugin.archive_name
    Get-VerifiedFile $plugin.archive_url $archivePath $plugin.archive_sha256
    $licensePath = Join-Path $pluginCache 'LICENSE'
    Get-VerifiedFile $plugin.license_url $licensePath $plugin.license_sha256
    $releaseRoot = Join-Path $pluginCache 'release'
    if (-not (Test-Path -LiteralPath $releaseRoot)) {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $releaseRoot
    }
    foreach ($file in $plugin.files) {
        Assert-Hash (Get-ContainedPath $releaseRoot $file.path) $file.sha256
        if ($file.license_url) {
            $extraLicense = Join-Path $pluginCache (([IO.Path]::GetFileNameWithoutExtension($file.path)) + '.LICENSE')
            Get-VerifiedFile $file.license_url $extraLicense $file.license_sha256
        }
    }
    foreach ($configFile in $plugin.configuration) {
        $configPath = Get-ContainedPath (Join-Path $projectRoot 'config\plugins') $configFile
        $null = [xml](Get-Content -LiteralPath $configPath -Raw -Encoding UTF8)
    }
    $downloaded += [pscustomobject]@{Name=$plugin.name; Version=$plugin.version; SHA256=$plugin.archive_sha256; Verified=$true}
}

if (-not $Apply) {
    $downloaded
    Write-Host 'Архивы, лицензии и конфигурации проверены. Игровой сервер не изменен.'
    Write-Host 'После сборки scripts/Build-MreUi.ps1 запустите этот скрипт с -Apply на остановленном сервере.'
    return
}

$patchedRoot = Join-Path $cacheRoot 'Teleportation.MRE'
$patchedBinary = Join-Path $patchedRoot 'Teleportation.dll'
$buildManifestPath = Join-Path $patchedRoot 'build.json'
if (-not (Test-Path -LiteralPath $buildManifestPath)) {
    throw 'Сначала соберите исправление: src/Teleportation.MRE/Build.ps1. Неисправленный TPA автоматически не устанавливается.'
}
$buildManifest = Get-Content -LiteralPath $buildManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($buildManifest.patch_id -ne 'mre-recipient-guard-v1') { throw 'Неизвестный вариант исправления Teleportation.' }
Assert-Hash $patchedBinary $buildManifest.sha256
$patchedHomesRoot = Join-Path $cacheRoot 'MoreHomes.MRE'
$patchedHomesBinary = Join-Path $patchedHomesRoot 'MoreHomes.dll'
$homesBuildPath = Join-Path $patchedHomesRoot 'build.json'
if (-not (Test-Path -LiteralPath $homesBuildPath)) {
    throw 'Сначала соберите HOME UI: src/MoreHomes.MRE/Build.ps1.'
}
$homesBuild = Get-Content -LiteralPath $homesBuildPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($homesBuild.patch_id -ne 'mre-home-ui-v1') { throw 'Неизвестный вариант исправления MoreHomes.' }
Assert-Hash $patchedHomesBinary $homesBuild.sha256
if ($homesBuild.teleportation_reference_sha256 -ne $buildManifest.sha256) {
    throw 'MoreHomes собран по другой Teleportation.dll. Пересоберите оба плагина.'
}
if (-not (Test-Path -LiteralPath (Join-Path $instanceRoot 'Server\Commands.dat'))) {
    throw 'Сначала создайте экземпляр MRE первым запуском U3DS.'
}
if (-not (Test-Path -LiteralPath (Join-Path $serverRoot 'Modules\Rocket.Unturned'))) {
    throw 'Сначала установите LDM из Extras/Rocket.Unturned в Modules/Rocket.Unturned.'
}

$maintenanceLock = $null
try {
    $maintenanceLock = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $serverPrefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $runningServers = @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'" | Where-Object {
        $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($serverPrefix, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($runningServers.Count -gt 0) { throw 'Сначала корректно остановите MRE. Файлы работающего сервера не изменяются.' }

    $configCopies = @()
    foreach ($plugin in $lockData.plugins) {
        foreach ($configFile in $plugin.configuration) {
            $sourcePath = Get-ContainedPath (Join-Path $projectRoot 'config\plugins') $configFile
            $targetPath = Get-ContainedPath (Join-Path $rocketRoot 'Plugins') $configFile
            if ((Test-Path -LiteralPath $targetPath) -and -not $UpdateConfiguration) {
                if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne (Get-FileHash -LiteralPath $targetPath).Hash) {
                    throw 'Конфигурация сервера уже отличается. Для явной замены с резервной копией используйте -UpdateConfiguration.'
                }
            }
            $configCopies += [pscustomobject]@{Source=$sourcePath; Target=$targetPath}
        }
    }

    $backupRoot = Join-Path (Join-Path $projectRoot 'backups') ('plugins-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $installedFiles = @()
    foreach ($plugin in $lockData.plugins) {
        $pluginCache = Get-ContainedPath $cacheRoot $plugin.name
        foreach ($file in $plugin.files) {
            $sourcePath = Get-ContainedPath (Join-Path $pluginCache 'release') $file.path
            if ($file.path -eq 'Plugins/Teleportation.dll') { $sourcePath = $patchedBinary }
            if ($file.path -eq 'Plugins/MoreHomes.dll') { $sourcePath = $patchedHomesBinary }
            $targetPath = Get-ContainedPath $rocketRoot $file.path
            if (Test-Path -LiteralPath $targetPath) {
                $oldPath = Get-ContainedPath $backupRoot $file.path
                New-Item -ItemType Directory -Path (Split-Path -Parent $oldPath) -Force | Out-Null
                Copy-Item -LiteralPath $targetPath -Destination $oldPath
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
            Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
            $installedFiles += [pscustomobject]@{Path=$file.path; SHA256=(Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()}
            if ($file.license_url) {
                $extraLicense = Join-Path $pluginCache (([IO.Path]::GetFileNameWithoutExtension($file.path)) + '.LICENSE')
                Copy-Item -LiteralPath $extraLicense -Destination (Join-Path (Split-Path -Parent $targetPath) '0Harmony.LICENSE') -Force
            }
        }
        $pluginDirectory = Get-ContainedPath (Join-Path $rocketRoot 'Plugins') $plugin.name
        New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $pluginCache 'LICENSE') -Destination (Join-Path $pluginDirectory 'LICENSE') -Force
    }
    foreach ($copy in $configCopies) {
        if (Test-Path -LiteralPath $copy.Target) {
            $relativePath = $copy.Target.Substring($rocketRoot.Length).TrimStart('\','/')
            $oldPath = Get-ContainedPath $backupRoot $relativePath
            New-Item -ItemType Directory -Path (Split-Path -Parent $oldPath) -Force | Out-Null
            Copy-Item -LiteralPath $copy.Target -Destination $oldPath -Force
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $copy.Target) -Force | Out-Null
        Copy-Item -LiteralPath $copy.Source -Destination $copy.Target -Force
    }
    $receipt = [ordered]@{installed_at=(Get-Date).ToUniversalTime().ToString('o'); files=$installedFiles; patch_id=$buildManifest.patch_id; home_patch_id=$homesBuild.patch_id; game_smoke_test='pending'; permissions='merge config/plugins/permissions.json separately'}
    [IO.File]::WriteAllText((Join-Path $cacheRoot 'installed-receipt.json'), ($receipt | ConvertTo-Json -Depth 8), $utf8)
    Write-Host 'Плагины и конфигурации установлены. Права и игровые проверки выполняются отдельно.'
} finally {
    if ($maintenanceLock) { $maintenanceLock.Dispose() }
}
