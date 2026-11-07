[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerRoot,
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-decay-hud-client-fixture-' + [guid]::NewGuid().ToString('N'))),
    [string]$Collection,
    [string]$MapSource
)

$ErrorActionPreference = 'Stop'

function Assert-NoReparsePath([string]$Path, [bool]$AllowMissingLeaf = $false) {
    if ([String]::IsNullOrWhiteSpace($Path)) { throw 'Refusing an empty path.' }
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    if ([String]::IsNullOrWhiteSpace($root)) { throw "Cannot resolve path root: $Path" }
    $parts = @($full.Substring($root.Length) -split '[\\/]+' | Where-Object { -not [String]::IsNullOrWhiteSpace($_) })
    $current = $root
    $rootItem = Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse root refused: $root" }
    for ($index = 0; $index -lt $parts.Count; $index++) {
        $current = Join-Path $current $parts[$index]
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            if ($AllowMissingLeaf -and $index -eq ($parts.Count - 1)) { continue }
            throw "Missing or unresolved path component: $current"
        }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $current" }
        if ($index -lt ($parts.Count - 1) -and -not $item.PSIsContainer) { throw "Non-directory path component: $current" }
    }
    return $full
}

function Assert-NoReparseTree([string]$Root) {
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse point inside package input refused: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
        }
    }
}

function Get-AppManifestValue([string]$Text, [string]$Name) {
    $pattern = '(?im)^\s*"' + [regex]::Escape($Name) + '"\s+"([^"]+)"'
    if ($Text -notmatch $pattern) { throw "SteamCMD appmanifest is missing $Name." }
    return $Matches[1]
}

$repo = Assert-NoReparsePath (Join-Path $PSScriptRoot '..\..')
$repo = (Resolve-Path -LiteralPath $repo).Path
$tempRoot = Assert-NoReparsePath ([IO.Path]::GetTempPath())
$temp = $tempRoot.TrimEnd('\','/') + '\'
$server = Assert-NoReparsePath $ServerRoot
$server = (Resolve-Path -LiteralPath $server).Path
$work = Assert-NoReparsePath $WorkDirectory $true
if (-not $work.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)) {
    throw 'WorkDirectory must be a new path inside a verified TEMP tree.'
}
if (-not $server.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) { throw 'ServerRoot must be an installed U3DS copy inside TEMP.' }
$serverPrefix = $server.TrimEnd('\','/') + '\'
if ($work.StartsWith($serverPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Build WorkDirectory cannot be inside ServerRoot.' }
if (($Collection -and -not $MapSource) -or ($MapSource -and -not $Collection)) {
    throw 'Collection and MapSource must be supplied together.'
}
if ($Collection) {
    $Collection = Assert-NoReparsePath $Collection
    if (-not (Test-Path -LiteralPath $Collection -PathType Container)) { throw 'Collection must be an existing directory.' }
    $Collection = (Resolve-Path -LiteralPath $Collection).Path
    Assert-NoReparseTree $Collection
}
if ($MapSource) {
    $MapSource = Assert-NoReparsePath $MapSource
    if (-not (Test-Path -LiteralPath $MapSource -PathType Container)) { throw 'MapSource must be an existing directory.' }
    $MapSource = (Resolve-Path -LiteralPath $MapSource).Path
    Assert-NoReparseTree $MapSource
}
foreach ($inputRoot in @($Collection,$MapSource)) {
    if ([String]::IsNullOrWhiteSpace($inputRoot)) { continue }
    $inputPrefix = $inputRoot.TrimEnd('\','/') + '\'
    if ($work.Equals($inputRoot,[StringComparison]::OrdinalIgnoreCase) -or $work.StartsWith($inputPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Build WorkDirectory cannot be equal to or inside Collection/MapSource.' }
}
$managed = Join-Path $server 'Unturned_Data\Managed'
$rocket = Join-Path $server 'Modules\Rocket.Unturned'
$required = @(
    (Join-Path $server 'Unturned.exe'),
    (Join-Path $managed 'Assembly-CSharp.dll'),
    (Join-Path $managed 'UnityEngine.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'SDG.NetTransport.dll'),
    (Join-Path $managed 'com.rlabrecque.steamworks.net.dll'),
    (Join-Path $rocket 'Rocket.API.dll'),
    (Join-Path $rocket 'Rocket.Core.dll'),
    (Join-Path $rocket 'Rocket.Unturned.dll')
)
foreach ($path in $required) {
    $safePath = Assert-NoReparsePath $path
    if (-not (Test-Path -LiteralPath $safePath -PathType Leaf)) { throw "Missing server build reference: $safePath" }
}

New-Item -ItemType Directory -Path $work | Out-Null
$source = Assert-NoReparsePath (Join-Path $PSScriptRoot 'DecayHudVisualFixture.cs')
$source = (Resolve-Path -LiteralPath $source).Path
$safeSource = [Security.SecurityElement]::Escape($source)
$safeManaged = [Security.SecurityElement]::Escape($managed)
$safeRocket = [Security.SecurityElement]::Escape($rocket)
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>MreDecayHudClientFixture</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>7.3</LangVersion></PropertyGroup>
  <ItemGroup>
    <Compile Include="$safeSource" />
    <Reference Include="Assembly-CSharp"><HintPath>$safeManaged/Assembly-CSharp.dll</HintPath></Reference>
    <Reference Include="UnityEngine"><HintPath>$safeManaged/UnityEngine.dll</HintPath></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$safeManaged/UnityEngine.CoreModule.dll</HintPath></Reference>
    <Reference Include="SDG.NetTransport"><HintPath>$safeManaged/SDG.NetTransport.dll</HintPath></Reference>
    <Reference Include="com.rlabrecque.steamworks.net"><HintPath>$safeManaged/com.rlabrecque.steamworks.net.dll</HintPath></Reference>
    <Reference Include="Rocket.API"><HintPath>$safeRocket/Rocket.API.dll</HintPath></Reference>
    <Reference Include="Rocket.Core"><HintPath>$safeRocket/Rocket.Core.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $work 'MreDecayHudClientFixture.csproj'
[IO.File]::WriteAllText($projectPath, $project, (New-Object Text.UTF8Encoding($false)))
dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'HUD test fixture compile-only build failed.' }

$package = Join-Path $work 'package'
$pluginPackage = Join-Path $package 'Rocket\Plugins\MreDecayHudClientFixture'
New-Item -ItemType Directory -Path $pluginPackage -Force | Out-Null
$dll = Join-Path $work 'bin\Release\netstandard2.1\MreDecayHudClientFixture.dll'
Copy-Item -LiteralPath $dll -Destination $pluginPackage
$manifest = @(
    'status=TEST_ONLY_NOT_INSTALLED',
    'server_id=MREDecayHudVisualTest',
    'map=MREDecayTest',
    'environment=MRE_DECAY_HUD_VISUAL_TEST=YES',
    'effect_id=48623',
    'effect_guid=5d4628c25c73441a89ef5aed4db47713',
    'effect_key=14823',
    'shows_only_to_exactly_two_clients=True',
    'uses_production_decay_status=False',
    ('dll_sha256=' + (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant())
)
[string[]]$versionFiles = @(
    'Unturned.exe',
    'Unturned_Data\Managed\Assembly-CSharp.dll',
    'Unturned_Data\Managed\UnityEngine.dll',
    'Unturned_Data\Managed\UnityEngine.CoreModule.dll',
    'Unturned_Data\Managed\SDG.NetTransport.dll',
    'Unturned_Data\Managed\com.rlabrecque.steamworks.net.dll',
    'Modules\Rocket.Unturned\Rocket.API.dll',
    'Modules\Rocket.Unturned\Rocket.Core.dll',
    'Modules\Rocket.Unturned\Rocket.Unturned.dll'
)
$appManifestCandidates = @(
    (Join-Path $server 'steamapps\appmanifest_1110390.acf'),
    (Join-Path (Split-Path -Parent $server) 'steamapps\appmanifest_1110390.acf'),
    (Join-Path (Split-Path -Parent (Split-Path -Parent $server)) 'appmanifest_1110390.acf')
)
$appManifestPath = $appManifestCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $appManifestPath) { throw 'SteamCMD appmanifest_1110390.acf was not found beside ServerRoot; build package from an installed U3DS tree.' }
$appManifestPath = Assert-NoReparsePath $appManifestPath
if (-not $appManifestPath.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)) { throw 'SteamCMD appmanifest must also be inside TEMP.' }
$appManifestText = Get-Content -LiteralPath $appManifestPath -Raw -Encoding UTF8
$appId = Get-AppManifestValue $appManifestText 'appid'
$stateFlags = Get-AppManifestValue $appManifestText 'StateFlags'
$updateResult = Get-AppManifestValue $appManifestText 'UpdateResult'
$buildId = Get-AppManifestValue $appManifestText 'buildid'
if ($appId -ne '1110390' -or $stateFlags -ne '4' -or $updateResult -ne '0' -or $buildId -notmatch '^\d+$') { throw 'SteamCMD appmanifest does not describe a fully installed, updated U3DS build.' }
if ($appManifestText -match '(?im)^\s*"TargetBuildID"\s+"([^"]+)"' -and $Matches[1] -ne $buildId) { throw 'SteamCMD appmanifest TargetBuildID differs from installed buildid.' }
$versionLines = @('status=TEST_ONLY', 'app_id=1110390', ('buildid=' + $buildId), ('appmanifest_sha256=' + (Get-FileHash -LiteralPath $appManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()))
foreach ($relative in $versionFiles) {
    $file = Assert-NoReparsePath (Join-Path $server $relative)
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing versioned server file: $relative" }
    $versionLines += ('sha256_{0}={1}' -f $relative.Replace('\','/'), (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())
}
$versionManifestPath = Join-Path $package 'server-version-manifest.txt'
[IO.File]::WriteAllLines($versionManifestPath, $versionLines, (New-Object Text.UTF8Encoding($false)))
$manifest += @('server_app_id=1110390', ('server_buildid=' + $buildId), ('server_version_manifest_sha256=' + (Get-FileHash -LiteralPath $versionManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()))
[IO.File]::WriteAllLines((Join-Path $package 'manifest.txt'), $manifest, (New-Object Text.UTF8Encoding($false)))

if ($Collection) {
    $mapStager = Join-Path $repo 'scripts\Build-MreDecaySafeTestMap.ps1'
    $mapStage = Join-Path $work 'map-stage'
    & $mapStager -Collection $Collection -MapSource $MapSource -WorkDirectory $mapStage
    if ($LASTEXITCODE -ne 0) { throw 'MREDecayTest map staging failed.' }
    $mapPackage = Join-Path $package 'Maps'
    New-Item -ItemType Directory -Path $mapPackage -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $mapStage 'MREDecayTest') -Destination $mapPackage -Recurse
    Copy-Item -LiteralPath (Join-Path $mapStage 'SHA256SUMS.txt') -Destination (Join-Path $package 'MREDecayTest-SHA256SUMS.txt')
}

Write-Output "TEST ONLY package: $package"
Write-Output 'Not installed or started. Copy only into separately prepared TEMP server/client test copies.'
