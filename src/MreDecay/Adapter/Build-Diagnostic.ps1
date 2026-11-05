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
<Compile Include="$source/ObjectObservation.cs"/><Compile Include="$source/ObservationAudit.cs"/><Compile Include="$source/ReadOnlyWorld.cs"/><Compile Include="$source/DiagnosticPlugin.cs"/>
<Reference Include="Assembly-CSharp"><HintPath>$managed/Assembly-CSharp.dll</HintPath></Reference>
<Reference Include="UnityEngine"><HintPath>$managed/UnityEngine.dll</HintPath></Reference>
<Reference Include="UnityEngine.CoreModule"><HintPath>$managed/UnityEngine.CoreModule.dll</HintPath></Reference>
<Reference Include="Rocket.API"><HintPath>$rocket/Rocket.API.dll</HintPath></Reference>
<Reference Include="Rocket.Core"><HintPath>$rocket/Rocket.Core.dll</HintPath></Reference>
</ItemGroup></Project>
"@
$projectPath = Join-Path $output 'Diagnostic.csproj'
[IO.File]::WriteAllText($projectPath, $project, (New-Object Text.UTF8Encoding($false)))
dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic build failed.' }
