[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$managed = Join-Path $root 'runtime/server/Unturned_Data/Managed'
$rocket = Join-Path $root 'runtime/server/Modules/Rocket.Unturned'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be inside TEMP.' }
if (Test-Path -LiteralPath $output) { throw 'OutputDirectory must not already exist.' }
New-Item -ItemType Directory -Path $output | Out-Null
function Escape-Xml([string]$value) { return [Security.SecurityElement]::Escape($value) }
$source = Escape-Xml $PSScriptRoot
$managed = Escape-Xml $managed
$rocket = Escape-Xml $rocket
$project = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>
<Compile Include="$source/../DecayModel.cs"/><Compile Include="$source/../FractionalHealth.cs"/><Compile Include="$source/../ProtectionSearch.cs"/><Compile Include="$source/../Zones/PolygonZone.cs"/><Compile Include="$source/../Engine/StepEngine.cs"/><Compile Include="$source/../Engine/RuntimeGate.cs"/><Compile Include="$source/../Engine/DecayRuntimePipeline.cs"/><Compile Include="$source/ProbeTimer.cs"/><Compile Include="$source/DecayRuntimePlugin.cs"/>
<Reference Include="Assembly-CSharp"><HintPath>$managed/Assembly-CSharp.dll</HintPath></Reference>
<Reference Include="Steamworks"><HintPath>$managed/com.rlabrecque.steamworks.net.dll</HintPath></Reference>
<Reference Include="UnityEngine"><HintPath>$managed/UnityEngine.dll</HintPath></Reference>
<Reference Include="UnityEngine.CoreModule"><HintPath>$managed/UnityEngine.CoreModule.dll</HintPath></Reference>
<Reference Include="Rocket.API"><HintPath>$rocket/Rocket.API.dll</HintPath></Reference>
<Reference Include="Rocket.Core"><HintPath>$rocket/Rocket.Core.dll</HintPath></Reference>
</ItemGroup></Project>
"@
$projectPath = Join-Path $output 'MreDecay.Runtime.csproj'
[IO.File]::WriteAllText($projectPath, $project, (New-Object Text.UTF8Encoding($false)))
dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Runtime plugin build failed.' }
