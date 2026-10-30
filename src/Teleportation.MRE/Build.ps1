[CmdletBinding()]
param(
    [string]$ReferenceRoot,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = Join-Path $projectRoot 'runtime\server'
}
$ReferenceRoot = [IO.Path]::GetFullPath($ReferenceRoot)
$managedRoot = Join-Path $ReferenceRoot 'Unturned_Data\Managed'
$rocketRoot = Join-Path $ReferenceRoot 'Extras\Rocket.Unturned'
if (-not (Test-Path -LiteralPath $managedRoot) -or -not (Test-Path -LiteralPath $rocketRoot)) {
    throw 'Нужны Managed и Extras/Rocket.Unturned из установленного U3DS или Unturned. Укажите -ReferenceRoot.'
}

$sdkLines = @(& dotnet --list-sdks)
$sdkLine = $sdkLines | Where-Object { $_ -match '^\d' } | Select-Object -Last 1
if (-not $sdkLine -or $sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'Не найден .NET SDK с компилятором Roslyn.' }
$cscPath = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
if (-not (Test-Path -LiteralPath $cscPath)) { throw 'Не найден компилятор csc.dll.' }

$outputRoot = Join-Path $projectRoot 'runtime\plugin-builds\Teleportation.MRE'
if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
    $runtimePrefix = [IO.Path]::GetFullPath((Join-Path $projectRoot 'runtime')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $outputRoot.StartsWith($runtimePrefix,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Каталог сборки должен находиться внутри runtime проекта.'
    }
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$outputDll = Join-Path $outputRoot 'Teleportation.dll'
$responsePath = Join-Path $outputRoot 'compile.rsp'
$arguments = @('/nologo','/target:library','/optimize+','/deterministic+','/langversion:latest','/nostdlib+',('/out:"' + $outputDll + '"'))
$requiredNames = @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','System.Runtime.dll','Assembly-CSharp.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll')
$referencePaths = @()
foreach ($name in $requiredNames) {
    $referencePaths += Join-Path $managedRoot $name
}
foreach ($name in @('Rocket.API.dll','Rocket.Core.dll','Rocket.Unturned.dll')) {
    $referencePaths += Join-Path $rocketRoot $name
}
foreach ($path in $referencePaths) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Отсутствует сборочная зависимость: $path" }
    $arguments += '/reference:"' + $path + '"'
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Plugin') -Recurse -File -Filter '*.cs' | Sort-Object FullName)
foreach ($source in $sources) { $arguments += '"' + $source.FullName + '"' }
[IO.File]::WriteAllLines($responsePath, $arguments, (New-Object Text.UTF8Encoding($true)))
& dotnet $cscPath ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw "Сборка Teleportation MRE завершилась с кодом $LASTEXITCODE." }
$sourceHashes = @($sources | ForEach-Object { [ordered]@{path=$_.FullName.Substring($PSScriptRoot.Length).TrimStart('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
$manifest = [ordered]@{
    patch_id='mre-recipient-guard-v1'
    assembly_version='1.8.3.2'
    upstream_commit='9c046b50ab7fc0a456263134d7d6f07b2461fbb9'
    built_at=(Get-Date).ToUniversalTime().ToString('o')
    sha256=(Get-FileHash -LiteralPath $outputDll -Algorithm SHA256).Hash.ToLowerInvariant()
    game_reference_sha256=(Get-FileHash -LiteralPath (Join-Path $managedRoot 'Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    sources=$sourceHashes
    game_smoke_test='pending'
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'build.json'), ($manifest | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Write-Host "Собрана Teleportation MRE 1.8.3.2: $outputDll"
