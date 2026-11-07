[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$serverRoot = Join-Path $runtimeRoot 'server'
$pluginRoot = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins'
$tpaBuildRoot = Join-Path $runtimeRoot 'plugin-builds\Teleportation.MRE'
$hudBuildRoot = Join-Path $runtimeRoot 'plugin-builds\MreHud'
$tpaSource = Join-Path $tpaBuildRoot 'Teleportation.dll'
$tpaManifestPath = Join-Path $tpaBuildRoot 'build.json'
$hudSource = Join-Path $hudBuildRoot 'MreHud.dll'
$hudManifestPath = Join-Path $hudBuildRoot 'build.json'
$tpaTarget = Join-Path $pluginRoot 'Teleportation.dll'
$hudTarget = Join-Path $pluginRoot 'MreHud.dll'
$essentialsTarget = Join-Path $pluginRoot 'MreEssentials.dll'
$harmonyTarget = Join-Path $serverRoot 'Servers\MRE\Rocket\Libraries\0Harmony.dll'

foreach ($path in @($tpaSource, $tpaManifestPath, $hudSource, $hudManifestPath, $tpaTarget, $hudTarget, $essentialsTarget, $harmonyTarget)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Не найден обязательный файл: $path" }
}

$tpaManifest = Get-Content -LiteralPath $tpaManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$hudManifest = Get-Content -LiteralPath $hudManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$tpaHash = (Get-FileHash -LiteralPath $tpaSource -Algorithm SHA256).Hash.ToLowerInvariant()
$hudHash = (Get-FileHash -LiteralPath $hudSource -Algorithm SHA256).Hash.ToLowerInvariant()
$essentialsHash = (Get-FileHash -LiteralPath $essentialsTarget -Algorithm SHA256).Hash.ToLowerInvariant()
$harmonyHash = (Get-FileHash -LiteralPath $harmonyTarget -Algorithm SHA256).Hash.ToLowerInvariant()

if ($tpaManifest.patch_id -ne 'mre-recipient-guard-v1' -or $tpaManifest.sha256 -ne $tpaHash) { throw 'Сборка Teleportation не совпадает с manifest.' }
if ($hudManifest.plugin -ne 'MreHud' -or $hudManifest.sha256 -ne $hudHash -or $hudManifest.teleportation_sha256 -ne $tpaHash) { throw 'MreHud собран не с этой версией Teleportation.' }
if ($hudManifest.essentials_sha256 -ne $essentialsHash -or $hudManifest.harmony_sha256 -ne $harmonyHash) { throw 'Зависимости установленного MreHud отличаются от зависимостей новой сборки.' }

$targets = @(
    [pscustomobject]@{ Name='Teleportation.dll'; Source=$tpaSource; Target=$tpaTarget; Hash=$tpaHash },
    [pscustomobject]@{ Name='MreHud.dll'; Source=$hudSource; Target=$hudTarget; Hash=$hudHash }
)
$targets | Select-Object Name,Hash
if (-not $Apply) {
    Write-Host 'Сборки и зависимости согласованы. Сервер не изменён; для установки запустите скрипт с -Apply.'
    return
}
if (-not $PSCmdlet.ShouldProcess($pluginRoot, 'Установить согласованную пару Teleportation и MreHud')) { return }

$lockHandle = $null
$backupRoot = Join-Path (Join-Path $projectRoot 'backups') ('teleport-sound-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
try {
    $lockHandle = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
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
        $processPath = Get-MreProcessPath $processInfo
        if (-not $processPath) { throw 'Нельзя определить путь работающего Unturned; установка остановлена.' }
        if ([IO.Path]::GetFullPath($processPath).StartsWith($serverPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Сначала корректно остановите локальный сервер MRE.'
        }
    }

    foreach ($entry in $targets) {
        if ((Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Hash) { throw "Сборка изменилась: $($entry.Name)." }
    }

    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    foreach ($entry in $targets) { Copy-Item -LiteralPath $entry.Target -Destination (Join-Path $backupRoot $entry.Name) }
    try {
        foreach ($entry in $targets) {
            $temporary = $entry.Target + '.mre-install.tmp'
            Copy-Item -LiteralPath $entry.Source -Destination $temporary -Force
            Copy-Item -LiteralPath $temporary -Destination $entry.Target -Force
            Remove-Item -LiteralPath $temporary -Force
            if ((Get-FileHash -LiteralPath $entry.Target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Hash) {
                throw "Не совпал SHA256 установленного файла $($entry.Name)."
            }
        }
    } catch {
        foreach ($entry in $targets) { Copy-Item -LiteralPath (Join-Path $backupRoot $entry.Name) -Destination $entry.Target -Force }
        throw
    }

    $receipt = [ordered]@{
        installed_at_utc = [DateTime]::UtcNow.ToString('o')
        backup_path = $backupRoot
        files = @($targets | ForEach-Object { [ordered]@{ name=$_.Name; sha256=$_.Hash } })
        gameplay_validation = 'pending'
    }
    [IO.File]::WriteAllText((Join-Path $tpaBuildRoot 'installed-receipt.json'), ($receipt | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Установлены Teleportation и MreHud. Резервная копия: $backupRoot"
} finally {
    if ($lockHandle) { $lockHandle.Dispose() }
}
