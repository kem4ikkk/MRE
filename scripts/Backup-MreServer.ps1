[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_-]{1,80}$')]
    [string]$Name = ('MRE-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runtimeRoot = Join-Path $projectRoot 'runtime'
$serverRoot = Join-Path $runtimeRoot 'server'
$instanceRoot = Join-Path $serverRoot 'Servers\MRE'
$backupRoot = Join-Path $projectRoot 'backups'
$lockPath = Join-Path $runtimeRoot '.mre-maintenance.lock'

function Assert-NoReparseAncestors([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current -and $current.Length -ge $projectRoot.Length) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Каталог или файл операций не может быть ссылкой: $current"
            }
        }
        if ($current -eq $projectRoot) { break }
        $current = Split-Path -Parent $current
    }
}

function Assert-MreStopped {
    $serverPrefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    foreach ($processItem in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        if ([string]::IsNullOrWhiteSpace($processItem.ExecutablePath)) {
            throw "Не удалось определить путь Unturned PID $($processItem.ProcessId). Нельзя подтвердить остановку MRE."
        }
        if ([IO.Path]::GetFullPath($processItem.ExecutablePath).StartsWith($serverPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "MRE запущен (PID $($processItem.ProcessId)). Сначала выполните корректный Shutdown."
        }
    }
}

function Get-MreFiles([string]$Root) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($rootFull)
    $records = New-Object 'System.Collections.Generic.List[object]'
    while ($pending.Count -gt 0) {
        $directory = Get-Item -LiteralPath $pending.Pop() -Force
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Символическая ссылка или junction недопустимы в копии: $($directory.FullName)"
        }
        foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Символическая ссылка или junction недопустимы в копии: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $pending.Push($item.FullName)
                continue
            }
            $records.Add([pscustomobject]@{
                Path = $item.FullName.Substring($rootFull.Length + 1).Replace('\', '/')
                Length = [long]$item.Length
                Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            })
        }
    }
    return @($records | Sort-Object Path)
}

foreach ($directoryPath in @($runtimeRoot, $backupRoot)) {
    Assert-NoReparseAncestors $directoryPath
    New-Item -ItemType Directory -Path $directoryPath -Force | Out-Null
}
Assert-NoReparseAncestors $instanceRoot
Assert-NoReparseAncestors $lockPath

$archivePath = Join-Path $backupRoot ($Name + '.zip')
$manifestPath = $archivePath + '.manifest.json'
$partialPath = $archivePath + '.partial'
foreach ($existingPath in @($archivePath, $manifestPath, $partialPath)) {
    if (Test-Path -LiteralPath $existingPath) { throw "Файл уже существует, выберите другое имя: $existingPath" }
}

$maintenanceLock = $null
try {
    try {
        $maintenanceLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch {
        throw 'MRE занят другой операцией запуска или обслуживания. Повторите после ее завершения.'
    }
    Assert-MreStopped
    if (-not (Test-Path -LiteralPath $instanceRoot -PathType Container)) {
        throw 'Каталог runtime/server/Servers/MRE еще не создан. Сначала выполните первоначальный запуск.'
    }
    $files = @(Get-MreFiles $instanceRoot)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($instanceRoot, $partialPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    Assert-MreStopped

    # Проверка содержимого архива до публикации ловит изменения файлов во время копирования.
    $archive = [IO.Compression.ZipFile]::OpenRead($partialPath)
    try {
        $expected = @{}
        foreach ($record in $files) { $expected[$record.Path] = $record }
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('\', '/')
            if ($relative.EndsWith('/')) { continue }
            if (-not $expected.ContainsKey($relative) -or $seen.ContainsKey($relative)) {
                throw "Состав файлов изменился во время копирования: $relative"
            }
            if ($entry.Length -ne $expected[$relative].Length) { throw "Изменился размер файла: $relative" }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($actualHash -ne $expected[$relative].Sha256) { throw "Изменился файл во время копирования: $relative" }
            $seen[$relative] = $true
        }
        if ($seen.Count -ne $expected.Count) { throw 'Архив содержит не все ожидаемые файлы.' }
    } finally { $archive.Dispose() }

    [IO.File]::Move($partialPath, $archivePath)
    $manifest = [ordered]@{
        FormatVersion = 1
        Instance = 'MRE'
        SourceRelativePath = 'runtime/server/Servers/MRE'
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        ArchiveFile = [IO.Path]::GetFileName($archivePath)
        ArchiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Files = @($files)
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    [pscustomobject]@{ Result = 'BackupCreated'; ArchivePath = $archivePath; ManifestPath = $manifestPath; FileCount = $files.Count; ArchiveSha256 = $manifest.ArchiveSha256 }
} finally {
    if ($maintenanceLock) { $maintenanceLock.Dispose() }
}
