# Read-only inspection of a version 1 MreTrade transaction journal.
# The result is evidence for manual reconciliation, not a recovery instruction.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $JournalPath)
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $JournalPath).Path
if ([IO.Path]::GetExtension($resolved) -notin @('.pending', '.done')) { throw 'Ожидается файл .pending или .done.' }
$info = Get-Item -LiteralPath $resolved
if ($info.PSIsContainer -or $info.Length -lt 61 -or $info.Length -gt (1MB + 32)) { throw 'Недопустимый размер файла журнала.' }
$bytes = [IO.File]::ReadAllBytes($resolved)
if ($bytes.Length -lt 61 -or $bytes.Length -gt (1MB + 32)) { throw 'Недопустимый размер файла журнала.' }
$payloadLength = $bytes.Length - 32
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = $sha.ComputeHash($bytes, 0, $payloadLength) } finally { $sha.Dispose() }
for ($i = 0; $i -lt 32; $i++) { if ($hash[$i] -ne $bytes[$payloadLength + $i]) { throw 'Контрольная сумма не совпадает; файл повреждён или неполный.' } }
$stream = New-Object IO.MemoryStream(,$bytes)
$reader = New-Object IO.BinaryReader($stream)
function Read-Inventory($r) {
    $count = $r.ReadInt32()
    if ($count -lt 0 -or $count -gt 1000) { throw 'Недопустимое число предметов.' }
    $result = @()
    for ($n = 0; $n -lt $count; $n++) {
        $page = $r.ReadByte(); $x = $r.ReadByte(); $y = $r.ReadByte(); $rotation = $r.ReadByte()
        $id = $r.ReadUInt16(); $amount = $r.ReadByte(); $quality = $r.ReadByte(); $length = $r.ReadByte()
        $state = $r.ReadBytes($length)
        if ($state.Length -ne $length) { throw 'Неполное состояние предмета.' }
        $result += [pscustomobject]@{ page = $page; x = $x; y = $y; rotation = $rotation; id = $id; amount = $amount; quality = $quality; stateBase64 = [Convert]::ToBase64String($state) }
    }
    return ,$result
}
function Read-Placements($r) {
    $count = $r.ReadInt32()
    if ($count -lt 0 -or $count -gt 10) { throw 'Недопустимое число переносимых предметов.' }
    $result = @()
    for ($n = 0; $n -lt $count; $n++) {
        $result += [pscustomobject]@{ sourcePage = $r.ReadByte(); sourceX = $r.ReadByte(); sourceY = $r.ReadByte(); destinationPage = $r.ReadByte(); destinationX = $r.ReadByte(); destinationY = $r.ReadByte(); destinationRotation = $r.ReadByte() }
    }
    return ,$result
}
try {
    if ($reader.ReadInt32() -ne 0x4D545244 -or $reader.ReadByte() -ne 1) { throw 'Неизвестный формат или версия журнала.' }
    $a = $reader.ReadUInt64().ToString(); $b = $reader.ReadUInt64().ToString(); $ticks = $reader.ReadInt64()
    $utc = [DateTime]::new($ticks, [DateTimeKind]::Utc).ToString('o')
    $inventoryA = Read-Inventory $reader; $inventoryB = Read-Inventory $reader
    $toA = Read-Placements $reader; $toB = Read-Placements $reader
    if ($stream.Position -ne $payloadLength) { throw 'Неверная длина данных журнала.' }
    [pscustomobject]@{
        warning = 'Снимки сделаны до переноса. План и расширение файла не доказывают фактическое состояние сохранений; не выдавайте предметы автоматически.'
        file = $resolved; operation = [IO.Path]::GetFileNameWithoutExtension($resolved); sha256 = ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
        createdUtc = $utc; playerA = $a; playerB = $b; inventoryBeforeA = $inventoryA; inventoryBeforeB = $inventoryB; plannedToA = $toA; plannedToB = $toB
    } | ConvertTo-Json -Depth 6
} finally { $reader.Dispose(); $stream.Dispose() }

