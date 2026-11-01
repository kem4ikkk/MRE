[CmdletBinding()]
param([string]$ReferenceRoot)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReferenceRoot) { $ReferenceRoot = Join-Path $projectRoot 'runtime\server' }
$ReferenceRoot = [IO.Path]::GetFullPath($ReferenceRoot)
$managedRoot = Join-Path $ReferenceRoot 'Unturned_Data\Managed'
$rocketRoot = Join-Path $ReferenceRoot 'Extras\Rocket.Unturned'
$teleportationPath = Join-Path $projectRoot 'runtime\plugin-builds\Teleportation.MRE\Teleportation.dll'
if (-not (Test-Path -LiteralPath $teleportationPath)) { throw 'Сначала соберите актуальный src/Teleportation.MRE/Build.ps1.' }
$essentialsPath = Join-Path $projectRoot 'runtime\builds\MreEssentials\MreEssentials.dll'
if (-not (Test-Path -LiteralPath $essentialsPath)) { throw 'Сначала соберите актуальный scripts/Build-MreEssentials.ps1.' }
$sdkLine = @(& dotnet --list-sdks) | Where-Object { $_ -match '^\d' } | Select-Object -Last 1
if ($sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'Для сборки MreHud нужен .NET SDK.' }
$compilerPath = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
$outputRoot = Join-Path $projectRoot 'runtime\plugin-builds\MreHud'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$outputPath = Join-Path $outputRoot 'MreHud.dll'
$references = @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','System.Runtime.dll','Assembly-CSharp.dll','SDG.NetTransport.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.dll','UnityEngine.CoreModule.dll') | ForEach-Object { Join-Path $managedRoot $_ }
$references += @('Rocket.API.dll','Rocket.Core.dll','Rocket.Unturned.dll') | ForEach-Object { Join-Path $rocketRoot $_ }
$references += $teleportationPath
$references += $essentialsPath
$harmonyPath = Join-Path $ReferenceRoot 'Servers\MRE\Rocket\Libraries\0Harmony.dll'
$references += $harmonyPath
$arguments = @('/nologo','/target:library','/optimize+','/deterministic+','/langversion:7.3','/nostdlib+',('/out:"' + $outputPath + '"'))
foreach ($referencePath in $references) {
    if (-not (Test-Path -LiteralPath $referencePath)) { throw "Отсутствует зависимость MreHud: $referencePath" }
    $arguments += '/reference:"' + $referencePath + '"'
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\MreHud\Plugin') -Filter '*.cs' -File | Sort-Object FullName)
foreach ($source in $sources) { $arguments += '"' + $source.FullName + '"' }
$responsePath = Join-Path $outputRoot 'compile.rsp'
[IO.File]::WriteAllLines($responsePath, $arguments, (New-Object Text.UTF8Encoding($true)))
& dotnet $compilerPath ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw "Ошибка сборки MreHud: $LASTEXITCODE" }
$manifest = [ordered]@{
    plugin = 'MreHud'; version = '1.1.0.0'; built_at_utc = [DateTime]::UtcNow.ToString('o')
    sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    teleportation_sha256 = (Get-FileHash -LiteralPath $teleportationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    essentials_sha256 = (Get-FileHash -LiteralPath $essentialsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    harmony_sha256 = (Get-FileHash -LiteralPath $harmonyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    game_reference_sha256 = (Get-FileHash -LiteralPath (Join-Path $managedRoot 'Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    sources = @($sources | ForEach-Object { [ordered]@{ path=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
    client_bundle = 'not_built'; in_game_verification = 'pending'
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'build.json'), ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Write-Host "MreHud собран: $outputPath"
Write-Host 'DLL не установлена автоматически. Клиентский Unity bundle и вход игрока проверяются отдельно.'
