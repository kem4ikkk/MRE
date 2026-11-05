[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$buildRoot = Join-Path $root 'runtime\ui-builds\MreHud\Windows'
$bundle = Join-Path $buildRoot 'MreHud\MreHud.unity3d'
$dat = Join-Path $buildRoot 'MreHud\MreHud.dat'
$manifestPath = Join-Path $buildRoot 'bundle-build.json'
foreach ($path in @($bundle, $dat, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Сначала соберите HUD: отсутствует $path" }
}
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
if ((Get-FileHash -LiteralPath (Join-Path $target 'MreHud.unity3d') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash -or
    (Get-FileHash -LiteralPath (Join-Path $target 'MreHud.dat') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $datHash) {
    throw 'Скопированный пакет не совпадает с проверенными файлами сборки.'
}
# Unturned's WorkshopTool.detectUGCMetaType проверяет файл Object.meta в корне работы.
[IO.File]::WriteAllBytes((Join-Path $content 'Object.meta'), [byte[]]@(0))
$receipt = [ordered]@{
    workshop_file_id = 3832074683
    content_path = $content
    bundle_sha256 = $hash
    dat_sha256 = $datHash
    compatibility_key = 'compatibility_version'
    compatibility_value = '6'
    tags = @('Object', 'Effect')
    published_by_script = $false
    in_game_verified = $false
}
[IO.File]::WriteAllText((Join-Path $package 'package.json'), ($receipt | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{ Package = $package; Content = $content; SHA256 = $hash; Published = $false }
