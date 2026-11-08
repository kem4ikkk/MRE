[CmdletBinding()]
param([string]$HudPackage)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $root 'runtime'
$manifestPath = Join-Path $root 'ui\MreHelp\Build\Windows\effect-manifest.json'
$helpDir = Join-Path $root 'ui\MreHelp\Build\Windows\MreHelp'
foreach ($path in @($manifestPath,(Join-Path $helpDir 'MreHelp.dat'),(Join-Path $helpDir 'MreHelp.unity3d'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Сначала соберите UI помощи: отсутствует $path" }
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$bundlePath = Join-Path $helpDir 'MreHelp.unity3d'
$bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.effect_id -ne 48625 -or $manifest.effect_guid -ne '6e4a31b49c874f428a622e1a68e4d203' -or
    $manifest.bundle_sha256 -ne $bundleHash -or $manifest.in_game_verified -ne $false) {
    throw 'UI помощи опубликован, изменён или отличается от ожидаемого asset contract.'
}

if (-not $HudPackage) {
    $candidate = Get-ChildItem -LiteralPath (Join-Path $runtime 'workshop-packages') -Directory -Filter 'MreHud-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'MreHud-Help-*' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $candidate) { throw 'Не найден пакет MRE HUD. Укажите -HudPackage с путём к Content базового пакета.' }
    $HudPackage = Join-Path $candidate.FullName 'Content'
}
$baseContent = [IO.Path]::GetFullPath($HudPackage)
$baseRoot = Split-Path -Parent $baseContent
$receiptPath = Join-Path $baseRoot 'package.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $baseContent 'Object.meta') -PathType Leaf)) {
    throw 'Базовый пакет должен быть Content папкой проверенной работы MRE HUD с package.json/Object.meta.'
}
$receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($receipt.workshop_file_id -ne 3832074683 -or $receipt.published_by_script -ne $false) { throw 'Пакет не является локальной заготовкой MRE HUD для Workshop 3832074683.' }
$hudBundle = Join-Path $baseContent 'Bundles\MreHud\MreHud.unity3d'
$hudDat = Join-Path $baseContent 'Bundles\MreHud\MreHud.dat'
if (-not (Test-Path -LiteralPath $hudBundle -PathType Leaf) -or -not (Test-Path -LiteralPath $hudDat -PathType Leaf)) { throw 'В базовой заготовке отсутствуют файлы MRE HUD.' }
if ($receipt.bundle_sha256 -ne (Get-FileHash -LiteralPath $hudBundle -Algorithm SHA256).Hash.ToLowerInvariant() -or
    $receipt.dat_sha256 -ne (Get-FileHash -LiteralPath $hudDat -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Хэши MRE HUD не совпадают с манифестом базовой заготовки.' }
$spawnBundle = Join-Path $baseContent 'Bundles\MreSpawnFx\MreSpawnFx.unity3d'
$spawnDat = Join-Path $baseContent 'Bundles\MreSpawnFx\MreSpawnFx.dat'
foreach ($path in @($spawnBundle, $spawnDat)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "В базовой заготовке отсутствует эффект появления: $path" }
}
$spawnHash = (Get-FileHash -LiteralPath $spawnBundle -Algorithm SHA256).Hash.ToLowerInvariant()
$spawnDatHash = (Get-FileHash -LiteralPath $spawnDat -Algorithm SHA256).Hash.ToLowerInvariant()
if ($receipt.spawn_fx_sha256 -ne $spawnHash -or $receipt.spawn_fx_dat_sha256 -ne $spawnDatHash) {
    throw 'Хэши эффекта появления не совпадают с базовой заготовкой.'
}

$outputRoot = Join-Path $runtime 'workshop-packages'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$package = Join-Path $outputRoot ('MreHud-Help-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $package) { throw 'Папка результата уже существует.' }
New-Item -ItemType Directory -Path $package | Out-Null
$content = Join-Path $package 'Content'
Copy-Item -LiteralPath $baseContent -Destination $content -Recurse
$destination = Join-Path $content 'Bundles\MreHelp'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $helpDir 'MreHelp.dat') -Destination (Join-Path $destination 'MreHelp.dat')
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $destination 'MreHelp.unity3d')
$fontLicense = Join-Path $root 'ui\MreHelp\Assets\MreFonts\PTSans-OFL.txt'
$fontNotice = Join-Path $root 'ui\MreHelp\Assets\MreFonts\PTSans-README.md'
$licenseTarget = Join-Path $content 'Licenses\PTSans'
New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
Copy-Item -LiteralPath $fontLicense -Destination (Join-Path $licenseTarget 'OFL.txt') -Force
Copy-Item -LiteralPath $fontNotice -Destination (Join-Path $licenseTarget 'README.md') -Force
$notoLicenseTarget = Join-Path $content 'Licenses\NotoSans'
New-Item -ItemType Directory -Path $notoLicenseTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'ui\MreHelp\Assets\MreFonts\NotoSans-OFL.txt') -Destination (Join-Path $notoLicenseTarget 'OFL.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'ui\MreHelp\Assets\MreFonts\NotoSans-README.md') -Destination (Join-Path $notoLicenseTarget 'README.md') -Force
$artNotice = Join-Path $root 'ui\MreHelp\Assets\MreMenuArt\README.md'
if (Test-Path -LiteralPath $artNotice -PathType Leaf) {
    $artLicenseTarget = Join-Path $content 'Licenses\MreMenuArt'
    New-Item -ItemType Directory -Path $artLicenseTarget -Force | Out-Null
    Copy-Item -LiteralPath $artNotice -Destination (Join-Path $artLicenseTarget 'README.md') -Force
    $shellNotice = Join-Path $root 'ui\MreHelp\Assets\MreMenuArt\shell\README.md'
    if (Test-Path -LiteralPath $shellNotice -PathType Leaf) {
        Copy-Item -LiteralPath $shellNotice -Destination (Join-Path $artLicenseTarget 'shell-README.md') -Force
    }
}

$newReceipt = [ordered]@{
    workshop_file_id = 3832074683
    content_path = $content
    base_content_path = $baseContent
    hud_bundle_sha256 = (Get-FileHash -LiteralPath $hudBundle -Algorithm SHA256).Hash.ToLowerInvariant()
    help_bundle_sha256 = $bundleHash
    spawn_fx_bundle_sha256 = $spawnHash
    spawn_fx_dat_sha256 = $spawnDatHash
    help_effect_id = 48625
    help_effect_guid = $manifest.effect_guid
    compatibility_key = 'compatibility_version'
    compatibility_value = '6'
    tags = @('Object','Effect')
    published_by_script = $false
    in_game_verified = $false
}
[IO.File]::WriteAllText((Join-Path $package 'package.json'), ($newReceipt | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
if ((Get-FileHash -LiteralPath (Join-Path $destination 'MreHelp.unity3d') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $bundleHash) { throw 'Хэш скопированного MreHelp bundle не совпал.' }
Write-Host "Локальная заготовка пакета MRE HUD + /menu готова: $content"
Write-Host 'Пакет локально собран. Публикация в Workshop выполняется отдельно.'
