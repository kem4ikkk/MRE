[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerRoot,
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [ValidateRange(20000,60000)][int]$Port = 28195
)

$ErrorActionPreference = 'Stop'
$serverId = 'MREDecayHudVisualTest'
$excluded = @('Servers','Logs','Maps','Sandbox','Cloud','steamapps','appcache','config','userdata')
$versionFiles = @(
    'Unturned.exe',
    'Unturned_Data/Managed/Assembly-CSharp.dll',
    'Unturned_Data/Managed/UnityEngine.dll',
    'Unturned_Data/Managed/UnityEngine.CoreModule.dll',
    'Unturned_Data/Managed/SDG.NetTransport.dll',
    'Unturned_Data/Managed/com.rlabrecque.steamworks.net.dll',
    'Modules/Rocket.Unturned/Rocket.API.dll',
    'Modules/Rocket.Unturned/Rocket.Core.dll',
    'Modules/Rocket.Unturned/Rocket.Unturned.dll'
)

function Assert-NoReparsePath([string]$Path, [bool]$AllowMissingLeaf = $false) {
    if ([String]::IsNullOrWhiteSpace($Path)) { throw 'Refusing an empty path.' }
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    $current = $root
    if ((Get-Item -LiteralPath $root -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse root refused: $root" }
    $parts = @($full.Substring($root.Length) -split '[\\/]+' | Where-Object { -not [String]::IsNullOrWhiteSpace($_) })
    for ($index=0; $index -lt $parts.Count; $index++) {
        $current = Join-Path $current $parts[$index]
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            if ($AllowMissingLeaf -and $index -eq ($parts.Count-1)) { continue }
            throw "Missing or unresolved path component: $current"
        }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $current" }
        if ($index -lt ($parts.Count-1) -and -not $item.PSIsContainer) { throw "Non-directory path component: $current" }
    }
    return $full
}

function Assert-NoReparseTree([string]$Root) {
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($entry in Get-ChildItem -LiteralPath $current -Force -ErrorAction Stop) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Nested reparse point refused: $($entry.FullName)" }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
        }
    }
}

function Read-KeyValueFile([string]$Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        if ($line -match '^([A-Za-z0-9_./-]+)=(.*)$') {
            if ($values.ContainsKey($Matches[1])) { throw "Duplicate manifest key: $($Matches[1])" }
            $values[$Matches[1]] = $Matches[2]
        } else { throw "Malformed manifest line in $Path" }
    }
    return $values
}

function Get-AppManifestValue([string]$Text, [string]$Name) {
    $pattern = '(?im)^\s*"' + [regex]::Escape($Name) + '"\s+"([^"]+)"'
    if ($Text -notmatch $pattern) { throw "SteamCMD appmanifest is missing $Name." }
    return $Matches[1]
}

function Assert-ManifestMatchesTree([string]$Root, [hashtable]$ExpectedHashes, [string]$Label) {
    Assert-NoReparsePath $Root | Out-Null
    Assert-NoReparseTree $Root
    $files = @(Get-ChildItem -LiteralPath $Root -Force -Recurse -File)
    if ($files.Count -ne $ExpectedHashes.Count) { throw "$Label file set does not exactly match its SHA-256 manifest." }
    $actual = @{}
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($Root.Length+1).Replace('\','/')
        if ($actual.ContainsKey($relative)) { throw "$Label contains duplicate normalized path: $relative" }
        $actual[$relative] = $file.FullName
    }
    foreach ($relative in $ExpectedHashes.Keys) {
        if (-not $actual.ContainsKey($relative)) { throw "$Label is missing manifest file: $relative" }
        $hash = (Get-FileHash -LiteralPath $actual[$relative] -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $ExpectedHashes[$relative]) { throw "$Label hash mismatch: $relative" }
    }
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + '\'
Assert-NoReparsePath ([IO.Path]::GetTempPath()) | Out-Null
$server = Assert-NoReparsePath $ServerRoot
$package = Assert-NoReparsePath $PackageDirectory
$work = Assert-NoReparsePath $WorkDirectory $true
if (-not $server.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'ServerRoot must be inside TEMP.' }
if (-not $package.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'PackageDirectory must be inside TEMP.' }
if (-not $work.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)) { throw 'WorkDirectory must be a new path inside TEMP.' }
if (-not (Test-Path -LiteralPath $server -PathType Container) -or -not (Test-Path -LiteralPath $package -PathType Container)) { throw 'ServerRoot and PackageDirectory must exist.' }
$server = (Resolve-Path -LiteralPath $server).Path
$package = (Resolve-Path -LiteralPath $package).Path
$work = [IO.Path]::GetFullPath($work)
Assert-NoReparseTree $server
Assert-NoReparseTree $package
foreach ($sourceRoot in @($server,$package)) {
    $sourcePrefix = $sourceRoot.TrimEnd('\','/') + '\'
    if ($work.Equals($sourceRoot,[StringComparison]::OrdinalIgnoreCase) -or $work.StartsWith($sourcePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'WorkDirectory cannot be inside ServerRoot or PackageDirectory.' }
}
$serverPrefix = $server.TrimEnd('\','/') + '\'
$packagePrefix = $package.TrimEnd('\','/') + '\'
if ($server.Equals($package,[StringComparison]::OrdinalIgnoreCase) -or
    $server.StartsWith($packagePrefix,[StringComparison]::OrdinalIgnoreCase) -or
    $package.StartsWith($serverPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'ServerRoot and PackageDirectory must be separate trees.' }

$exe = Join-Path $server 'Unturned.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'ServerRoot does not contain Unturned.exe.' }
$appManifestCandidates = @(
    (Join-Path $server 'steamapps\appmanifest_1110390.acf'),
    (Join-Path (Split-Path -Parent $server) 'steamapps\appmanifest_1110390.acf'),
    (Join-Path (Split-Path -Parent (Split-Path -Parent $server)) 'appmanifest_1110390.acf')
)
$appManifestPath = $appManifestCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $appManifestPath) { throw 'SteamCMD appmanifest_1110390.acf not found for ServerRoot.' }
$appManifestPath = Assert-NoReparsePath $appManifestPath
if (-not $appManifestPath.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Steam appmanifest must also be inside TEMP.' }
$appManifestText = Get-Content -LiteralPath $appManifestPath -Raw -Encoding UTF8
$appId = Get-AppManifestValue $appManifestText 'appid'
$stateFlags = Get-AppManifestValue $appManifestText 'StateFlags'
$updateResult = Get-AppManifestValue $appManifestText 'UpdateResult'
$sourceBuildId = Get-AppManifestValue $appManifestText 'buildid'
if ($appId -ne '1110390' -or $stateFlags -ne '4' -or $updateResult -ne '0' -or $sourceBuildId -notmatch '^\d+$') { throw 'SteamCMD appmanifest does not describe a fully installed, updated U3DS build.' }
if ($appManifestText -match '(?im)^\s*"TargetBuildID"\s+"([^"]+)"' -and $Matches[1] -ne $sourceBuildId) { throw 'SteamCMD appmanifest TargetBuildID differs from installed buildid.' }

$versionManifestPath = Join-Path $package 'server-version-manifest.txt'
$packageManifestPath = Join-Path $package 'manifest.txt'
if (-not (Test-Path -LiteralPath $versionManifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $packageManifestPath -PathType Leaf)) { throw 'Package lacks its source version manifest or package manifest.' }
$packageManifest = Read-KeyValueFile $packageManifestPath
$versionManifest = Read-KeyValueFile $versionManifestPath
if ($packageManifest.status -ne 'TEST_ONLY_NOT_INSTALLED' -or $packageManifest.server_id -ne $serverId -or
    $packageManifest.server_app_id -ne '1110390' -or $packageManifest.server_buildid -ne $sourceBuildId -or
    $versionManifest.status -ne 'TEST_ONLY' -or $versionManifest.app_id -ne '1110390' -or $versionManifest.buildid -ne $sourceBuildId) {
    throw 'Package TEST_ONLY identity or Steam buildid does not match ServerRoot.'
}
if ((Get-FileHash -LiteralPath $appManifestPath -Algorithm SHA256).Hash -ne $versionManifest.appmanifest_sha256) { throw 'Steam appmanifest SHA-256 does not match the source package.' }
if ((Get-FileHash -LiteralPath $versionManifestPath -Algorithm SHA256).Hash -ne $packageManifest.server_version_manifest_sha256) { throw 'Package source version manifest hash mismatch.' }
foreach ($relative in $versionFiles) {
    $key = 'sha256_' + $relative
    if (-not $versionManifest.ContainsKey($key)) { throw "Package version manifest is missing $key" }
    $sourceFile = Assert-NoReparsePath (Join-Path $server ($relative.Replace('/','\')))
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) { throw "ServerRoot missing versioned file: $relative" }
    $actual = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $versionManifest[$key]) { throw "ServerRoot version hash mismatch: $relative" }
}

$mapSource = Join-Path $package 'Maps\MREDecayTest'
$mapSums = Join-Path $package 'MREDecayTest-SHA256SUMS.txt'
$pluginSource = Join-Path $package 'Rocket\Plugins\MreDecayHudClientFixture\MreDecayHudClientFixture.dll'
if (-not (Test-Path -LiteralPath (Join-Path $mapSource 'Level.dat') -PathType Leaf) -or -not (Test-Path -LiteralPath $mapSums -PathType Leaf)) { throw 'Package does not contain the MREDecayTest map and checksum manifest.' }
if (-not (Test-Path -LiteralPath $pluginSource -PathType Leaf)) { throw 'Package does not contain the HUD visual fixture DLL.' }
$pluginHash = (Get-FileHash -LiteralPath $pluginSource -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pluginHash -ne $packageManifest.dll_sha256) { throw 'Package fixture DLL does not match manifest hash.' }
$expectedMapHashes = @{}
foreach ($line in Get-Content -LiteralPath $mapSums -Encoding ASCII) {
    if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Malformed map checksum manifest.' }
    $mapHash = $Matches[1].ToLowerInvariant()
    $relative = $Matches[2].Replace('\','/')
    $segments = @($relative.Split('/'))
    if ([String]::IsNullOrWhiteSpace($relative) -or $relative.Contains(':') -or [IO.Path]::IsPathRooted($relative) -or
        $segments -contains '..' -or $segments -contains '.' -or $segments -contains '') { throw 'Unsafe map checksum path.' }
    if ($expectedMapHashes.ContainsKey($relative)) { throw "Duplicate map checksum path: $relative" }
    $expectedMapHashes[$relative] = $mapHash
}
if ($expectedMapHashes.Count -eq 0) { throw 'Map checksum manifest is empty.' }
Assert-ManifestMatchesTree $mapSource $expectedMapHashes 'Package map'
Assert-NoReparsePath $pluginSource | Out-Null

try { $udpEndpoints = @(Get-NetUDPEndpoint -ErrorAction Stop) }
catch { throw 'Cannot enumerate UDP endpoints; refusing to prepare the server.' }
foreach ($candidate in @($Port,($Port+1),($Port+2))) {
    $endpoints = @($udpEndpoints | Where-Object { [int]$_.LocalPort -eq $candidate })
    if ($endpoints.Count -gt 0) { throw "UDP port $candidate is already in use." }
}
try { $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'" -ErrorAction Stop) }
catch { throw 'Cannot inspect Unturned.exe process paths; refusing to copy the source.' }
foreach ($process in $processes) {
    if ([String]::IsNullOrWhiteSpace($process.ExecutablePath)) { throw "Unturned.exe PID $($process.ProcessId) path is inaccessible; refusing to copy any server source." }
    try { $runningExe = [IO.Path]::GetFullPath($process.ExecutablePath) } catch { throw "Unturned.exe PID $($process.ProcessId) path is invalid; refusing to copy any server source." }
    if ($runningExe.Equals([IO.Path]::GetFullPath($exe),[StringComparison]::OrdinalIgnoreCase)) { throw "ServerRoot executable is running (PID $($process.ProcessId)); refusing to copy it." }
}

New-Item -ItemType Directory -Path $work | Out-Null
Assert-NoReparsePath $work | Out-Null
$instance = Join-Path $work 'instance'
New-Item -ItemType Directory -Path $instance | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $server -Force | Where-Object { $_.Name -notin $excluded }) {
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $($entry.FullName)" }
    Copy-Item -LiteralPath $entry.FullName -Destination $instance -Recurse
}
New-Item -ItemType Directory -Path (Join-Path $instance 'Maps') -Force | Out-Null
Copy-Item -LiteralPath $mapSource -Destination (Join-Path $instance 'Maps\MREDecayTest') -Recurse
$pluginDirectory = Join-Path $instance "Servers\$serverId\Rocket\Plugins\MreDecayHudClientFixture"
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
Copy-Item -LiteralPath $pluginSource -Destination $pluginDirectory
$serverConfig = Join-Path $instance "Servers\$serverId\Server"
New-Item -ItemType Directory -Path $serverConfig -Force | Out-Null
@('Name MRE Decay HUD Visual Test','Map MREDecayTest','Mode Easy','MaxPlayers 4',"Port $Port",'Perspective Both') | Set-Content -LiteralPath (Join-Path $serverConfig 'Commands.dat') -Encoding ASCII
Assert-NoReparseTree $instance
foreach ($relative in $versionFiles) {
    $copiedFile = Join-Path $instance ($relative.Replace('/','\'))
    if ((Get-FileHash -LiteralPath $copiedFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $versionManifest['sha256_' + $relative]) { throw "Copied server version hash mismatch: $relative" }
}
if ((Get-FileHash -LiteralPath (Join-Path $pluginDirectory 'MreDecayHudClientFixture.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pluginHash) { throw 'Copied fixture plugin hash mismatch.' }
Assert-ManifestMatchesTree (Join-Path $instance 'Maps\MREDecayTest') $expectedMapHashes 'Copied map'

$sumPath = Join-Path $work 'server-SHA256SUMS.txt'
$sumLines = foreach ($file in Get-ChildItem -LiteralPath $instance -Force -Recurse -File | Sort-Object FullName) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.FullName.Substring($instance.Length+1).Replace('\','/')
}
[IO.File]::WriteAllLines($sumPath, [string[]]$sumLines, (New-Object Text.UTF8Encoding($false)))
$receiptLines = @(
    'status=TEST_ONLY_NOT_STARTED',
    ('server_id=' + $serverId),
    'map=MREDecayTest',
    ('port=' + $Port),
    ('steam_app_id=1110390'),
    ('steam_buildid=' + $sourceBuildId),
    ('source_appmanifest_sha256=' + (Get-FileHash -LiteralPath $appManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()),
    ('source_version_manifest_sha256=' + (Get-FileHash -LiteralPath $versionManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()),
    ('server_sha256_manifest_sha256=' + (Get-FileHash -LiteralPath $sumPath -Algorithm SHA256).Hash.ToLowerInvariant()),
    ('package_manifest_sha256=' + (Get-FileHash -LiteralPath $packageManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()),
    ('fixture_dll_sha256=' + $pluginHash),
    ('source=' + $server),
    ('package=' + $package)
)
[IO.File]::WriteAllLines((Join-Path $work 'prepare-receipt.txt'), $receiptLines, (New-Object Text.UTF8Encoding($false)))
Write-Output "TEST_ONLY server prepared and NOT STARTED: $work"
Write-Output 'Receipt: prepare-receipt.txt; SHA-256 manifest: server-SHA256SUMS.txt'
