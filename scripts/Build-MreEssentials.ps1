[CmdletBinding()]
param(
    [string]$GameDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $GameDirectory) { $GameDirectory = Join-Path $projectRoot 'runtime\server' }
$GameDirectory = [IO.Path]::GetFullPath($GameDirectory)
$managedRoot = Join-Path $GameDirectory 'Unturned_Data\Managed'
$rocketRoot = Join-Path $GameDirectory 'Extras\Rocket.Unturned'
$outputRoot = Join-Path $projectRoot 'runtime\builds\MreEssentials'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$sdkRows = @(dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or $sdkRows.Count -eq 0) { throw 'Для сборки нужен установленный .NET SDK.' }
$sdkRows = @($sdkRows | Where-Object { $_ -match '^(\d+\.\d+\.\d+) \[(.+)\]' })
$latestSdk = $sdkRows[-1]
if ($latestSdk -notmatch '^(\d+\.\d+\.\d+) \[(.+)\]') { throw 'Не удалось определить каталог .NET SDK.' }
$compilerPath = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
$referencePaths = @(
    (Join-Path $managedRoot 'mscorlib.dll'),
    (Join-Path $managedRoot 'System.dll'),
    (Join-Path $managedRoot 'System.Core.dll'),
    (Join-Path $managedRoot 'netstandard.dll'),
    (Join-Path $managedRoot 'System.Runtime.dll'),
    (Join-Path $managedRoot 'Assembly-CSharp.dll'),
    (Join-Path $managedRoot 'UnityEngine.dll'),
    (Join-Path $managedRoot 'UnityEngine.CoreModule.dll'),
    (Join-Path $managedRoot 'com.rlabrecque.steamworks.net.dll'),
    (Join-Path $rocketRoot 'Rocket.API.dll'),
    (Join-Path $rocketRoot 'Rocket.Core.dll'),
    (Join-Path $rocketRoot 'Rocket.Unturned.dll')
)
foreach ($referencePath in $referencePaths) {
    if (-not (Test-Path -LiteralPath $referencePath -PathType Leaf)) {
        throw "Отсутствует reference: $referencePath. Установите U3DS или задайте -GameDirectory для имеющегося клиента."
    }
}
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\MreEssentials') -File -Filter '*.cs' | ForEach-Object { $_.FullName })
$outputPath = Join-Path $outputRoot 'MreEssentials.dll'
$compilerArguments = @('/noconfig', '/nostdlib+', '/target:library', '/langversion:7.3', '/optimize+', '/deterministic+', '/utf8output', "/out:$outputPath")
$compilerArguments += @($referencePaths | ForEach-Object { "/reference:$_" })
$compilerArguments += $sourcePaths
& dotnet $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции MreEssentials: $LASTEXITCODE." }
$referenceManifest = @($referencePaths | ForEach-Object {
    [ordered]@{ name = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{
    plugin = 'MreEssentials'
    built_at_utc = [DateTime]::UtcNow.ToString('o')
    sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    references = $referenceManifest
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'build-manifest.json'), ($manifest | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Write-Host "MreEssentials собран: $outputPath"
Write-Host 'Скрипт не устанавливает DLL на сервер. Загрузка и работа в игре проверяются отдельно.'
