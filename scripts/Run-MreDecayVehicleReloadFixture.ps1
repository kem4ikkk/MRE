[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$WorkDirectory,
    [ValidateRange(60, 900)][int]$TimeoutSeconds = 360
)

$ErrorActionPreference = 'Stop'
$work = [IO.Path]::GetFullPath($WorkDirectory)
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (-not $work.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) { throw 'WorkDirectory must be inside TEMP.' }
$instance = Join-Path $work 'instance'
$exe = [IO.Path]::GetFullPath((Join-Path $instance 'Unturned.exe'))
$receipt = Join-Path $work 'prepare-receipt.txt'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or -not (Test-Path -LiteralPath $receipt -PathType Leaf)) { throw 'Fixture copy or preparation receipt is missing.' }
$receiptText = Get-Content -LiteralPath $receipt -Raw -Encoding UTF8
if ($receiptText -notmatch '(?m)^server_id=MREDecayVehicleReloadFixture\r?$' -or $receiptText -notmatch '(?m)^port=(\d+)\r?$') { throw 'Preparation receipt has an unexpected server identity.' }
$port = [int]$Matches[1]
$serverId = 'MREDecayVehicleReloadFixture'
$configPath = Join-Path $instance "Servers\$serverId\Rocket\Plugins\$serverId\$serverId.configuration.xml"
$logPath = Join-Path $instance "Logs\Server_$serverId.log"
$pluginLog1 = Join-Path $work 'attach-server.log'
$pluginLog2 = Join-Path $work 'verify-server.log'
$unityLog1 = Join-Path $work 'attach-unity.log'
$unityLog2 = Join-Path $work 'verify-unity.log'
$statePath = Join-Path $instance "Servers\$serverId\Server\vehicle-reload-state.txt"

function Assert-NoFixtureProcess {
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'")) {
        $path = $null
        try { $path = $p.ExecutablePath } catch { }
        if ($path -and [IO.Path]::GetFullPath($path).Equals($exe, [StringComparison]::OrdinalIgnoreCase)) { throw "Fixture server already running, PID $($p.ProcessId)." }
    }
}

function Assert-PortFree {
    foreach ($candidate in @($port, ($port + 1), ($port + 2))) {
        if (Get-NetUDPEndpoint -LocalPort $candidate -ErrorAction SilentlyContinue) { throw "UDP port $candidate is already in use." }
    }
}

function Start-FixturePhase([string]$unityLog, [string]$serverLogCopy) {
    Assert-NoFixtureProcess
    Assert-PortFree
    $oldEnv = $env:MRE_DECAY_VEHICLE_RELOAD
    try {
        $env:MRE_DECAY_VEHICLE_RELOAD = 'YES'
        $arguments = @('-batchmode', '-nographics', '-NoDefaultConsole', '-logfile', ('"' + $unityLog + '"'), ('+LanServer/' + $serverId))
        $process = Start-Process -FilePath $exe -WorkingDirectory $instance -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        $identityConfirmed = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            $live = Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)" -ErrorAction SilentlyContinue
            if ($live) {
                $actualExe = $null
                try { $actualExe = [IO.Path]::GetFullPath($live.ExecutablePath) } catch { }
                if (-not $actualExe -or -not $actualExe.Equals($exe, [StringComparison]::OrdinalIgnoreCase) -or
                    -not $live.CommandLine.Contains('+LanServer/' + $serverId)) { throw 'Started process path/command line does not match the isolated fixture.' }
                $identityConfirmed = $true
                break
            }
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 200
        }
        if (-not $identityConfirmed) { throw "Could not confirm isolated fixture process identity for PID $($process.Id)." }
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Fixture PID $($process.Id) timed out. It remains running; verify its executable path before stopping it." }
    }
    finally { $env:MRE_DECAY_VEHICLE_RELOAD = $oldEnv }
    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) { throw "Server log is missing after normal process exit: $logPath" }
    Copy-Item -LiteralPath $logPath -Destination $serverLogCopy -Force
    $text = Get-Content -LiteralPath $logPath -Raw -Encoding UTF8
    if ($text -notmatch 'Quit game: server shutdown') { throw 'Fixture did not report normal server shutdown.' }
    return $text
}

if ((Test-Path -LiteralPath $statePath) -or (Test-Path -LiteralPath ($statePath + '.verified')) -or (Test-Path -LiteralPath ($statePath + '.previous'))) { throw 'Fixture state already exists; use a fresh TEMP copy.' }
$attachText = Start-FixturePhase $unityLog1 $pluginLog1
if ($attachText -notmatch '\[MRE vehicle reload fixture\] ATTACH_PASS' -or
    $attachText -notmatch '\[MRE vehicle reload fixture\] SAVE_REQUESTED' -or
    $attachText -match '\[MRE vehicle reload fixture\] (FAILED|REFUSED)') { throw 'Attach phase did not pass its guarded mutation and save checks.' }
if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { throw 'Attach phase did not write its state receipt.' }

[xml]$configuration = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
if ($configuration.VehicleReloadConfiguration.Mode -ne 'attach') { throw 'Fixture config changed unexpectedly before verify phase.' }
$configuration.VehicleReloadConfiguration.Mode = 'verify'
$configuration.Save($configPath)
Remove-Item -LiteralPath $logPath -Force
$verifyText = Start-FixturePhase $unityLog2 $pluginLog2
if ($verifyText -notmatch '\[MRE vehicle reload fixture\] RELOAD_PASS' -or
    $verifyText -notmatch '\[MRE vehicle reload fixture\] VERIFY_SAVE_REQUESTED' -or
    $verifyText -match '\[MRE vehicle reload fixture\] (FAILED|REFUSED)' -or
    -not (Test-Path -LiteralPath ($statePath + '.verified') -PathType Leaf)) { throw 'Reload phase did not confirm the persisted attachment.' }

$fingerprints = @($receipt, $pluginLog1, $pluginLog2, $unityLog1, $unityLog2, $statePath, ($statePath + '.verified'), (Join-Path $instance "Servers\$serverId\Rocket\Plugins\$serverId\$serverId.configuration.xml"), (Join-Path $instance "Servers\$serverId\Rocket\Plugins\$serverId.dll"), (Join-Path $instance 'Unturned_Data\Managed\Assembly-CSharp.dll'))
Get-FileHash -Algorithm SHA256 -LiteralPath $fingerprints | Format-Table -AutoSize
Write-Output "PASS: isolated attach/save/shutdown/restart/verify/shutdown sequence completed. TEMP artifacts: $work"
