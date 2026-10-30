[CmdletBinding()]
param([ValidateRange(10, 300)][int]$TimeoutSeconds = 60)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $projectRoot 'runtime\mre-process.json'
$maintenanceLock = $null
try {
    $maintenanceLock = [IO.File]::Open((Join-Path $projectRoot 'runtime\.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
if (-not (Test-Path -LiteralPath $statePath)) { throw 'Нет локальной записи о процессе MRE.' }
$serverState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Send-MreServerCommand.ps1') -Command Save | Out-Null
& (Join-Path $PSScriptRoot 'Send-MreServerCommand.ps1') -Command Shutdown | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    $serverProcess = Get-Process -Id ([int]$serverState.ProcessId) -ErrorAction SilentlyContinue
    if (-not $serverProcess) {
        Write-Host 'MRE сохранил мир и корректно остановился.'
        return
    }
    Start-Sleep -Milliseconds 300
}
throw 'MRE не завершился за отведенное время. Принудительная остановка не выполнялась.'
}
finally {
    if ($maintenanceLock) { $maintenanceLock.Dispose() }
}
