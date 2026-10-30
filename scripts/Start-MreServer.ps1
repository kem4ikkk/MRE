[CmdletBinding()]
param(
    [switch]$WaitForReady,
    [ValidateRange(10, 300)][int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$serverRoot = Join-Path $runtimeRoot 'server'
$serverExe = Join-Path $serverRoot 'Unturned.exe'
$statePath = Join-Path $runtimeRoot 'mre-process.json'
$pluginPath = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins\MreEssentials.dll'
$maintenanceLock = $null

try {
    $maintenanceLock = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    if (-not (Test-Path -LiteralPath $serverExe)) { throw 'Сначала установите U3DS.' }
    if (-not (Test-Path -LiteralPath $pluginPath)) { throw 'Сначала соберите и установите MreEssentials для управления сервером.' }
    if (-not (Test-Path -LiteralPath (Join-Path $serverRoot 'Servers\MRE\Server\Commands.dat'))) {
        throw 'Сначала выполните Initialize-MreServer.ps1.'
    }
    $serverProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'")
    foreach ($serverProcess in $serverProcesses) {
        if (-not $serverProcess.ExecutablePath) { throw 'Не удалось определить путь одного из процессов Unturned.' }
        if ([IO.Path]::GetFullPath($serverProcess.ExecutablePath) -eq [IO.Path]::GetFullPath($serverExe)) {
            throw "Сервер из этой установки уже работает, PID $($serverProcess.ProcessId)."
        }
    }
    $startedAt = [DateTime]::UtcNow
    $logDirectory = Join-Path $projectRoot 'logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $unityLog = Join-Path $logDirectory ('MRE-' + $startedAt.ToString('yyyyMMdd-HHmmss-fffffff') + '.log')
    $arguments = @('-batchmode', '-nographics', '-NoDefaultConsole', '-LogGameplayConfig', '-logfile', ('"' + $unityLog + '"'), '+LanServer/MRE')
    $startedProcess = Start-Process -FilePath $serverExe -ArgumentList $arguments -WorkingDirectory $serverRoot -WindowStyle Hidden -PassThru
    $state = [ordered]@{
        ProcessId = $startedProcess.Id
        ProcessStartedAtUtc = $startedProcess.StartTime.ToUniversalTime().ToString('o')
        ExecutablePath = $serverExe
        StartedAtUtc = $startedAt.ToString('o')
        Instance = 'MRE'
        Mode = 'LAN'
        Address = '127.0.0.1'
        Port = 27015
        UnityLog = $unityLog
    }
    $temporaryStatePath = $statePath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporaryStatePath, ($state | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $statePath) {
        [IO.File]::Replace($temporaryStatePath, $statePath, $statePath + '.previous')
    } else {
        [IO.File]::Move($temporaryStatePath, $statePath)
    }
    Write-Host "MRE запущен в фоновом режиме, PID $($startedProcess.Id)."
    Write-Host "Журнал: $unityLog"
}
finally {
    if ($maintenanceLock) { $maintenanceLock.Dispose() }
}

if ($WaitForReady) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue)) {
            throw 'Процесс MRE завершился до готовности. Проверьте журнал.'
        }
        $combinedText = ''
        foreach ($logItem in @(Get-ChildItem -LiteralPath (Join-Path $serverRoot 'Logs') -File -ErrorAction SilentlyContinue | Where-Object LastWriteTimeUtc -ge $startedAt)) {
            $combinedText += Get-Content -LiteralPath $logItem.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $unityLog) { $combinedText += Get-Content -LiteralPath $unityLog -Raw -Encoding UTF8 -ErrorAction SilentlyContinue }
        if ($combinedText -match 'Loading level: 100%' -and $combinedText -match '\[MRE\] Local control ready') {
            Write-Host 'PEI загружена, локальное управление готово.'
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "За $TimeoutSeconds секунд готовность не подтверждена. Процесс оставлен работающим; проверьте журналы и используйте Stop-MreServer.ps1."
}
