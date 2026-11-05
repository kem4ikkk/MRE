$ErrorActionPreference = 'Stop'
$inspect = Join-Path $PSScriptRoot 'Inspect-MreTradeJournal.ps1'
$dir = Join-Path ([IO.Path]::GetTempPath()) ('mre-inspect-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dir) | Out-Null
try {
    $mem = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($mem)
    $writer.Write([int]0x4D545244); $writer.Write([byte]1); $writer.Write([uint64]1); $writer.Write([uint64]2); $writer.Write([long]638000000000000000)
    $writer.Write([int]1); $writer.Write([byte]2); $writer.Write([byte]3); $writer.Write([byte]4); $writer.Write([byte]1); $writer.Write([uint16]42); $writer.Write([byte]1); $writer.Write([byte]90); $writer.Write([byte]2); $writer.Write([byte[]](9, 10))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]1)
    foreach ($value in 2,3,4,5,6,7,1) { $writer.Write([byte]$value) }
    $writer.Flush(); $payload = $mem.ToArray(); $writer.Dispose(); $mem.Dispose()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($payload) } finally { $sha.Dispose() }
    $path = Join-Path $dir 'sample.pending'
    [IO.File]::WriteAllBytes($path, [byte[]]($payload + $hash))
    $before = [IO.File]::ReadAllBytes($path)
    $result = & $inspect -JournalPath $path | ConvertFrom-Json
    if ($result.playerA -ne '1' -or $result.playerB -ne '2' -or $result.inventoryBeforeA[0].id -ne 42 -or $result.inventoryBeforeA[0].stateBase64 -ne 'CQo=' -or $result.plannedToB[0].destinationX -ne 6) { throw 'Неверное содержимое отчёта.' }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -ne [Convert]::ToBase64String($before)) { throw 'Инспектор изменил исходный файл.' }
    $before[10] = $before[10] -bxor 1; [IO.File]::WriteAllBytes($path, $before)
    $rejected = $false
    try { & $inspect -JournalPath $path | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Повреждённый файл не отклонён.' }
    [IO.File]::WriteAllBytes($path, [byte[]](1,2,3))
    $rejected = $false
    try { & $inspect -JournalPath $path | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Короткий файл не отклонён.' }
    Write-Host 'Проверки офлайн-инспектора пройдены.'
} finally { Remove-Item -LiteralPath $dir -Recurse -Force }

