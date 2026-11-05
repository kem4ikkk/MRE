[CmdletBinding()]
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $root 'runtime'
$server = Join-Path $runtime 'server'
$plugins = Join-Path $server 'Servers\MRE\Rocket\Plugins'
$essentials = Join-Path $runtime 'builds\MreEssentials\MreEssentials.dll'
$hud = Join-Path $runtime 'plugin-builds\MreHud\MreHud.dll'
$essentialsManifest = Get-Content -LiteralPath (Join-Path $runtime 'builds\MreEssentials\build-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$hudManifest = Get-Content -LiteralPath (Join-Path $runtime 'plugin-builds\MreHud\build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$essentialsHash = (Get-FileHash -LiteralPath $essentials -Algorithm SHA256).Hash.ToLowerInvariant()
$hudHash = (Get-FileHash -LiteralPath $hud -Algorithm SHA256).Hash.ToLowerInvariant()
if ($essentialsManifest.plugin -ne 'MreEssentials' -or $essentialsManifest.sha256 -ne $essentialsHash -or
    $hudManifest.plugin -ne 'MreHud' -or $hudManifest.sha256 -ne $hudHash -or $hudManifest.essentials_sha256 -ne $essentialsHash) { throw 'Incompatible or altered build.' }
foreach ($dependency in @(@('Teleportation.dll', $hudManifest.teleportation_sha256), @('..\Libraries\0Harmony.dll', $hudManifest.harmony_sha256))) {
    $path = Join-Path $plugins $dependency[0]
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dependency[1]) { throw 'Installed dependency differs from build.' }
}
if (-not $Apply) { [pscustomobject]@{ Essentials=$essentialsHash; Hud=$hudHash; Apply=$false }; return }
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$lock = $null
try {
    $lock = [IO.File]::Open((Join-Path $runtime '.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $prefix = [IO.Path]::GetFullPath($server).TrimEnd('\') + '\'
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $path = Get-MreProcessPath $p
        if (-not $path) { throw 'Cannot verify a running game process.' }
        if ([IO.Path]::GetFullPath($path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Stop MRE before installation.' }
    }
    $backup = Join-Path (Join-Path $root 'backups') ('vote-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    $targets = @('MreEssentials.dll','MreHud.dll')
    foreach ($name in $targets) {
        $target = Join-Path $plugins $name
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing current $name; no files changed." }
        Copy-Item -LiteralPath $target -Destination (Join-Path $backup $name)
    }
    try {
        Copy-Item -LiteralPath $essentials -Destination (Join-Path $plugins 'MreEssentials.dll') -Force
        Copy-Item -LiteralPath $hud -Destination (Join-Path $plugins 'MreHud.dll') -Force
        if ((Get-FileHash -LiteralPath (Join-Path $plugins 'MreEssentials.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $essentialsHash -or
            (Get-FileHash -LiteralPath (Join-Path $plugins 'MreHud.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hudHash) { throw 'Installed files differ from builds.' }
    } catch {
        foreach ($name in $targets) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $plugins $name) -Force }
        throw
    }
    [pscustomobject]@{ Essentials=$essentialsHash; Hud=$hudHash; Backup=$backup; Apply=$true }
} finally { if ($lock) { $lock.Dispose() } }
