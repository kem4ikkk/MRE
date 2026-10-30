[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$Apply,
    [switch]$IncludeVehicles
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$serverRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'runtime\server'))
$instanceRoot = Join-Path $serverRoot 'Servers\MRE'
$configPath = Join-Path $instanceRoot 'WorkshopDownloadConfig.json'
$manifestPath = Join-Path $projectRoot 'config\workshop.json'
$utf8 = New-Object Text.UTF8Encoding($false)

if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw 'Сначала один раз запустите MRE и корректно остановите сервер, чтобы он создал WorkshopDownloadConfig.json.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$originalConfigHash = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash
$workshopConfig = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($originalConfigHash -ne (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash) {
    throw 'Конфигурация Workshop изменилась во время чтения. Повторите команду.'
}
$managedIds = @($manifest.mods | ForEach-Object { [long]$_.id })
$forbiddenIds = @($manifest.excluded_without_permission | ForEach-Object { [long]$_.id })
$selectedIds = @($manifest.mods | Where-Object {
    $_.default_enabled -or ($IncludeVehicles -and [long]$_.id -eq 3485061773)
} | ForEach-Object { [long]$_.id })

$existingIds = @()
if ($workshopConfig.PSObject.Properties.Name -contains 'File_IDs') {
    $existingIds = @($workshopConfig.File_IDs | ForEach-Object { [long]$_ })
}
if (@($existingIds | Where-Object { $_ -in $forbiddenIds }).Count) {
    throw 'В конфигурации уже есть ограниченные ресурсы REDLINE. Разберите их права и удалите вручную перед применением списка MRE.'
}
$mergedIds = @(@($existingIds | Where-Object { $_ -notin $managedIds }) + $selectedIds | Sort-Object -Unique)
if (@($mergedIds | Where-Object { $_ -le 0 }).Count) {
    throw 'Список Workshop содержит некорректный ID.'
}
$workshopConfig | Add-Member -MemberType NoteProperty -Name 'File_IDs' -Value ([long[]]$mergedIds) -Force

Write-Host ('Workshop для MRE: ' + ($mergedIds -join ', '))
Write-Host 'Неизвестные свойства JSON и сторонние ID сохраняются. Управляемый William Vehicles отключен без -IncludeVehicles.'
if (-not $Apply) {
    Write-Host 'Предпросмотр: изменения не записаны. Для применения к остановленному серверу добавьте -Apply.'
    return
}
if (-not $PSCmdlet.ShouldProcess($configPath, 'Сохранить объединенный список Workshop MRE')) { return }

$lockPath = Join-Path $projectRoot 'runtime\.mre-maintenance.lock'
$lockHandle = $null
try {
    try {
        $lockHandle = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch {
        throw 'Другая операция MRE заняла блокировку обслуживания. Повторите после ее завершения.'
    }
    $running = @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'" | Where-Object {
        $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath).StartsWith(
            $serverRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($running.Count) {
        throw 'Сервер MRE работает. Корректно остановите его перед изменением Workshop.'
    }
    if ($originalConfigHash -ne (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash) {
        throw 'Конфигурация Workshop изменилась после чтения. Повторите команду для объединения актуальных значений.'
    }
    $backupRoot = Join-Path $projectRoot 'runtime\config-backups\MRE'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
    Copy-Item -LiteralPath $configPath -Destination (Join-Path $backupRoot ("WorkshopDownloadConfig-$stamp.json"))
    $temporaryPath = $configPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporaryPath, ($workshopConfig | ConvertTo-Json -Depth 32) + [Environment]::NewLine, $utf8)
    Move-Item -LiteralPath $temporaryPath -Destination $configPath -Force
    Write-Host 'WorkshopDownloadConfig.json обновлен. Резервная копия лежит в runtime/config-backups/MRE.'
} finally {
    if ($lockHandle) { $lockHandle.Dispose() }
}
