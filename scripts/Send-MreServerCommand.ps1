[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Save', 'Shutdown', 'Players', 'Day', 'Night', 'Diagnostics')]
    [string]$Command,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $projectRoot 'runtime\mre-process.json'
$expectedExe = Join-Path $projectRoot 'runtime\server\Unturned.exe'
if (-not (Test-Path -LiteralPath $statePath)) { throw 'Нет локальной записи о работающем MRE.' }
$serverState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
$serverProcess = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + [int]$serverState.ProcessId)
if (-not $serverProcess -or -not $serverProcess.ExecutablePath -or
    [IO.Path]::GetFullPath($serverProcess.ExecutablePath) -ne [IO.Path]::GetFullPath($expectedExe)) {
    throw 'Записанный процесс MRE не работает или его путь не совпадает.'
}
if ($serverState.PSObject.Properties.Name -contains 'ProcessStartedAtUtc') {
    $expectedStart = [DateTime]::Parse($serverState.ProcessStartedAtUtc).ToUniversalTime()
    $actualStart = ([DateTime]$serverProcess.CreationDate).ToUniversalTime()
    if ([math]::Abs(($actualStart - $expectedStart).TotalSeconds) -gt 1) {
        throw 'Время создания процесса MRE не совпадает. Возможно, Windows повторно использовала PID.'
    }
}
$controlRoot = Join-Path $projectRoot 'runtime\control\MRE'
$requestRoot = Join-Path $controlRoot 'requests'
$resultRoot = Join-Path $controlRoot 'results'
if (-not (Test-Path -LiteralPath $requestRoot)) { throw 'Локальный мост управления еще не готов.' }
$requestId = [Guid]::NewGuid().ToString('N')
$temporaryPath = Join-Path $requestRoot ($requestId + '.tmp')
$requestPath = Join-Path $requestRoot ($requestId + '.cmd')
$resultPath = Join-Path $resultRoot ($requestId + '.json')
[IO.File]::WriteAllText($temporaryPath, $Command, (New-Object Text.UTF8Encoding($false)))
[IO.File]::Move($temporaryPath, $requestPath)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $resultPath) {
        $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($result.status -notin @('completed', 'accepted')) {
            throw "Команда $Command не выполнена: $($result.status)."
        }
        $result
        return
    }
    Start-Sleep -Milliseconds 200
}
throw "За $TimeoutSeconds секунд нет подтверждения $Command. Не повторяйте операцию вслепую: проверьте результат $requestId."
