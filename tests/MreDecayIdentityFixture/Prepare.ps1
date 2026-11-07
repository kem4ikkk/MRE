[CmdletBinding()]
param(
    [string]$ServerSource,
    [Parameter(Mandatory=$true)][string]$Collection,
    [Parameter(Mandatory=$true)][string]$MapSource,
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-decay-ground-identity-' + [guid]::NewGuid().ToString('N'))),
    [ValidateRange(20000,60000)][int]$Port = 28194
)
$ErrorActionPreference = 'Stop'
if ([String]::IsNullOrWhiteSpace($ServerSource)) { $ServerSource = Join-Path $PSScriptRoot '..\..\runtime\server' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = [IO.Path]::GetFullPath($ServerSource)
$collectionPath = [IO.Path]::GetFullPath($Collection)
$mapInput = [IO.Path]::GetFullPath($MapSource)
$work = [IO.Path]::GetFullPath($WorkDirectory)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$serverId = 'MREDecayGroundIdentityFixture'
function Assert-NoReparsePath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path); $drive=[IO.Path]::GetPathRoot($full); $current=$drive
    foreach($part in $full.Substring($drive.Length).Split([char[]]@([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar),[StringSplitOptions]::RemoveEmptyEntries)) {
        $current=Join-Path $current $part
        if(Test-Path -LiteralPath $current) { $item=Get-Item -LiteralPath $current -Force; if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse-point path component refused: $current" } }
    }
}
function Assert-NoReparseTree([string]$Path) {
    Assert-NoReparsePath $Path
    $stack=New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push([IO.Path]::GetFullPath($Path))
    while($stack.Count -gt 0) {
        $current=$stack.Pop()
        if(-not (Test-Path -LiteralPath $current)) { throw "Expected path is missing: $current" }
        $item=Get-Item -LiteralPath $current -Force
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $current" }
        if($item.PSIsContainer) {
            foreach($child in Get-ChildItem -LiteralPath $current -Force) {
                if($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Nested reparse point refused: $($child.FullName)" }
                if($child.PSIsContainer) { $stack.Push($child.FullName) }
            }
        }
    }
}
Assert-NoReparsePath $tempRoot
Assert-NoReparsePath $source
Assert-NoReparseTree $collectionPath
Assert-NoReparseTree $mapInput
Assert-NoReparsePath (Split-Path -Parent $work)
if (-not $work.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)) { throw 'WorkDirectory must be a new canonical path inside TEMP.' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'Unturned.exe') -PathType Leaf)) { throw 'ServerSource must contain Unturned.exe.' }
if (-not $source.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and $source -ne [IO.Path]::GetFullPath((Join-Path $repo 'runtime/server'))) { throw 'ServerSource must be repository runtime/server or a TEMP copy.' }
if (((Get-FileHash (Join-Path $repo 'runtime/server/Unturned_Data/Managed/Assembly-CSharp.dll') -Algorithm SHA256).Hash) -ne
    ((Get-FileHash (Join-Path $source 'Unturned_Data/Managed/Assembly-CSharp.dll') -Algorithm SHA256).Hash)) { throw 'ServerSource Assembly-CSharp does not match this project runtime.' }
if (-not (Test-Path -LiteralPath (Join-Path $collectionPath 'Items/Safe100/Safe100.dat') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $collectionPath 'Items/Safe100/Safe100.unity3d') -PathType Leaf)) { throw 'Collection is missing the required MRE Safe100 asset files.' }
$safeDat = Get-Content -LiteralPath (Join-Path $collectionPath 'Items/Safe100/Safe100.dat') -Raw -Encoding UTF8
if ($safeDat -notmatch '(?m)^GUID bf233036b60d403c836deb8ac0f03292\s*$' -or $safeDat -notmatch '(?m)^ID 60971\s*$' -or $safeDat -notmatch '(?m)^Type Storage\s*$') { throw 'Safe100 package DAT does not match the required MRE safe ID/GUID/type.' }
if (-not (Test-Path -LiteralPath (Join-Path $mapInput 'Level.dat') -PathType Leaf) -or (Test-Path -LiteralPath (Join-Path $mapInput 'Bundles'))) { throw 'MapSource must be an extracted map without Bundles.' }
foreach ($candidate in @($Port,($Port+1),($Port+2))) { if (Get-NetUDPEndpoint -LocalPort $candidate -ErrorAction SilentlyContinue) { throw "UDP port $candidate is already in use." } }
try { $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'" -ErrorAction Stop) }
catch { throw 'Cannot inspect Unturned.exe process paths; refusing all server copies.' }
foreach ($p in $processes) {
    if ([String]::IsNullOrWhiteSpace($p.ExecutablePath)) { throw "Unturned.exe PID $($p.ProcessId) path is inaccessible; refusing all server copies." }
    try { $runningExe=[IO.Path]::GetFullPath($p.ExecutablePath) } catch { throw "Unturned.exe PID $($p.ProcessId) path is invalid; refusing all server copies." }
    if ($runningExe.Equals([IO.Path]::GetFullPath((Join-Path $source 'Unturned.exe')),[StringComparison]::OrdinalIgnoreCase)) { throw 'ServerSource is running; refusing to copy it.' }
}
New-Item -ItemType Directory -Path $work | Out-Null
Assert-NoReparsePath $work
$mapStage = Join-Path $work 'map-stage'
& (Join-Path $PSScriptRoot '..\..\scripts\Build-MreDecaySafeTestMap.ps1') -Collection $collectionPath -MapSource $mapInput -WorkDirectory $mapStage
$stagedMap = Join-Path $mapStage 'MREDecayTest'
Assert-NoReparseTree $mapStage
$safeStagedDat = Join-Path $stagedMap 'Bundles/Items/Safe100/Safe100.dat'
if (-not (Test-Path -LiteralPath $safeStagedDat -PathType Leaf) -or (Get-FileHash -LiteralPath $safeStagedDat -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $collectionPath 'Items/Safe100/Safe100.dat') -Algorithm SHA256).Hash) { throw 'Staged map does not contain the exact packaged MRE Safe100 DAT.' }
$safeStagedText = Get-Content -LiteralPath $safeStagedDat -Raw -Encoding UTF8
if ($safeStagedText -notmatch '(?m)^GUID bf233036b60d403c836deb8ac0f03292\s*$' -or $safeStagedText -notmatch '(?m)^ID 60971\s*$') { throw 'Staged map Safe100 identity check failed.' }
$instance = Join-Path $work 'instance'; New-Item -ItemType Directory -Path $instance | Out-Null
$excluded = @('Servers','Logs','Maps','Sandbox','Cloud','steamapps','appcache','config','userdata')
foreach ($entry in @(Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -notin $excluded })) {
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $($entry.FullName)" }
    if ($entry.PSIsContainer -and (Get-ChildItem -LiteralPath $entry.FullName -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1)) { throw "Nested reparse point refused: $($entry.FullName)" }
    Copy-Item -LiteralPath $entry.FullName -Destination $instance -Recurse
}
New-Item -ItemType Directory -Path (Join-Path $instance 'Maps') | Out-Null
Copy-Item -LiteralPath $stagedMap -Destination (Join-Path $instance 'Maps/MREDecayTest') -Recurse
Assert-NoReparsePath $instance
Assert-NoReparseTree $instance
foreach ($bundleFile in Get-ChildItem -LiteralPath (Join-Path $stagedMap 'Bundles') -Recurse -File) {
    $relative=$bundleFile.FullName.Substring((Join-Path $stagedMap 'Bundles').Length+1)
    $copied=Join-Path (Join-Path $instance 'Maps/MREDecayTest/Bundles') $relative
    if (-not (Test-Path -LiteralPath $copied -PathType Leaf) -or (Get-FileHash -LiteralPath $bundleFile.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash) { throw "Test map Bundle copy mismatch: $relative" }
}

$pluginDir = Join-Path $instance "Servers\$serverId\Rocket\Plugins\$serverId"
New-Item -ItemType Directory -Path $pluginDir,(Join-Path $instance "Servers\$serverId\Server") -Force | Out-Null
$managed = Join-Path $instance 'Unturned_Data\Managed'
$rocket = Join-Path $instance 'Modules\Rocket.Unturned'
$escapedFixture = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($PSScriptRoot))
$escapedManaged = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($managed))
$escapedRocket = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($rocket))
$escapedRepo = [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($repo))
$project = Join-Path $work 'GroundIdentityFixture.csproj'
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>$serverId</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup>
<Compile Include="$escapedFixture/GroundIdentityReloadFixture.cs"/><Compile Include="$escapedRepo/src/MreDecay/Adapter/ObjectObservation.cs"/><Compile Include="$escapedRepo/src/MreDecay/Adapter/ObservationAudit.cs"/><Compile Include="$escapedRepo/src/MreDecay/Adapter/ReadOnlyWorld.cs"/>
<Reference Include="Assembly-CSharp"><HintPath>$escapedManaged/Assembly-CSharp.dll</HintPath></Reference><Reference Include="Steamworks"><HintPath>$escapedManaged/com.rlabrecque.steamworks.net.dll</HintPath></Reference><Reference Include="UnityEngine"><HintPath>$escapedManaged/UnityEngine.dll</HintPath></Reference><Reference Include="UnityEngine.CoreModule"><HintPath>$escapedManaged/UnityEngine.CoreModule.dll</HintPath></Reference><Reference Include="Rocket.API"><HintPath>$escapedRocket/Rocket.API.dll</HintPath></Reference><Reference Include="Rocket.Core"><HintPath>$escapedRocket/Rocket.Core.dll</HintPath></Reference>
</ItemGroup></Project>
"@
[IO.File]::WriteAllText($project,$projectText,(New-Object Text.UTF8Encoding($false)))
dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Fixture compile-only build failed.' }
$dll = Join-Path $work ("bin/Release/netstandard2.1/$serverId.dll")
Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDir ("$serverId.dll"))
@('Name MRE Ground Identity Fixture','Map MREDecayTest','Mode Easy','MaxPlayers 4',"Port $Port",'Perspective Both') | Set-Content (Join-Path $instance "Servers\$serverId\Server\Commands.dat") -Encoding ASCII
$config = @"
<?xml version="1.0" encoding="utf-8"?>
<GroundIdentityConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"><Enabled>true</Enabled><Mode>create</Mode></GroundIdentityConfiguration>
"@
[IO.File]::WriteAllText((Join-Path $pluginDir "$serverId.configuration.xml"),$config,(New-Object Text.UTF8Encoding($false)))
$receipt = Join-Path $work 'prepare-receipt.txt'
$lines = @("server_id=$serverId","port=$Port","map=MREDecayTest","source=$source","instance=$instance")
foreach ($file in @((Join-Path $instance 'Unturned.exe'),(Join-Path $instance 'Unturned_Data/Managed/Assembly-CSharp.dll'),(Join-Path $pluginDir "$serverId.dll"),$project,$safeStagedDat,(Join-Path $instance 'Maps/MREDecayTest/Bundles/Items/Safe100/Safe100.unity3d'))) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing fixture artifact: $file" }
    $lines += "sha256=$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash) $file"
}
[IO.File]::WriteAllLines($receipt,$lines,(New-Object Text.UTF8Encoding($false)))
Assert-NoReparseTree $work
Write-Output "Prepared compile-ready isolated fixture in TEMP only; server not started: $work"
Write-Output "Receipt: $receipt"
