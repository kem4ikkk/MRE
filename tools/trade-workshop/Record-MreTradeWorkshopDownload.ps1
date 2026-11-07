[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$DownloadedPayload
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$package = [IO.Path]::GetFullPath($PackagePath)
$payload = [IO.Path]::GetFullPath($DownloadedPayload)
$receiptPath = Join-Path $package 'package.json'
$validator = Join-Path $root 'tests\CheckMreTradeWorkshopPackage.py'
$receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($receipt.published -ne $true -or $receipt.upload_status -ne 'steam_client_ok' -or
    [UInt64]$receipt.workshop_file_id -ne 3834982706 -or (Split-Path -Leaf $payload) -ne '3834982706') {
    throw 'Receipt или каталог payload не соответствует опубликованному MRE Trade item.'
}
& python $validator $package --downloaded-payload $payload
if ($LASTEXITCODE -ne 0) { throw 'SteamCMD payload не прошёл сравнение состава/хешей; receipt не изменён.' }
$moscow = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, [TimeZoneInfo]::FindSystemTimeZoneById('Russian Standard Time'))
$receipt | Add-Member -MemberType NoteProperty -Name downloaded_payload_verified -Value $true -Force
$receipt | Add-Member -MemberType NoteProperty -Name downloaded_payload_sha256_match -Value $true -Force
$receipt | Add-Member -MemberType NoteProperty -Name downloaded_at -Value ($moscow.ToString('yyyy-MM-dd HH:mm:ss') + ' Europe/Moscow') -Force
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 8) + [Environment]::NewLine, $utf8)
[pscustomobject]@{ WorkshopFileId=$receipt.workshop_file_id; BundleSHA256=$receipt.bundle_sha256; DownloadedAt=$receipt.downloaded_at }
