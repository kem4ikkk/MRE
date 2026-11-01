[CmdletBinding()]
param(
    [string]$ReferenceRoot,
    [string]$TeleportationAssembly,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) { $ReferenceRoot = Join-Path $projectRoot 'runtime\server' }
$ReferenceRoot = [IO.Path]::GetFullPath($ReferenceRoot)
$managedRoot = Join-Path $ReferenceRoot 'Unturned_Data\Managed'
$rocketRoot = Join-Path $ReferenceRoot 'Extras\Rocket.Unturned'
if ([string]::IsNullOrWhiteSpace($TeleportationAssembly)) {
    $TeleportationAssembly = Join-Path $projectRoot 'runtime\plugin-builds\Teleportation.MRE\Teleportation.dll'
}
$harmonyAssembly = Join-Path $projectRoot 'runtime\plugin-builds\MoreHomes\release\Libraries\0Harmony.dll'
$outputRoot = Join-Path $projectRoot 'runtime\plugin-builds\MoreHomes.MRE'
if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) { $outputRoot = [IO.Path]::GetFullPath($OutputDirectory) }
$runtimePrefix = [IO.Path]::GetFullPath((Join-Path $projectRoot 'runtime')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if (-not ([IO.Path]::GetFullPath($outputRoot)).StartsWith($runtimePrefix,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Каталог сборки должен находиться внутри runtime проекта.'
}

$sdkLines = @(& dotnet --list-sdks)
$sdkLine = $sdkLines | Where-Object { $_ -match '^\d' } | Select-Object -Last 1
if (-not $sdkLine -or $sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'Не найден .NET SDK с компилятором Roslyn.' }
$cscPath = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
$references = @()
foreach ($name in @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','System.Runtime.dll','System.Xml.dll',
    'Assembly-CSharp.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.dll','UnityEngine.CoreModule.dll',
    'UnityEngine.PhysicsModule.dll','Newtonsoft.Json.dll')) {
    $references += Join-Path $managedRoot $name
}
foreach ($name in @('Rocket.API.dll','Rocket.Core.dll','Rocket.Unturned.dll')) { $references += Join-Path $rocketRoot $name }
$references += [IO.Path]::GetFullPath($TeleportationAssembly)
$references += $harmonyAssembly
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Отсутствует сборочная зависимость: $reference" }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$outputDll = Join-Path $outputRoot 'MoreHomes.dll'
$responsePath = Join-Path $outputRoot 'compile.rsp'
$arguments = @('/nologo','/target:library','/optimize+','/deterministic+','/langversion:latest','/nostdlib+',('/out:"' + $outputDll + '"'))
foreach ($reference in $references) { $arguments += '/reference:"' + $reference + '"' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Plugin') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName)
foreach ($source in $sources) { $arguments += '"' + $source.FullName + '"' }
[IO.File]::WriteAllLines($responsePath,$arguments,(New-Object Text.UTF8Encoding($true)))
& dotnet $cscPath ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw "Сборка MoreHomes MRE завершилась с кодом $LASTEXITCODE." }
$sourceHashes = @($sources | ForEach-Object { [ordered]@{
    path=$_.FullName.Substring($PSScriptRoot.Length).TrimStart('\','/')
    sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
} })
$manifest = [ordered]@{
    patch_id='mre-home-ui-v1'
    assembly_version='1.10.3.1'
    upstream_commit='d57770a30fc9705b1360eb3adc966c4722eaf40c'
    built_at=(Get-Date).ToUniversalTime().ToString('o')
    sha256=(Get-FileHash -LiteralPath $outputDll -Algorithm SHA256).Hash.ToLowerInvariant()
    game_reference_sha256=(Get-FileHash -LiteralPath (Join-Path $managedRoot 'Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    teleportation_reference_sha256=(Get-FileHash -LiteralPath $TeleportationAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    sources=$sourceHashes
    game_smoke_test='pending'
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'build.json'),($manifest | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
Write-Host "Собрана MoreHomes MRE 1.10.3.1: $outputDll"
