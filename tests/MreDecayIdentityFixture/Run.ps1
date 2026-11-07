[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [switch]$ResumeRecreate,
    [ValidateRange(60,900)][int]$TimeoutSeconds = 360
)
$ErrorActionPreference = 'Stop'
$work = [IO.Path]::GetFullPath($WorkDirectory)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$serverId = 'MREDecayGroundIdentityFixture'
if (-not $work.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'WorkDirectory must be inside TEMP.' }
$instance = Join-Path $work 'instance'
$exe = [IO.Path]::GetFullPath((Join-Path $instance 'Unturned.exe'))
$receipt = Join-Path $work 'prepare-receipt.txt'
$configPath = Join-Path $instance "Servers\$serverId\Rocket\Plugins\$serverId\$serverId.configuration.xml"
$serverLog = Join-Path $instance "Logs\Server_$serverId.log"
$state = Join-Path $instance "Servers\$serverId\Server\ground-identity-state.txt"
function Assert-NoReparsePath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path); $drive=[IO.Path]::GetPathRoot($full); $current=$drive
    foreach($part in $full.Substring($drive.Length).Split([char[]]@([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar),[StringSplitOptions]::RemoveEmptyEntries)) {
        $current=Join-Path $current $part
        if(Test-Path -LiteralPath $current) { $item=Get-Item -LiteralPath $current -Force; if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse-point path component refused: $current" } }
    }
}
function Assert-NoReparseTree([string]$Path) {
    Assert-NoReparsePath $Path
    $stack=New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push([IO.Path]::GetFullPath($Path))
    while($stack.Count -gt 0) {
        $current=$stack.Pop()
        if(-not (Test-Path -LiteralPath $current)) { throw "Expected path is missing: $current" }
        $item=Get-Item -LiteralPath $current -Force
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $current" }
        if($item.PSIsContainer) {
            foreach($child in Get-ChildItem -LiteralPath $current -Force) {
                if($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Nested reparse point refused: $($child.FullName)" }
                if($child.PSIsContainer) { $stack.Push($child.FullName) }
            }
        }
    }
}
Assert-NoReparsePath $tempRoot
Assert-NoReparsePath $work
Assert-NoReparseTree $instance
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or -not (Test-Path -LiteralPath $receipt -PathType Leaf) -or
    -not (Test-Path -LiteralPath $configPath -PathType Leaf)) { throw 'Prepared fixture is incomplete.' }
$receiptText = Get-Content -LiteralPath $receipt -Raw -Encoding UTF8
if ($receiptText -notmatch '(?m)^server_id=MREDecayGroundIdentityFixture\r?$' -or $receiptText -notmatch '(?m)^port=(\d+)\r?$') { throw 'Unexpected fixture identity receipt.' }
$port = [int]$Matches[1]
$fixtureFiles = @($exe,(Join-Path $instance 'Unturned_Data\Managed\Assembly-CSharp.dll'),(Join-Path $instance "Servers\$serverId\Rocket\Plugins\$serverId\$serverId.dll"))
foreach ($file in $fixtureFiles) {
    $pattern = '(?m)^sha256=([A-Fa-f0-9]{64}) ' + [regex]::Escape([IO.Path]::GetFullPath($file)) + '\r?$'
    if ($receiptText -notmatch $pattern) { throw "Preparation receipt does not attest fixture file: $file" }
    $expectedHash = $Matches[1]
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expectedHash) { throw "Fixture file changed after preparation: $file" }
}
$commandsPath = Join-Path $instance "Servers\$serverId\Server\Commands.dat"
$mapPath = Join-Path $instance 'Maps/MREDecayTest'
$safeDat = Join-Path $mapPath 'Bundles/Items/Safe100/Safe100.dat'
$safeBundle = Join-Path $mapPath 'Bundles/Items/Safe100/Safe100.unity3d'
if (-not (Select-String -LiteralPath $commandsPath -SimpleMatch 'Map MREDecayTest' -Quiet) -or
    -not (Test-Path -LiteralPath (Join-Path $mapPath 'Level.dat') -PathType Leaf) -or
    -not (Test-Path -LiteralPath $safeDat -PathType Leaf) -or -not (Test-Path -LiteralPath $safeBundle -PathType Leaf)) { throw 'Prepared instance is not using the complete MREDecayTest map bundle.' }
$safeText = Get-Content -LiteralPath $safeDat -Raw -Encoding UTF8
if ($safeText -notmatch '(?m)^GUID bf233036b60d403c836deb8ac0f03292\s*$' -or $safeText -notmatch '(?m)^ID 60971\s*$' -or $safeText -notmatch '(?m)^Type Storage\s*$') { throw 'MRE Safe100 asset identity is missing from the copied test map.' }
$sumFile=Join-Path $work 'map-stage/SHA256SUMS.txt'
if(-not(Test-Path -LiteralPath $sumFile -PathType Leaf)){throw 'Test map checksum manifest is missing.'}
foreach($line in Get-Content -LiteralPath $sumFile -Encoding ASCII) {
    if($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$'){throw 'Malformed test map checksum line.'}
    $relative=$Matches[2]
    if($relative.Split('/') -contains '..'){throw 'Parent traversal in test map checksum manifest.'}
    $file=Join-Path $mapPath ($relative.Replace('/','\'))
    if(-not(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Matches[1]){throw "Test map hash mismatch: $relative"}
}

function Start-Phase([string]$Mode,[string]$Phase) {
    try { $processes=@(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'" -ErrorAction Stop) }
    catch { throw 'Cannot enumerate Unturned.exe processes; refusing to start fixture.' }
    foreach ($existing in $processes) {
        if ([String]::IsNullOrWhiteSpace($existing.ExecutablePath)) { throw "Unturned.exe PID $($existing.ProcessId) path is inaccessible; refusing to start fixture." }
        try { $existingExe=[IO.Path]::GetFullPath($existing.ExecutablePath) } catch { throw "Unturned.exe PID $($existing.ProcessId) path is invalid; refusing to start fixture." }
        if ($existingExe.Equals($exe,[StringComparison]::OrdinalIgnoreCase)) { throw "Fixture process already running, PID $($existing.ProcessId)." }
    }
    foreach ($candidate in @($port,($port+1),($port+2))) { if(Get-NetUDPEndpoint -LocalPort $candidate -ErrorAction SilentlyContinue){throw "UDP port $candidate is in use."} }
    [xml]$configuration = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
    if ($configuration.GroundIdentityConfiguration.Enabled -ne 'true') { throw 'Fixture config is not enabled.' }
    $configuration.GroundIdentityConfiguration.Mode = $Mode
    $configuration.Save($configPath)
    if (Test-Path -LiteralPath $serverLog) { Remove-Item -LiteralPath $serverLog -Force }
    $unityLog = Join-Path $work "$Phase-unity.log"
    $serverLogCopy = Join-Path $work "$Phase-server.log"
    $oldMarker = $env:MRE_DECAY_GROUND_IDENTITY
    try {
        $env:MRE_DECAY_GROUND_IDENTITY = 'YES'
        $arguments = @('-batchmode','-nographics','-NoDefaultConsole','-logfile',('"'+$unityLog+'"'),('+LanServer/'+$serverId))
        $process = Start-Process -FilePath $exe -WorkingDirectory $instance -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if(-not $process -or $process.Id -le 0){throw 'Could not obtain the launched fixture process handle.'}
        if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Fixture PID $($process.Id) timed out; it remains running. Verify exact path before stopping it."}
    } finally { $env:MRE_DECAY_GROUND_IDENTITY=$oldMarker }
    if(-not(Test-Path -LiteralPath $serverLog -PathType Leaf)){throw "Server log missing after normal shutdown: $serverLog"}
    Copy-Item -LiteralPath $serverLog -Destination $serverLogCopy -Force
    $text=Get-Content -LiteralPath $serverLog -Raw -Encoding UTF8
    if($text -notmatch 'Quit game: server shutdown'){throw "$Phase did not end with normal server shutdown."}
    if($text -match '\[MRE ground identity fixture\] (FAILED|REFUSED)'){throw "$Phase fixture refused or failed; see $serverLogCopy"}
    return $text
}

if ($ResumeRecreate) {
    $createLogPath = Join-Path $work '01-create-server.log'
    $reloadLogPath = Join-Path $work '02-reload-delete-server.log'
    if (-not (Test-Path -LiteralPath $state -PathType Leaf) -or (Test-Path -LiteralPath ($state+'.verified'))) { throw 'ResumeRecreate requires the original state and no verified receipt.' }
    if (-not (Test-Path -LiteralPath $createLogPath -PathType Leaf)) { throw 'ResumeRecreate requires the saved first-phase server log.' }
    $create = Get-Content -LiteralPath $createLogPath -Raw -Encoding UTF8
    if ($create -notmatch '\[MRE ground identity fixture\] CREATE_PASS' -or
        $create -notmatch '\[MRE ground identity fixture\] CREATE_SAVE_REQUESTED' -or
        $create -notmatch 'Quit game: server shutdown' -or
        $create -match '\[MRE ground identity fixture\] (FAILED|REFUSED)') { throw 'First-phase log does not prove successful creation, save, and normal shutdown.' }
    if (-not (Test-Path -LiteralPath $reloadLogPath -PathType Leaf)) { throw 'ResumeRecreate requires the saved second-phase server log.' }
    $reload = Get-Content -LiteralPath $reloadLogPath -Raw -Encoding UTF8
    if ($reload -notmatch '\[MRE ground identity fixture\] RELOAD_ID_PASS' -or
        $reload -notmatch '\[MRE ground identity fixture\] DELETE_SAVE_REQUESTED' -or
        $reload -notmatch 'Quit game: server shutdown' -or
        $reload -match '\[MRE ground identity fixture\] (FAILED|REFUSED)') { throw 'Second-phase log does not prove successful deletion and normal shutdown.' }
} else {
    if ((Test-Path -LiteralPath $state) -or (Test-Path -LiteralPath ($state+'.verified'))) { throw 'Fixture receipts already exist; use a new TEMP copy or -ResumeRecreate after a verified deletion.' }
    $create=Start-Phase 'create' '01-create'
    if($create -notmatch '\[MRE ground identity fixture\] CREATE_PASS' -or $create -notmatch 'CREATE_SAVE_REQUESTED' -or -not(Test-Path -LiteralPath $state)){throw 'Initial generation was not created and saved.'}
    $reload=Start-Phase 'verify_delete' '02-reload-delete'
    if($reload -notmatch 'RELOAD_ID_PASS' -or $reload -notmatch 'DELETE_SAVE_REQUESTED'){throw 'Reload identity verification/deletion did not pass.'}
}
$recreate=Start-Phase 'recreate' '03-recreate'
if($recreate -notmatch 'DELETE_RECREATE_PASS' -or $recreate -notmatch 'RECREATE_SAVE_REQUESTED' -or -not(Test-Path -LiteralPath ($state+'.verified'))){throw 'Fresh generation did not differ from the deleted generation.'}
$artifacts=@($receipt,$state,($state+'.verified'),(Join-Path $work '01-create-server.log'),(Join-Path $work '02-reload-delete-server.log'),(Join-Path $work '03-recreate-server.log'),$configPath,(Join-Path $instance "Servers\$serverId\Rocket\Plugins\$serverId\$serverId.dll"),(Join-Path $instance 'Unturned_Data\Managed\Assembly-CSharp.dll'))
Get-FileHash -LiteralPath $artifacts -Algorithm SHA256 | Format-Table -AutoSize
Write-Output "PASS: isolated ordinary save/restart preserved same-object Kind:InstanceId and delete/recreate used a fresh ID generation. TEMP artifacts: $work"
Write-Output 'LIMIT: this does not test crash durability, transaction recovery, rollback resistance, or production decay readiness.'
