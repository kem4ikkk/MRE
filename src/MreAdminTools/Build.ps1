[CmdletBinding()]
param(
    [string]$ReferenceRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) { $ReferenceRoot = Join-Path $projectRoot 'runtime\server' }
$ReferenceRoot = [IO.Path]::GetFullPath($ReferenceRoot)
$managedRoot = Join-Path $ReferenceRoot 'Unturned_Data\Managed'
$rocketRoot = Join-Path $ReferenceRoot 'Extras\Rocket.Unturned'
if (-not (Test-Path -LiteralPath $managedRoot) -or -not (Test-Path -LiteralPath $rocketRoot)) {
    throw 'Нужны Managed и Extras/Rocket.Unturned из установленного сервера. Укажите -ReferenceRoot.'
}

$sdkLines = @(& dotnet --list-sdks)
$sdkLine = $sdkLines | Where-Object { $_ -match '^\d' } | Select-Object -Last 1
if (-not $sdkLine -or $sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'Не найден .NET SDK с компилятором Roslyn.' }
$cscPath = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
if (-not (Test-Path -LiteralPath $cscPath)) { throw 'Не найден компилятор csc.dll.' }

$outputRoot = Join-Path $projectRoot 'runtime\builds\MreAdminTools'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$outputDll = Join-Path $outputRoot 'MreAdminTools.dll'
$responsePath = Join-Path $outputRoot 'compile.rsp'
$arguments = @('/nologo','/target:library','/optimize+','/deterministic+','/langversion:latest','/nostdlib+',('/out:"' + $outputDll + '"'))
$requiredNames = @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','System.Runtime.dll','Assembly-CSharp.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.dll','UnityEngine.CoreModule.dll')
foreach ($name in $requiredNames) {
    $path = Join-Path $managedRoot $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Отсутствует сборочная зависимость: $path" }
    $arguments += '/reference:"' + $path + '"'
}
foreach ($name in @('Rocket.API.dll','Rocket.Core.dll','Rocket.Unturned.dll')) {
    $path = Join-Path $rocketRoot $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Отсутствует сборочная зависимость: $path" }
    $arguments += '/reference:"' + $path + '"'
}
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.cs' | Sort-Object FullName)
foreach ($source in $sources) { $arguments += '"' + $source.FullName + '"' }
[IO.File]::WriteAllLines($responsePath, $arguments, (New-Object Text.UTF8Encoding($true)))
& dotnet $cscPath ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw "Сборка MreAdminTools завершилась с кодом $LASTEXITCODE." }
$referencePaths = @($requiredNames | ForEach-Object { Join-Path $managedRoot $_ })
$referencePaths += @('Rocket.API.dll','Rocket.Core.dll','Rocket.Unturned.dll' | ForEach-Object { Join-Path $rocketRoot $_ })
$sourceManifest = @($sources | ForEach-Object {
    [ordered]@{ name = [IO.Path]::GetFileName($_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{
    plugin = 'MreAdminTools'
    built_at_utc = [DateTime]::UtcNow.ToString('o')
    sha256 = (Get-FileHash -LiteralPath $outputDll -Algorithm SHA256).Hash.ToLowerInvariant()
    references = @($referencePaths | ForEach-Object { [ordered]@{ name = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } })
    sources = $sourceManifest
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'build-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Write-Host "Собрана MreAdminTools: $outputDll"
