[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runtimeRoot = Join-Path $projectRoot 'runtime'
$serverRoot = Join-Path $runtimeRoot 'server'
$instanceRoot = [IO.Path]::GetFullPath((Join-Path $serverRoot 'Servers\MRE'))
$backupRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'backups'))
$lockPath = Join-Path $runtimeRoot '.mre-maintenance.lock'

function Assert-Contained([string]$Path, [string]$Root) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Путь выходит за разрешенный каталог: $fullPath"
    }
    return $fullPath
}

function Assert-NoReparseAncestors([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current -and $current.Length -ge $projectRoot.Length) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Каталог или файл восстановления не может быть ссылкой: $current"
            }
        }
        if ($current -eq $projectRoot) { break }
        $current = Split-Path -Parent $current
    }
}

function Assert-MreStopped {
    $serverPrefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    foreach ($processItem in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $imagePath = Get-MreProcessPath $processItem
        if ([string]::IsNullOrWhiteSpace($imagePath)) {
            throw "Не удалось определить путь Unturned PID $($processItem.ProcessId). Нельзя подтвердить остановку MRE."
        }
        if ([IO.Path]::GetFullPath($imagePath).StartsWith($serverPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "MRE запущен (PID $($processItem.ProcessId)). Сначала выполните корректный Shutdown."
        }
    }
}

function Get-SafeRelativePath([string]$Path, [bool]$Directory = $false) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Архив содержит пустой путь.' }
    $normalized = $Path.Replace('\', '/')
    if ($Directory) { $normalized = $normalized.TrimEnd('/') }
    if ($normalized.StartsWith('/') -or $normalized.Contains(':')) { throw "Недопустимый абсолютный путь: $Path" }
    foreach ($part in ($normalized -split '/')) {
        if (-not $part -or $part -in @('.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') {
            throw "Недопустимый сегмент пути архива: $Path"
        }
    }
    return $normalized
}

function Get-MreFiles([string]$Root) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($rootFull)
    $records = New-Object 'System.Collections.Generic.List[object]'
    while ($pending.Count -gt 0) {
        $directory = Get-Item -LiteralPath $pending.Pop() -Force
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Ссылка недопустима: $($directory.FullName)" }
        foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Ссылка недопустима: $($item.FullName)" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName); continue }
            $records.Add([pscustomobject]@{
                Path = $item.FullName.Substring($rootFull.Length + 1).Replace('\', '/')
                Length = [long]$item.Length
                Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            })
        }
    }
    return @($records | Sort-Object Path)
}

$archiveItem = Get-Item -LiteralPath $ArchivePath -Force
if ($archiveItem.PSIsContainer) { throw 'Вместо ZIP указан каталог.' }
$resolvedArchive = Assert-Contained $archiveItem.FullName $backupRoot
$manifestPath = $resolvedArchive + '.manifest.json'
Assert-NoReparseAncestors $resolvedArchive
Assert-NoReparseAncestors $manifestPath
Assert-NoReparseAncestors $instanceRoot
foreach ($requiredPath in @($resolvedArchive, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Файл не найден: $requiredPath" }
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.FormatVersion -ne 1 -or $manifest.Instance -ne 'MRE' -or $manifest.SourceRelativePath -ne 'runtime/server/Servers/MRE') {
    throw 'Манифест не соответствует формату резервной копии MRE.'
}
if ($manifest.ArchiveFile -ne [IO.Path]::GetFileName($resolvedArchive)) { throw 'Имя архива не совпадает с манифестом.' }
if ((Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash -ne $manifest.ArchiveSha256) { throw 'SHA256 архива не совпадает с манифестом.' }

$expected = @{}
foreach ($record in @($manifest.Files)) {
    $relative = Get-SafeRelativePath ([string]$record.Path)
    if ($expected.ContainsKey($relative) -or [long]$record.Length -lt 0 -or [string]$record.Sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw "Недопустимая или повторяющаяся запись манифеста: $relative"
    }
    $expected[$relative] = $record
}

New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
Assert-NoReparseAncestors $runtimeRoot
Assert-NoReparseAncestors $lockPath
$maintenanceLock = $null
$archive = $null
$originalLocation = $null
$stageRoot = $null
try {
    try { $maintenanceLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw 'MRE занят другой операцией запуска или обслуживания.' }
    Assert-MreStopped
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    $seen = @{}
    $seenEntries = @{}
    foreach ($entry in $archive.Entries) {
        $isDirectory = $entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')
        $relative = Get-SafeRelativePath $entry.FullName $isDirectory
        if ($seenEntries.ContainsKey($relative)) { throw "Повтор пути в архиве: $relative" }
        $seenEntries[$relative] = $true
        # Верхние Unix mode bits обозначают символическую ссылку; такие entries запрещены.
        if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw "Архив содержит символическую ссылку: $relative" }
        if ($isDirectory) { continue }
        if (-not $expected.ContainsKey($relative) -or $entry.Length -ne [long]$expected[$relative].Length) { throw "Состав архива не совпадает с манифестом: $relative" }
        $seen[$relative] = $true
    }
    if ($seen.Count -ne $expected.Count) { throw 'В архиве отсутствуют файлы из манифеста.' }

    if (-not $Apply -or -not $PSCmdlet.ShouldProcess($instanceRoot, 'Восстановить MRE с сохранением текущего каталога в backups')) {
        [pscustomobject]@{ Result = 'Preview'; ArchivePath = $resolvedArchive; RestoreTarget = $instanceRoot; FileCount = $expected.Count; ApplyRequired = $true }
        return
    }

    $operationId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $stageRoot = Assert-Contained (Join-Path $backupRoot ('.restore-stage-' + $operationId)) $backupRoot
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    foreach ($entry in $archive.Entries) {
        $isDirectory = $entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')
        $relative = Get-SafeRelativePath $entry.FullName $isDirectory
        $destination = Assert-Contained (Join-Path $stageRoot $relative.Replace('/', '\')) $stageRoot
        if ($isDirectory) { New-Item -ItemType Directory -Path $destination -Force | Out-Null; continue }
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        $inputStream = $entry.Open()
        $outputStream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected[$relative].Sha256) { throw "SHA256 файла не совпадает: $relative" }
    }
    $archive.Dispose()
    $archive = $null
    Assert-MreStopped
    Assert-NoReparseAncestors $instanceRoot
    $safetyRoot = $null
    if (Test-Path -LiteralPath $instanceRoot) {
        $safetyRoot = Assert-Contained (Join-Path $backupRoot ('MRE-before-restore-' + $operationId)) $backupRoot
        New-Item -ItemType Directory -Path $safetyRoot | Out-Null
        $safetyManifest = [ordered]@{ Instance = 'MRE'; CreatedUtc = [DateTime]::UtcNow.ToString('o'); Reason = 'before_restore'; RestoreArchive = [IO.Path]::GetFileName($resolvedArchive); Files = @(Get-MreFiles $instanceRoot) }
        [IO.File]::WriteAllText((Join-Path $safetyRoot 'manifest.json'), ($safetyManifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
        $originalLocation = Assert-Contained (Join-Path $safetyRoot 'MRE') $backupRoot
        # Оба абсолютных пути проверены до перемещения; исходные данные не удаляются.
        $null = Assert-Contained $instanceRoot $serverRoot
        [IO.Directory]::Move($instanceRoot, $originalLocation)
    }
    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $instanceRoot) -Force | Out-Null
        $null = Assert-Contained $instanceRoot $serverRoot
        [IO.Directory]::Move($stageRoot, $instanceRoot)
        $stageRoot = $null
    } catch {
        if ($originalLocation -and -not (Test-Path -LiteralPath $instanceRoot)) {
            [IO.Directory]::Move($originalLocation, $instanceRoot)
            $originalLocation = $null
        }
        throw
    }
    [pscustomobject]@{ Result = 'Restored'; ArchivePath = $resolvedArchive; RestoreTarget = $instanceRoot; FileCount = $expected.Count; PreviousDataPath = $originalLocation }
} finally {
    if ($archive) { $archive.Dispose() }
    if ($maintenanceLock) { $maintenanceLock.Dispose() }
}
