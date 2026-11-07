[CmdletBinding(SupportsShouldProcess = $true)]
param([Parameter(Mandatory=$true)][string]$Receipt)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runtimeRoot = Join-Path $projectRoot 'runtime'
$instanceRoot = Join-Path $runtimeRoot 'server\Servers\MRE'
$lockPath = Join-Path $runtimeRoot '.mre-maintenance.lock'
$receiptPath = [IO.Path]::GetFullPath($Receipt)
$backupPrefix = [IO.Path]::GetFullPath((Join-Path $runtimeRoot 'backups')).TrimEnd('\') + '\'
if (-not $receiptPath.StartsWith($backupPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Receipt rollback должен находиться в runtime/backups.' }
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw "Receipt не найден: $receiptPath" }
$data = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($data.schema -ne 1 -or $data.status -ne 'installed' -or -not $data.files) { throw 'Некорректный receipt или он уже откатан.' }
$root = [IO.Path]::GetFullPath($instanceRoot).TrimEnd('\') + '\'
function Assert-ServerStopped {
    . (Join-Path $projectRoot 'scripts\Get-MreProcessPath.ps1')
    $serverPath = [IO.Path]::GetFullPath((Join-Path $runtimeRoot 'server')).TrimEnd('\')
    $serverPrefix = $serverPath + '\'
    $serverExe = [IO.Path]::GetFullPath((Join-Path $serverPath 'Unturned.exe'))
    foreach ($processItem in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $imagePath = Get-MreProcessPath $processItem
        if (-not $imagePath) { throw 'Не удалось проверить путь работающего Unturned; rollback отменён.' }
        $canonicalImagePath = [IO.Path]::GetFullPath($imagePath)
        if ($canonicalImagePath.Equals($serverExe,[StringComparison]::OrdinalIgnoreCase) -or $canonicalImagePath.StartsWith($serverPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Сначала корректно остановите MRE.' }
    }
}
function Write-JsonAtomic([string]$Path,$Value,[int]$Depth) {
    $directory = Split-Path -Parent $Path
    $temporary = Join-Path $directory (([IO.Path]::GetFileName($Path)) + '.mre-write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $encoding = New-Object Text.UTF8Encoding($false)
    try {
        [IO.File]::WriteAllText($temporary,($Value | ConvertTo-Json -Depth $Depth),$encoding)
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary,$Path,$null) }
        else { [IO.File]::Move($temporary,$Path) }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
function Write-TextAtomic([string]$Path,[string]$Content) {
    $directory = Split-Path -Parent $Path
    $temporary = Join-Path $directory (([IO.Path]::GetFileName($Path)) + '.mre-write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $encoding = New-Object Text.UTF8Encoding($false)
    try {
        [IO.File]::WriteAllText($temporary,$Content,$encoding)
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary,$Path,$null) }
        else { [IO.File]::Move($temporary,$Path) }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
function Assert-RollbackPreconditions {
    foreach ($file in $data.files) {
        $path = [IO.Path]::GetFullPath([string]$file.path)
        if (-not $path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { throw "Receipt ссылается за пределы сервера: $path" }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Целевой файл после установки отсутствует; откат остановлен без записи: $path" }
        if ([string]::IsNullOrWhiteSpace([string]$file.after_sha256) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne ([string]$file.after_sha256).ToLowerInvariant()) {
            throw "Целевой файл изменён после установки; откат остановлен без записи: $path"
        }
        if ($file.existed) {
            if (-not $file.backup) { throw "Нет резервной копии для $path" }
            $backupPath = [IO.Path]::GetFullPath([string]$file.backup)
            if (-not $backupPath.StartsWith($backupPrefix,[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $backupPath -PathType Leaf)) { throw "Резервная копия вне каталога MRE или отсутствует: $backupPath" }
            if ((Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne ([string]$file.before_sha256).ToLowerInvariant()) { throw "SHA256 резервной копии не совпадает: $path" }
        }
    }
}
Assert-ServerStopped
Assert-RollbackPreconditions
if (-not $PSCmdlet.ShouldProcess($instanceRoot,'Откатить MRE Admin и все связанные plugin DLL из проверенного receipt')) { return }
$lockHandle = $null
try {
    $lockHandle = [IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None')
    Assert-ServerStopped
    # Recheck every target and backup under the maintenance lock before the first write.
    Assert-RollbackPreconditions
    $attemptRoot = Join-Path (Split-Path -Parent $receiptPath) ('rollback-attempt-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    New-Item -ItemType Directory -Path $attemptRoot -Force | Out-Null
    $targetSnapshots = @()
    for ($index = 0; $index -lt $data.files.Count; $index++) {
        $path = [IO.Path]::GetFullPath([string]$data.files[$index].path)
        $snapshot = Join-Path $attemptRoot ('target-' + $index.ToString('D3') + '.snapshot')
        Copy-Item -LiteralPath $path -Destination $snapshot
        $targetSnapshots += [ordered]@{ path=$path; snapshot=$snapshot }
    }
    $rollbackPath = Join-Path (Split-Path -Parent $receiptPath) 'rollback-receipt.json'
    $originalReceiptText = [IO.File]::ReadAllText($receiptPath,[Text.Encoding]::UTF8)
    $hadRollbackReceipt = [IO.File]::Exists($rollbackPath)
    $originalRollbackText = if ($hadRollbackReceipt) { [IO.File]::ReadAllText($rollbackPath,[Text.Encoding]::UTF8) } else { $null }
    try {
        for ($index = $data.files.Count - 1; $index -ge 0; $index--) {
            $file = $data.files[$index]
            $path = [IO.Path]::GetFullPath([string]$file.path)
            if ($file.existed) { Copy-Item -LiteralPath ([string]$file.backup) -Destination $path -Force }
            elseif (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
        }
        $rolledBackUtc = [DateTime]::UtcNow.ToString('o')
        $rollback = [ordered]@{ schema=1; status='rolled_back'; rolled_back_utc=$rolledBackUtc; source_receipt=$receiptPath; restored_files=@($data.files | ForEach-Object { $_.path }) }
        $data.status = 'rolled_back'
        $data | Add-Member -NotePropertyName rolled_back_utc -NotePropertyValue $rolledBackUtc -Force
        Write-JsonAtomic $receiptPath $data 8
        Write-JsonAtomic $rollbackPath $rollback 5
    }
    catch {
        $rollbackError = $_
        $compensationErrors = @()
        for ($snapshotIndex = $targetSnapshots.Count - 1; $snapshotIndex -ge 0; $snapshotIndex--) {
            $snapshot = $targetSnapshots[$snapshotIndex]
            try { Copy-Item -LiteralPath $snapshot.snapshot -Destination $snapshot.path -Force }
            catch { $compensationErrors += ("цель {0}: {1}" -f $snapshot.path,$_.Exception.Message) }
        }
        try { Write-TextAtomic $receiptPath $originalReceiptText }
        catch { $compensationErrors += ("receipt {0}: {1}" -f $receiptPath,$_.Exception.Message) }
        try {
            if ($hadRollbackReceipt) { Write-TextAtomic $rollbackPath $originalRollbackText }
            elseif ([IO.File]::Exists($rollbackPath)) { [IO.File]::Delete($rollbackPath) }
        }
        catch { $compensationErrors += ("rollback receipt {0}: {1}" -f $rollbackPath,$_.Exception.Message) }
        if ($compensationErrors.Count -gt 0) {
            throw ("Rollback failed ({0}); compensation also failed: {1}. Recovery snapshots: {2}" -f $rollbackError.Exception.Message,($compensationErrors -join '; '),$attemptRoot)
        }
        throw "Откат не завершён; исходные целевые файлы и receipt восстановлены из snapshot. Причина: $($rollbackError.Exception.Message). Snapshot: $attemptRoot"
    }
    Write-Host "Откат завершён. Receipt: $rollbackPath"
}
finally { if ($lockHandle) { $lockHandle.Dispose() } }

