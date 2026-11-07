[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$buildRoot = Join-Path $root 'runtime\ui-builds\MreHud\Windows'
$bundle = Join-Path $buildRoot 'MreHud\MreHud.unity3d'
$dat = Join-Path $buildRoot 'MreHud\MreHud.dat'
$fxRoot = Join-Path $root 'runtime\ui-builds\MreSpawnFx\Windows\MreSpawnFx'
$fxBundle = Join-Path $fxRoot 'MreSpawnFx.unity3d'
$fxDat = Join-Path $fxRoot 'MreSpawnFx.dat'
$manifestPath = Join-Path $buildRoot 'bundle-build.json'
foreach ($path in @($bundle, $dat, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Сначала соберите HUD: отсутствует $path" }
}
foreach ($path in @($fxBundle, $fxDat)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Сначала соберите эффект защиты: $path" } }
$fxText = Get-Content -LiteralPath $fxDat -Raw -Encoding UTF8
foreach ($line in @('GUID 7744a548037a48a6a2ec469b54e7c2e1','Type Effect','ID 29065','Asset_Bundle_Version 6','Exclude_From_Master_Bundle','Lifetime 0.5','Lifetime_Spread 0')) {
    if ($fxText -notmatch ('(?m)^' + [regex]::Escape($line) + '\r?$')) { throw "Неверный ресурс эффекта: $line" }
}
$fxHash = (Get-FileHash -LiteralPath $fxBundle -Algorithm SHA256).Hash.ToLowerInvariant()
$fxDatHash = (Get-FileHash -LiteralPath $fxDat -Algorithm SHA256).Hash.ToLowerInvariant()
$stream = [IO.File]::OpenRead($fxBundle)
try { $sig = New-Object byte[] 8; if ($stream.Read($sig, 0, 8) -ne 8 -or [Text.Encoding]::ASCII.GetString($sig) -ne "UnityFS`0") { throw 'Эффект не является UnityFS bundle.' } } finally { $stream.Dispose() }
$build = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant()
if ($build.bundle_sha256 -ne $hash -or $build.effect_id -ne 48621 -or
    $build.effect_guid -ne '4f11731bb53e4a55b588b97c2c2f61b5' -or
    $build.unity_version -ne '2022.3.62f3' -or $build.target -ne 'StandaloneWindows64' -or
    $build.prefab_address_verified -ne $true) {
    throw 'Манифест собранного HUD не совпадает с ожидаемым содержимым.'
}
$datHash = (Get-FileHash -LiteralPath $dat -Algorithm SHA256).Hash.ToLowerInvariant()
$datText = Get-Content -LiteralPath $dat -Raw -Encoding UTF8
if ((Get-FileHash -LiteralPath $dat -Algorithm SHA256).Hash.ToLowerInvariant() -ne $datHash) {
    throw 'Описание ресурса изменилось во время проверки.'
}
foreach ($line in @('GUID 4f11731bb53e4a55b588b97c2c2f61b5', 'Type Effect', 'ID 48621', 'Asset_Bundle_Version 6', 'Exclude_From_Master_Bundle')) {
    if ($datText -notmatch ('(?m)^' + [regex]::Escape($line) + '\r?$')) { throw "В описании ресурса отсутствует $line" }
}
if ($datText -match '(?m)^\s*Master_Bundle_Override(?:\s|$)') { throw 'Master_Bundle_Override конфликтует с отдельным bundle HUD.' }
$package = Join-Path (Join-Path $root 'runtime\workshop-packages') ('MreHud-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $package) { throw 'Папка результата уже существует.' }
$content = Join-Path $package 'Content'
$target = Join-Path $content 'Bundles\MreHud'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath $bundle -Destination (Join-Path $target 'MreHud.unity3d')
Copy-Item -LiteralPath $dat -Destination (Join-Path $target 'MreHud.dat')
$fxTarget = Join-Path $content 'Bundles\MreSpawnFx'
New-Item -ItemType Directory -Path $fxTarget -Force | Out-Null
Copy-Item -LiteralPath $fxBundle -Destination (Join-Path $fxTarget 'MreSpawnFx.unity3d')
Copy-Item -LiteralPath $fxDat -Destination (Join-Path $fxTarget 'MreSpawnFx.dat')
if ((Get-FileHash -LiteralPath (Join-Path $fxTarget 'MreSpawnFx.unity3d') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fxHash -or
    (Get-FileHash -LiteralPath (Join-Path $fxTarget 'MreSpawnFx.dat') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fxDatHash) { throw 'Эффект изменился при копировании.' }
if ((Get-FileHash -LiteralPath (Join-Path $target 'MreHud.unity3d') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash -or
    (Get-FileHash -LiteralPath (Join-Path $target 'MreHud.dat') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $datHash) {
    throw 'Скопированный пакет не совпадает с проверенными файлами сборки.'
}
# Unturned's WorkshopTool.detectUGCMetaType проверяет файл Object.meta в корне работы.
[IO.File]::WriteAllBytes((Join-Path $content 'Object.meta'), [byte[]]@(0))
$fontRoot = Join-Path $root 'ui\MreHud\Assets\MreFonts'
$fontLicense = Join-Path $fontRoot 'PTSans-OFL.txt'
$fontNotice = Join-Path $fontRoot 'PTSans-README.md'
foreach ($path in @($fontLicense, $fontNotice)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Отсутствует лицензия шрифта: $path" }
}
$licenseTarget = Join-Path $content 'Licenses\PTSans'
New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
Copy-Item -LiteralPath $fontLicense -Destination (Join-Path $licenseTarget 'OFL.txt')
Copy-Item -LiteralPath $fontNotice -Destination (Join-Path $licenseTarget 'README.md')
$receipt = [ordered]@{
    workshop_file_id = 3832074683
    content_path = $content
    bundle_sha256 = $hash
    spawn_fx_sha256 = $fxHash
    spawn_fx_dat_sha256 = $fxDatHash
    dat_sha256 = $datHash
    compatibility_key = 'compatibility_version'
    compatibility_value = '6'
    tags = @('Object', 'Effect')
    published_by_script = $false
    in_game_verified = $false
}
[IO.File]::WriteAllText((Join-Path $package 'package.json'), ($receipt | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{ Package = $package; Content = $content; SHA256 = $hash; Published = $false }
