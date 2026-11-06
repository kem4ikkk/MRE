param(
 [Parameter(Mandatory=$true)][string]$ServerSource,
 [Parameter(Mandatory=$true)][string]$Collection,
 [Parameter(Mandatory=$true)][string]$MapSource,
 [string]$WorkDirectory=(Join-Path $env:TEMP ('mre-decay-server-test-'+[guid]::NewGuid().ToString('N'))),
 [ValidateRange(20000,60000)][int]$Port=28050,
 [ValidateRange(60,900)][int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
$work=[IO.Path]::GetFullPath($WorkDirectory); $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
if(!$work.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)){throw 'WorkDirectory must be a new TEMP directory.'}
$source=(Resolve-Path -LiteralPath $ServerSource).Path
if(!(Test-Path -LiteralPath (Join-Path $source 'Unturned.exe') -PathType Leaf)){throw 'ServerSource must contain Unturned.exe.'}
if(Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue){throw 'Port is already in use.'}
# No junctions/symlinks may be followed when copying an untrusted source tree.
$skip=@('Servers','Logs','Maps','Sandbox','Cloud','steamapps','appcache','config')
$entries=@(Get-ChildItem -LiteralPath $source -Force | Where-Object {$_.Name -notin $skip})
foreach($entry in $entries){if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Reparse point refused: $($entry.FullName)"}; if($entry.PSIsContainer -and (Get-ChildItem -LiteralPath $entry.FullName -Force -Recurse | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint} | Select-Object -First 1)){throw "Reparse point in source: $($entry.FullName)"}}
New-Item -ItemType Directory -Path $work | Out-Null
$instance=Join-Path $work 'instance'; New-Item -ItemType Directory -Path $instance | Out-Null
foreach($entry in $entries){Copy-Item -LiteralPath $entry.FullName -Destination $instance -Recurse}
& (Join-Path $PSScriptRoot 'Build-MreDecaySafeTestMap.ps1') -Collection $Collection -MapSource $MapSource -WorkDirectory (Join-Path $work 'map-stage')
New-Item -ItemType Directory -Path "$instance\Maps","$instance\Servers\MREMapPackage\Server","$instance\Servers\MREMapPackage\Rocket\Plugins" -Force | Out-Null
Copy-Item -Recurse -LiteralPath "$work\map-stage\MREDecayTest" -Destination "$instance\Maps"
$managed=Join-Path $instance 'Unturned_Data\Managed'; $rocket=Join-Path $instance 'Extras\Rocket.Unturned'
$refs=@('mscorlib','System','System.Core','netstandard','System.Runtime','Assembly-CSharp','UnityEngine','UnityEngine.CoreModule','UnityEngine.PhysicsModule','com.rlabrecque.steamworks.net') | ForEach-Object {'/reference:'+(Join-Path $managed ($_.ToString()+'.dll'))}
$refs+=@('Rocket.API','Rocket.Core','Rocket.Unturned') | ForEach-Object {'/reference:'+(Join-Path $rocket ($_.ToString()+'.dll'))}
$sdk=(& dotnet --list-sdks | Where-Object {$_ -match '^8\.0\.'} | Select-Object -Last 1)
if(!$sdk -or $sdk -notmatch '^([^ ]+) \[(.+)\]$'){throw 'A .NET 8 SDK is required.'}
$csc=Join-Path $Matches[2] "$($Matches[1])\Roslyn\bincore\csc.dll"
$dll=Join-Path $instance 'Servers\MREMapPackage\Rocket\Plugins\SafeMapPackageDiagnostic.dll'
& dotnet $csc /nologo /noconfig /nostdlib+ /target:library /langversion:7.3 "/out:$dll" $refs (Join-Path $PSScriptRoot '..\ui\MreDecaySafe\checks\SafeMapPackageDiagnostic.cs')
if($LASTEXITCODE -ne 0){throw 'Diagnostic compilation failed.'}
@('Name MRE Isolated Map Test','Map MREDecayTest','Mode Easy','MaxPlayers 4',"Port $Port") | Set-Content -Encoding ASCII "$instance\Servers\MREMapPackage\Server\Commands.dat"
$stage=Join-Path $work 'map-stage\MREDecayTest'; $installed=Join-Path $instance 'Maps\MREDecayTest'
$files=@(Get-ChildItem -LiteralPath $stage -Recurse -File)
foreach($f in $files){$rel=$f.FullName.Substring($stage.Length+1);if((Get-FileHash -Algorithm SHA256 $f.FullName).Hash -ne (Get-FileHash -Algorithm SHA256 (Join-Path $installed $rel)).Hash){throw "Map copy mismatch: $rel"}}
"Verified map files: $($files.Count)" | Set-Content -Encoding ASCII "$work\installation-receipt.txt"
$old=$env:MRE_MAP_PACKAGE_TEST
try {$env:MRE_MAP_PACKAGE_TEST='YES'; $proc=Start-Process -FilePath "$instance\Unturned.exe" -WorkingDirectory $instance -ArgumentList @('-batchmode','-nographics','-NoDefaultConsole','-logfile',('"'+(Join-Path $work 'unity.log')+'"'),'+LanServer/MREMapPackage') -WindowStyle Hidden -PassThru; if(!$proc.WaitForExit($TimeoutSeconds*1000)){throw "Test process PID $($proc.Id) timed out; it remains running, stop only after verifying its path and command line."}} finally {$env:MRE_MAP_PACKAGE_TEST=$old}
$log=Join-Path $instance 'Logs\Server_MREMapPackage.log'
if(!(Test-Path $log) -or !(Select-String -LiteralPath $log -SimpleMatch 'CLIENT_PACKAGE_PASS' -Quiet) -or !(Select-String -LiteralPath $log -SimpleMatch 'Quit game: server shutdown' -Quiet) -or (Select-String -LiteralPath $log -Pattern 'CLIENT_PACKAGE_FAILED|CLIENT_PACKAGE_REFUSED' -Quiet)){throw "Diagnostic failed; see $log"}
Get-FileHash -Algorithm SHA256 $log,"$work\installation-receipt.txt","$work\map-stage\SHA256SUMS.txt",$dll | Format-List
Write-Output "PASS: isolated server shut down; artifacts: $work"
