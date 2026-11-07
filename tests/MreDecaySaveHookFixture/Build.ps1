[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ServerRoot,
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-decay-save-hook-build-' + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$appIdExpected = '1110390'
$buildIdExpected = '26491864'
$files = @(
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
$fixtureSources = @(
    (Join-Path $PSScriptRoot 'SaveHookFixture.cs'),
    (Join-Path $PSScriptRoot '..\..\src\MreDecay\Adapter\ObjectObservation.cs'),
    (Join-Path $PSScriptRoot '..\..\src\MreDecay\Adapter\ObservationAudit.cs'),
    (Join-Path $PSScriptRoot '..\..\src\MreDecay\Adapter\ReadOnlyWorld.cs')
)

function Assert-NoReparsePath([string]$Path, [bool]$AllowMissingLeaf = $false) {
    if ([String]::IsNullOrWhiteSpace($Path)) { throw 'Empty path refused.' }
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    $current = $root
    if ((Get-Item -LiteralPath $root -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse volume root refused: $root" }
    $parts = @($full.Substring($root.Length) -split '[\\/]+' | Where-Object { -not [String]::IsNullOrWhiteSpace($_) })
    for ($i=0; $i -lt $parts.Count; $i++) {
        $current = Join-Path $current $parts[$i]
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            if ($AllowMissingLeaf -and $i -eq ($parts.Count - 1)) { continue }
            throw "Missing path component: $current"
        }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $current" }
        if ($i -lt ($parts.Count - 1) -and -not $item.PSIsContainer) { throw "Non-directory path component: $current" }
    }
    return $full
}

function Get-AppManifestValue([string]$Text, [string]$Name) {
    $pattern = '(?im)^\s*"' + [regex]::Escape($Name) + '"\s+"([^"]+)"'
    if ($Text -notmatch $pattern) { throw "Steam appmanifest missing $Name." }
    return $Matches[1]
}

$temp = (Assert-NoReparsePath ([IO.Path]::GetTempPath())).TrimEnd('\','/') + '\'
$server = Assert-NoReparsePath $ServerRoot
if (-not $server.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)) { throw 'ServerRoot must be inside TEMP.' }
$work = Assert-NoReparsePath $WorkDirectory $true
if (-not $work.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)) { throw 'WorkDirectory must be a new, unused path inside TEMP.' }
if ($work.StartsWith($server.TrimEnd('\','/') + '\',[StringComparison]::OrdinalIgnoreCase) -or $server.StartsWith($work.TrimEnd('\','/') + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'WorkDirectory and ServerRoot must be separate trees.' }

$manifestCandidates = @(
    (Join-Path $server 'steamapps\appmanifest_1110390.acf'),
    (Join-Path (Split-Path -Parent $server) 'steamapps\appmanifest_1110390.acf'),
    (Join-Path (Split-Path -Parent (Split-Path -Parent $server)) 'appmanifest_1110390.acf')
)
$manifestPath = $manifestCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $manifestPath) { throw 'TEMP U3DS steamapps appmanifest is required.' }
$manifestPath = Assert-NoReparsePath $manifestPath
if (-not $manifestPath.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)) { throw 'Steam appmanifest must be inside TEMP.' }
$manifestText = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
$appId = Get-AppManifestValue $manifestText 'appid'
$stateFlags = Get-AppManifestValue $manifestText 'StateFlags'
$updateResult = Get-AppManifestValue $manifestText 'UpdateResult'
$buildId = Get-AppManifestValue $manifestText 'buildid'
if ($appId -ne $appIdExpected -or $stateFlags -ne '4' -or $updateResult -ne '0' -or $buildId -ne $buildIdExpected) { throw 'TEMP U3DS install state or build ID mismatch.' }
if ($manifestText -match '(?im)^\s*"TargetBuildID"\s+"([^"]+)"' -and $Matches[1] -ne $buildIdExpected) { throw 'Steam TargetBuildID differs from required build.' }

$managed = Join-Path $server 'Unturned_Data\Managed'
$rocket = Join-Path $server 'Modules\Rocket.Unturned'
$resolved = @{}
foreach ($relative in $files) {
    $path = Assert-NoReparsePath (Join-Path $server ($relative.Replace('/','\')))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing build reference: $relative" }
    $resolved[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

New-Item -ItemType Directory -Path $work | Out-Null
$sourcePath = Assert-NoReparsePath $fixtureSources[0]
$observationSource = Assert-NoReparsePath $fixtureSources[1]
$auditSource = Assert-NoReparsePath $fixtureSources[2]
$worldReaderSource = Assert-NoReparsePath $fixtureSources[3]
$safeSource = [Security.SecurityElement]::Escape($sourcePath)
$safeObservationSource = [Security.SecurityElement]::Escape($observationSource)
$safeAuditSource = [Security.SecurityElement]::Escape($auditSource)
$safeWorldReaderSource = [Security.SecurityElement]::Escape($worldReaderSource)
$safeManaged = [Security.SecurityElement]::Escape((Assert-NoReparsePath $managed))
$safeRocket = [Security.SecurityElement]::Escape((Assert-NoReparsePath $rocket))
$project = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>MreDecaySaveHookFixture</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup>
<Compile Include="$safeSource"/>
<Compile Include="$safeObservationSource"/>
<Compile Include="$safeAuditSource"/>
<Compile Include="$safeWorldReaderSource"/>
<Reference Include="Assembly-CSharp"><HintPath>$safeManaged/Assembly-CSharp.dll</HintPath></Reference>
<Reference Include="UnityEngine"><HintPath>$safeManaged/UnityEngine.dll</HintPath></Reference>
<Reference Include="UnityEngine.CoreModule"><HintPath>$safeManaged/UnityEngine.CoreModule.dll</HintPath></Reference>
<Reference Include="SDG.NetTransport"><HintPath>$safeManaged/SDG.NetTransport.dll</HintPath></Reference>
<Reference Include="com.rlabrecque.steamworks.net"><HintPath>$safeManaged/com.rlabrecque.steamworks.net.dll</HintPath></Reference>
<Reference Include="Rocket.API"><HintPath>$safeRocket/Rocket.API.dll</HintPath></Reference>
<Reference Include="Rocket.Core"><HintPath>$safeRocket/Rocket.Core.dll</HintPath></Reference>
</ItemGroup></Project>
"@
$projectPath = Join-Path $work 'MreDecaySaveHookFixture.csproj'
[IO.File]::WriteAllText($projectPath,$project,(New-Object Text.UTF8Encoding($false)))
dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Compile-only fixture build failed.' }

$package = Join-Path $work 'package'
$pluginDirectory = Join-Path $package 'Rocket\Plugins\MreDecaySaveHookFixture'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
$dll = Join-Path $work 'bin\Release\netstandard2.1\MreDecaySaveHookFixture.dll'
Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDirectory 'MreDecaySaveHookFixture.dll')
$dllHash = (Get-FileHash -LiteralPath (Join-Path $pluginDirectory 'MreDecaySaveHookFixture.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestLines = @('status=TEST_ONLY_NOT_INSTALLED',('appid=' + $appId),('buildid=' + $buildId),('appmanifest_sha256=' + (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()),('dll_sha256=' + $dllHash))
foreach ($relative in $files) { $manifestLines += 'sha256_' + $relative.Replace('/','_') + '=' + $resolved[$relative] }
$manifestLines += 'save_manager_onPreSave=SDG.Unturned.SaveHandler.Invoke():void'
$manifestLines += 'save_manager_onPostSave=SDG.Unturned.SaveHandler.Invoke():void'
$manifestLines += 'save_manager_save=void'
$manifestLines += 'commander_execute=Boolean(Steamworks.CSteamID,System.String)'
$manifestLines += 'server_savedata_directory=/Servers'
$manifestLines += 'source_sha256_SaveHookFixture.cs=' + (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestLines += 'source_sha256_ObjectObservation.cs=' + (Get-FileHash -LiteralPath $observationSource -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestLines += 'source_sha256_ObservationAudit.cs=' + (Get-FileHash -LiteralPath $auditSource -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestLines += 'source_sha256_ReadOnlyWorld.cs=' + (Get-FileHash -LiteralPath $worldReaderSource -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllLines((Join-Path $package 'fixture-manifest.txt'),$manifestLines,(New-Object Text.UTF8Encoding($false)))
Write-Output ('TEST_ONLY package: ' + $package)
Write-Output ('DLL SHA256: ' + $dllHash)
