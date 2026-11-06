[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$buildRoot = Join-Path $root 'runtime\ui-builds\MreTrade\Windows'
$bundle = Join-Path $buildRoot 'MreTrade\MreTrade.unity3d'
$dat = Join-Path $buildRoot 'MreTrade\MreTrade.dat'
$buildPath = Join-Path $buildRoot 'bundle-build.json'
$effectPath = Join-Path $root 'ui\MreTrade\effect-manifest.json'
$previewPath = Join-Path $root 'ui\MreTrade\workshop-preview.jpg'
foreach ($path in @($bundle, $dat, $buildPath, $effectPath, $previewPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Отсутствует файл: $path" }
}
$effect = Get-Content -LiteralPath $effectPath -Raw -Encoding UTF8 | ConvertFrom-Json
$build = Get-Content -LiteralPath $buildPath -Raw -Encoding UTF8 | ConvertFrom-Json
$guid = 'a986933995df4d29816efcc37f87b220'
$hash = (Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant()
$datHash = (Get-FileHash -LiteralPath $dat -Algorithm SHA256).Hash.ToLowerInvariant()
$previewBytes = [IO.File]::ReadAllBytes($previewPath)
if ($previewBytes.Length -lt 4 -or $previewBytes.Length -gt 1000000 -or
    $previewBytes[0] -ne 0xFF -or $previewBytes[1] -ne 0xD8 -or
    $previewBytes[$previewBytes.Length - 2] -ne 0xFF -or $previewBytes[$previewBytes.Length - 1] -ne 0xD9) {
    throw 'Превью должно быть корректным JPEG размером не более 1 000 000 байт.'
}
$previewHash = (Get-FileHash -LiteralPath $previewPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($effect.effect_id -ne 48622 -or $effect.effect_key -ne 14822 -or $effect.effect_guid -cne $guid -or
    $effect.unity_version -cne '2022.3.62f3' -or $effect.target -cne 'StandaloneWindows64' -or
    $effect.bundle -cne 'MreTrade.unity3d' -or $effect.prefab_address -cne 'Effect' -or
    $build.effect_id -ne $effect.effect_id -or $build.effect_key -ne $effect.effect_key -or
    $build.effect_guid -cne $guid -or $build.unity_version -cne $effect.unity_version -or
    $build.target -cne $effect.target -or $build.bundle_sha256 -cne $hash -or
    $build.prefab_address_verified -ne $true) { throw 'Контракт эффекта или манифест сборки не совпадает с bundle.' }
$header = [byte[]]::new(8)
$stream = [IO.File]::OpenRead($bundle)
try { $count = $stream.Read($header, 0, $header.Length) } finally { $stream.Dispose() }
if ($count -ne 8 -or [Text.Encoding]::ASCII.GetString($header) -cne "UnityFS`0") { throw 'Bundle не содержит заголовок UnityFS.' }
$lines = @(Get-Content -LiteralPath $dat -Encoding UTF8 | Where-Object { $_.Trim() -ne '' })
$expected = @("GUID $guid", 'Type Effect', 'ID 48622', 'Asset_Bundle_Version 6', 'Exclude_From_Master_Bundle')
if ($lines.Count -ne $expected.Count) { throw 'Описание dat содержит лишние или отсутствующие строки.' }
for ($i = 0; $i -lt $expected.Count; $i++) {
    if ($lines[$i] -cne $expected[$i]) { throw "Неверная строка dat: $($lines[$i])" }
}
$packageRoot = Join-Path $root 'runtime\workshop-packages'
[IO.Directory]::CreateDirectory($packageRoot) | Out-Null
$package = Join-Path $packageRoot ('MreTrade-' + [guid]::NewGuid().ToString('N'))
# Создание новой директории с уникальным именем без перезаписи существующего пакета.
if (Test-Path -LiteralPath $package) { throw 'Папка результата уже существует.' }
$target = Join-Path $package 'Content\Bundles\MreTrade'
New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -LiteralPath $bundle -Destination (Join-Path $target 'MreTrade.unity3d')
Copy-Item -LiteralPath $dat -Destination (Join-Path $target 'MreTrade.dat')
Copy-Item -LiteralPath $previewPath -Destination (Join-Path $package 'MreTrade-preview.jpg')
if ((Get-FileHash -LiteralPath (Join-Path $target 'MreTrade.unity3d') -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash -or
    (Get-FileHash -LiteralPath (Join-Path $target 'MreTrade.dat') -Algorithm SHA256).Hash.ToLowerInvariant() -cne $datHash) { throw 'Скопированные файлы не совпадают с проверенной сборкой.' }
[IO.File]::WriteAllBytes((Join-Path $package 'Content\Object.meta'), [byte[]]@(0))
$receipt = [ordered]@{
    effect_id = 48622
    effect_guid = $guid
    unity_version = $effect.unity_version
    target = $effect.target
    bundle_sha256 = $hash
    dat_sha256 = $datHash
    preview_sha256 = $previewHash
    preview_dimensions = '512x512'
    workshop_title = 'MRE Trade'
    workshop_visibility = 'Public'
    workshop_asset_type = 'Object/Effect'
    workshop_section = 'Ready-to-Use'
    compatibility_version = 6
    tags = @('Object', 'Effect')
    published = $false
    in_game_verified = $false
}
[IO.File]::WriteAllText((Join-Path $package 'package.json'), ($receipt | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
[pscustomobject]@{ Package = $package; SHA256 = $hash; Published = $false }
