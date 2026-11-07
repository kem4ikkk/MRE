[CmdletBinding()]
param(
    [string]$BarricadeAssetGuid = 'a8c7f87176d547d8ba34167836f16054',
    [ValidateRange(1, 65535)][int]$VehicleAssetId = 185,
    [ValidateRange(20000, 60000)][int]$Port = 28191,
    [string]$ServerSource = (Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime/server'),
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-decay-vehicle-reload-' + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = (Resolve-Path -LiteralPath $ServerSource).Path
$work = [IO.Path]::GetFullPath($WorkDirectory)
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$serverId = 'MREDecayVehicleReloadFixture'
if (-not $work.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)) { throw 'WorkDirectory must be a new TEMP directory.' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'Unturned.exe') -PathType Leaf)) { throw 'ServerSource must contain Unturned.exe.' }
if (-not $source.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -and $source -ne [IO.Path]::GetFullPath((Join-Path $root 'runtime/server'))) { throw 'Non-installation ServerSource must be inside TEMP.' }
$parsedBarricadeGuid = [guid]::Empty
if (-not [guid]::TryParse($BarricadeAssetGuid, [ref]$parsedBarricadeGuid) -or $parsedBarricadeGuid -eq [guid]::Empty) { throw 'BarricadeAssetGuid must be a nonempty GUID.' }
foreach ($candidatePort in @($Port, ($Port + 1), ($Port + 2))) {
    if (Get-NetUDPEndpoint -LocalPort $candidatePort -ErrorAction SilentlyContinue) { throw "UDP port $candidatePort is in use." }
}
foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'")) {
    $exe = $null
    try { $exe = $p.ExecutablePath } catch { }
    if ($exe -and [IO.Path]::GetFullPath($exe).StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source U3DS is running; refusing to copy it.' }
}
$expectedAssembly = (Get-FileHash -LiteralPath (Join-Path $root 'runtime/server/Unturned_Data/Managed/Assembly-CSharp.dll') -Algorithm SHA256).Hash
$sourceAssembly = (Get-FileHash -LiteralPath (Join-Path $source 'Unturned_Data/Managed/Assembly-CSharp.dll') -Algorithm SHA256).Hash
if ($sourceAssembly -ne $expectedAssembly) { throw 'ServerSource Assembly-CSharp does not match the installed runtime.' }

$excluded = @('Servers', 'Logs', 'Maps', 'Sandbox', 'Cloud', 'steamapps', 'appcache', 'config', 'userdata')
$entries = @(Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -notin $excluded })
foreach ($entry in $entries) {
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Source reparse point refused: $($entry.FullName)" }
    if ($entry.PSIsContainer -and (Get-ChildItem -LiteralPath $entry.FullName -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1)) { throw "Reparse point under source entry: $($entry.FullName)" }
}

New-Item -ItemType Directory -Path $work | Out-Null
$instance = Join-Path $work 'instance'
New-Item -ItemType Directory -Path $instance | Out-Null
foreach ($entry in $entries) { Copy-Item -LiteralPath $entry.FullName -Destination $instance -Recurse }
New-Item -ItemType Directory -Path (Join-Path $instance 'Maps') | Out-Null
$peiSource = Join-Path $source 'Maps/PEI'
if (Get-ChildItem -LiteralPath $peiSource -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) { throw 'PEI map reparse point refused.' }
Copy-Item -LiteralPath $peiSource -Destination (Join-Path $instance 'Maps') -Recurse

$fixtureDir = Join-Path $instance ("Servers\$serverId\Rocket\Plugins\$serverId")
$pluginDir = Split-Path -Parent $fixtureDir
New-Item -ItemType Directory -Path (Join-Path $instance "Servers\$serverId\Server"), $fixtureDir -Force | Out-Null
$managed = Join-Path $instance 'Unturned_Data\Managed'
$rocket = Join-Path $instance 'Modules\Rocket.Unturned'
$project = Join-Path $work 'VehicleReloadFixture.csproj'
$sourceSnapshot = Join-Path $work 'source'
New-Item -ItemType Directory -Path $sourceSnapshot | Out-Null
$sourceFiles = @('ObjectObservation.cs', 'ObservationAudit.cs', 'ReadOnlyWorld.cs', 'VehicleReloadFixture.cs')
foreach ($file in $sourceFiles) { Copy-Item -LiteralPath (Join-Path $root "src/MreDecay/Adapter/$file") -Destination (Join-Path $sourceSnapshot $file) }
$escapedSource = [Security.SecurityElement]::Escape($sourceSnapshot)
$escapedManaged = [Security.SecurityElement]::Escape($managed)
$escapedRocket = [Security.SecurityElement]::Escape($rocket)
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>$serverId</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup>
<Compile Include="$escapedSource/ObjectObservation.cs"/><Compile Include="$escapedSource/ObservationAudit.cs"/><Compile Include="$escapedSource/ReadOnlyWorld.cs"/><Compile Include="$escapedSource/VehicleReloadFixture.cs"/>
<Reference Include="Assembly-CSharp"><HintPath>$escapedManaged/Assembly-CSharp.dll</HintPath></Reference><Reference Include="Steamworks"><HintPath>$escapedManaged/com.rlabrecque.steamworks.net.dll</HintPath></Reference><Reference Include="UnityEngine"><HintPath>$escapedManaged/UnityEngine.dll</HintPath></Reference><Reference Include="UnityEngine.CoreModule"><HintPath>$escapedManaged/UnityEngine.CoreModule.dll</HintPath></Reference><Reference Include="Rocket.API"><HintPath>$escapedRocket/Rocket.API.dll</HintPath></Reference><Reference Include="Rocket.Core"><HintPath>$escapedRocket/Rocket.Core.dll</HintPath></Reference>
</ItemGroup></Project>
"@
[IO.File]::WriteAllText($project, $projectText, (New-Object Text.UTF8Encoding($false)))
dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Fixture plugin build failed.' }
$dll = Join-Path $work ("bin/Release/netstandard2.1/" + $serverId + '.dll')
if (-not (Test-Path -LiteralPath $dll)) { throw 'Fixture DLL was not produced.' }
Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDir ($serverId + '.dll'))

@("Name MRE Decay Vehicle Reload Fixture", 'Map PEI', 'Mode Easy', 'MaxPlayers 4', "Port $Port", 'Perspective Both') | Set-Content -LiteralPath (Join-Path $instance "Servers\$serverId\Server\Commands.dat") -Encoding ASCII
$safeGuid = $parsedBarricadeGuid.ToString('D')
$config = @"
<?xml version="1.0" encoding="utf-8"?>
<VehicleReloadConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Enabled>true</Enabled><Mode>attach</Mode><VehicleAssetId>$VehicleAssetId</VehicleAssetId><BarricadeAssetGuid>$safeGuid</BarricadeAssetGuid>
</VehicleReloadConfiguration>
"@
[IO.File]::WriteAllText((Join-Path $fixtureDir ($serverId + '.configuration.xml')), $config, (New-Object Text.UTF8Encoding($false)))
$receipt = Join-Path $work 'prepare-receipt.txt'
$items = @((Join-Path $instance 'Unturned.exe'), (Join-Path $instance 'Unturned_Data/Managed/Assembly-CSharp.dll'), (Join-Path $pluginDir ($serverId + '.dll')))
$lines = @("server_id=$serverId", "port=$Port", "vehicle_asset_id=$VehicleAssetId", "barricade_asset_guid=$safeGuid", "source_runtime=$source", "instance=$instance")
foreach ($item in $items) {
    if (Test-Path -LiteralPath $item -PathType Leaf) { $hash = (Get-FileHash -LiteralPath $item -Algorithm SHA256).Hash; $lines += "sha256=$hash $item" }
    else { throw "Missing fixture artifact: $item" }
}
foreach ($file in $sourceFiles) { $path = Join-Path $sourceSnapshot $file; $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; $lines += "sha256=$hash source/$file" }
$mapFiles = @(Get-ChildItem -LiteralPath (Join-Path $instance 'Maps/PEI') -Recurse -File | Sort-Object FullName)
foreach ($file in $mapFiles) { $rel = $file.FullName.Substring((Join-Path $instance 'Maps/PEI').Length + 1); $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash; $lines += "sha256=$hash Maps/PEI/$rel" }
[IO.File]::WriteAllLines($receipt, $lines, (New-Object Text.UTF8Encoding($false)))
Write-Output "Prepared isolated fixture only; server not started. Instance: $instance"
Write-Output "Receipt: $receipt"
