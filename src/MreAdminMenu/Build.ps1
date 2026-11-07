[CmdletBinding()]
param(
    [string]$GameDirectory,
    [string]$ToolsDll
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $GameDirectory) { $GameDirectory = Join-Path $projectRoot 'runtime\server' }
$GameDirectory = [IO.Path]::GetFullPath($GameDirectory)
$managedRoot = Join-Path $GameDirectory 'Unturned_Data\Managed'
$rocketRoot = Join-Path $GameDirectory 'Extras\Rocket.Unturned'
if ([string]::IsNullOrWhiteSpace($ToolsDll)) {
    $stagedToolsDll = Join-Path $projectRoot 'runtime\builds\MreAdminTools\MreAdminTools.dll'
    if (Test-Path -LiteralPath $stagedToolsDll -PathType Leaf) {
        $toolsManifestPath = Join-Path (Split-Path -Parent $stagedToolsDll) 'build-manifest.json'
        if (-not (Test-Path -LiteralPath $toolsManifestPath -PathType Leaf)) { throw "Нет manifest для staged MreAdminTools: $toolsManifestPath" }
        try { $toolsManifest = Get-Content -LiteralPath $toolsManifestPath -Raw | ConvertFrom-Json }
        catch { throw "Некорректный manifest MreAdminTools: $toolsManifestPath" }
        $stagedHash = (Get-FileHash -LiteralPath $stagedToolsDll -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($toolsManifest.plugin -ne 'MreAdminTools' -or $toolsManifest.sha256 -ne $stagedHash) {
            throw 'SHA256 staged MreAdminTools.dll не совпадает с build-manifest.json.'
        }
        $ToolsDll = $stagedToolsDll
    } else {
        $ToolsDll = Join-Path ([IO.Path]::GetTempPath()) 'MreAdminTools-build\MreAdminTools.dll'
    }
}
$toolsDll = [IO.Path]::GetFullPath($ToolsDll)
$interfaceDll = Join-Path $projectRoot 'runtime\builds\MreInterface\MreInterface.dll'
if (-not (Test-Path -LiteralPath $interfaceDll -PathType Leaf)) { $interfaceDll = Join-Path $GameDirectory 'Servers\MRE\Rocket\Plugins\MreInterface.dll' }
$flightDll = Join-Path $projectRoot 'runtime\builds\MreFlight\MreFlight.dll'
if (-not (Test-Path -LiteralPath $flightDll -PathType Leaf)) { $flightDll = Join-Path $GameDirectory 'Servers\MRE\Rocket\Plugins\MreFlight.dll' }
$moderationDll = Join-Path $projectRoot 'runtime\builds\MreModeration\MreModeration.dll'
if (-not (Test-Path -LiteralPath $moderationDll -PathType Leaf)) { $moderationDll = Join-Path $GameDirectory 'Servers\MRE\Rocket\Plugins\MreModeration.dll' }
$inventoryDll = Join-Path $projectRoot 'runtime\builds\MreInventoryAudit\MreInventoryAudit.dll'
if (-not (Test-Path -LiteralPath $inventoryDll -PathType Leaf)) { $inventoryDll = Join-Path $GameDirectory 'Servers\MRE\Rocket\Plugins\MreInventoryAudit.dll' }
$airdropDll = Join-Path $projectRoot 'runtime\builds\MreAirdrop\MreAirdrop.dll'
if (-not (Test-Path -LiteralPath $airdropDll -PathType Leaf)) { $airdropDll = Join-Path $GameDirectory 'Servers\MRE\Rocket\Plugins\MreAirdrop.dll' }
if (-not (Test-Path -LiteralPath $toolsDll -PathType Leaf)) {
    throw "Зависимость MreAdminTools ещё не собрана: $toolsDll. Сначала выполните powershell -NoProfile -ExecutionPolicy Bypass -File src/MreAdminTools/Build.ps1."
}
if (-not (Test-Path -LiteralPath $interfaceDll -PathType Leaf)) {
    throw "Не найдена MreInterface.dll: $interfaceDll. Установленная локальная сборка MreInterface нужна только как reference; UI DLL не устанавливается."
}
$typedDependencies = @(
    [pscustomobject]@{ Plugin='MreModeration'; Path=$moderationDll },
    [pscustomobject]@{ Plugin='MreInventoryAudit'; Path=$inventoryDll },
    [pscustomobject]@{ Plugin='MreAirdrop'; Path=$airdropDll }
)
foreach ($dependency in $typedDependencies) {
    if (-not (Test-Path -LiteralPath $dependency.Path -PathType Leaf)) { throw "Не найдена typed API зависимость $($dependency.Plugin): $($dependency.Path). Сначала выполните её compile-only сборку." }
    if ($dependency.Path.StartsWith((Join-Path $projectRoot 'runtime\builds\'), [StringComparison]::OrdinalIgnoreCase)) {
        $manifestPath = Join-Path (Split-Path -Parent $dependency.Path) 'build-manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Нет build-manifest для staged $($dependency.Plugin): $manifestPath" }
        try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } catch { throw "Некорректный build-manifest $($dependency.Plugin): $manifestPath" }
        $hash = (Get-FileHash -LiteralPath $dependency.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($manifest.plugin -ne $dependency.Plugin -or $manifest.sha256 -ne $hash) { throw "SHA256 $($dependency.Plugin).dll не совпадает с build-manifest.json." }
    }
}
$outputRoot = Join-Path $projectRoot 'runtime\builds\MreAdminMenu'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$sdkRows = @(dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or $sdkRows.Count -eq 0) { throw 'Для сборки нужен установленный .NET SDK.' }
$sdkRows = @($sdkRows | Where-Object { $_ -match '^(\d+\.\d+\.\d+) \[(.+)\]' })
$latestSdk = $sdkRows[-1]
if ($latestSdk -notmatch '^(\d+\.\d+\.\d+) \[(.+)\]') { throw 'Не удалось определить каталог .NET SDK.' }
$compilerPath = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
$references = @(
    (Join-Path $managedRoot 'mscorlib.dll'),
    (Join-Path $managedRoot 'System.dll'),
    (Join-Path $managedRoot 'System.Core.dll'),
    (Join-Path $managedRoot 'netstandard.dll'),
    (Join-Path $managedRoot 'System.Runtime.dll'),
    (Join-Path $managedRoot 'Assembly-CSharp.dll'),
    (Join-Path $managedRoot 'SDG.NetTransport.dll'),
    (Join-Path $managedRoot 'UnityEngine.dll'),
    (Join-Path $managedRoot 'UnityEngine.CoreModule.dll'),
    (Join-Path $managedRoot 'com.rlabrecque.steamworks.net.dll'),
    (Join-Path $rocketRoot 'Rocket.API.dll'),
    (Join-Path $rocketRoot 'Rocket.Core.dll'),
    (Join-Path $rocketRoot 'Rocket.Unturned.dll'),
    $interfaceDll,
    $flightDll,
    $toolsDll,
    $moderationDll,
    $inventoryDll,
    $airdropDll
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Не найден reference: $reference. Проверьте локальный U3DS SDK и сборки зависимостей." }
}
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.cs' | Sort-Object FullName)
if ($sources.Count -eq 0) { throw "В $PSScriptRoot нет исходников C# для сборки." }
$outputPath = Join-Path $outputRoot 'MreAdminMenu.dll'
$compilerArguments = @('/noconfig', '/nostdlib+', '/target:library', '/langversion:7.3', '/optimize+', '/deterministic+', '/utf8output', "/out:$outputPath")
$compilerArguments += @($references | ForEach-Object { "/reference:$_" })
$compilerArguments += @($sources | ForEach-Object { $_.FullName })
& dotnet $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции MreAdminMenu: $LASTEXITCODE." }
$manifest = [ordered]@{
    plugin = 'MreAdminMenu'
    built_at_utc = [DateTime]::UtcNow.ToString('o')
    sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    references = @($references | ForEach-Object { [ordered]@{ name = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } })
    sources = @($sources | ForEach-Object { [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'build-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Write-Host "MreAdminMenu собран: $outputPath"
Write-Host 'Скрипт не устанавливает DLL на сервер. Эффект собирается отдельно в Unity MRE/Admin.'
