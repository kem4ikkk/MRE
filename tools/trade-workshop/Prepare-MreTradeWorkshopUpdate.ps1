[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PackagePath)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$package = [IO.Path]::GetFullPath($PackagePath)
$packagePrefix = [IO.Path]::GetFullPath((Join-Path $root 'runtime\workshop-packages')) + [IO.Path]::DirectorySeparatorChar
if (-not $package.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Путь пакета должен находиться внутри runtime/workshop-packages.'
}
$receiptPath = Join-Path $package 'package.json'
$effectPath = Join-Path $root 'ui\MreTrade\effect-manifest.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'В пакете отсутствует package.json.' }
$effect = Get-Content -LiteralPath $effectPath -Raw -Encoding UTF8 | ConvertFrom-Json
$receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
$itemId = [UInt64]$effect.workshop_file_id
if ($itemId -ne 3834982706 -or $effect.confirmation_protocol -cne 'nonce-input-v1' -or $effect.schema_version -ne 2) {
    throw 'effect-manifest не подтверждает Trade item и nonce-протокол.'
}
if ($receipt.effect_id -ne $effect.effect_id -or $receipt.effect_guid -cne $effect.effect_guid -or
    $receipt.schema_version -ne $effect.schema_version -or $receipt.confirmation_protocol -cne $effect.confirmation_protocol -or
    $receipt.bundle_sha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw 'Receipt пакета не совпадает с актуальным контрактом MRE Trade.'
}
$contentPath = Join-Path $package 'Content'
$bundlePath = Join-Path $contentPath 'Bundles\MreTrade\MreTrade.unity3d'
$datPath = Join-Path $contentPath 'Bundles\MreTrade\MreTrade.dat'
if ((Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $receipt.bundle_sha256 -or
    (Get-FileHash -LiteralPath $datPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $receipt.dat_sha256) {
    throw 'Bundle или dat изменились после создания пакета.'
}
$existing = @()
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $root 'runtime\workshop-packages') -Directory -Filter 'MreTrade-*') {
    if ($directory.FullName -eq $package) { continue }
    $candidatePath = Join-Path $directory.FullName 'package.json'
    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) { continue }
    $candidate = Get-Content -LiteralPath $candidatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($candidate.published -eq $true -and [UInt64]$candidate.workshop_file_id -eq $itemId) {
        $existing += [pscustomobject]@{Root=$directory.FullName; Receipt=$candidate}
    }
}
if ($existing.Count -ne 1) { throw "Ожидался ровно один опубликованный receipt для $itemId; найдено: $($existing.Count)." }
$previousRoot = $existing[0].Root
$previewSource = Join-Path $previousRoot 'MreTrade-preview.jpg'
$idSource = Join-Path $previousRoot 'published-item-id.txt'
if ((Get-Content -LiteralPath $idSource -Raw -Encoding ascii).Trim() -ne $itemId.ToString()) {
    throw 'Предыдущий пакет содержит другой Workshop ID.'
}
$previewHash = (Get-FileHash -LiteralPath $previewSource -Algorithm SHA256).Hash.ToLowerInvariant()
if ($previewHash -cne $existing[0].Receipt.preview_sha256 -or $existing[0].Receipt.preview_dimensions -cne '512x512') {
    throw 'Превью опубликованного item не совпадает с сохранённым receipt.'
}
Copy-Item -LiteralPath $previewSource -Destination (Join-Path $package 'MreTrade-preview.jpg')
[IO.File]::WriteAllText((Join-Path $package 'published-item-id.txt'), $itemId.ToString() + [Environment]::NewLine, [Text.Encoding]::ASCII)
$receipt | Add-Member -MemberType NoteProperty -Name workshop_file_id -Value $itemId -Force
$receipt | Add-Member -MemberType NoteProperty -Name preview_sha256 -Value $previewHash -Force
$receipt | Add-Member -MemberType NoteProperty -Name preview_dimensions -Value '512x512' -Force
$receipt | Add-Member -MemberType NoteProperty -Name workshop_title -Value 'MRE Trade' -Force
$receipt | Add-Member -MemberType NoteProperty -Name workshop_asset_type -Value 'Object/Effect' -Force
$receipt | Add-Member -MemberType NoteProperty -Name compatibility_version -Value 6 -Force
$receipt | Add-Member -MemberType NoteProperty -Name tags -Value @('Object','Effect') -Force
$receipt | Add-Member -MemberType NoteProperty -Name published -Value $false -Force
$receipt | Add-Member -MemberType NoteProperty -Name upload_status -Value 'prepared' -Force
$receipt | Add-Member -MemberType NoteProperty -Name workshop_visibility -Value 'Public' -Force
$receipt | Add-Member -MemberType NoteProperty -Name public_page_verified -Value $false -Force
$receipt | Add-Member -MemberType NoteProperty -Name in_game_verified -Value $false -Force
$receipt | Add-Member -MemberType NoteProperty -Name downloaded_payload_verified -Value $false -Force
$receipt | Add-Member -MemberType NoteProperty -Name downloaded_payload_sha256_match -Value $false -Force
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 8) + [Environment]::NewLine, $utf8)
[pscustomobject]@{ Package=$package; WorkshopFileId=$itemId; BundleSHA256=$receipt.bundle_sha256; Status=$receipt.upload_status }
