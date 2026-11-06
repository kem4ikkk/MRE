param(
    [Parameter(Mandatory=$true)][string]$Collection,
    [Parameter(Mandatory=$true)][string]$MapSource,
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-decay-map-stage-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference='Stop'
$work=[IO.Path]::GetFullPath($WorkDirectory)
$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
if(-not $work.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)){throw 'WorkDirectory must be a new path inside TEMP.'}
$source=(Resolve-Path -LiteralPath $MapSource).Path
$collectionPath=(Resolve-Path -LiteralPath $Collection).Path
if(-not (Test-Path -LiteralPath (Join-Path $source 'Level.dat') -PathType Leaf)){throw 'MapSource must be a valid extracted map directory.'}
if(Test-Path -LiteralPath (Join-Path $source 'Bundles')){throw 'MapSource already contains Bundles; do not overwrite or merge.'}
$expected=@('Items\Safe100\Safe100.dat','Items\Safe100\English.dat','Items\Safe100\Safe100.unity3d','Items\Safe200\Safe200.dat','Items\Safe200\English.dat','Items\Safe200\Safe200.unity3d','Items\Safe300\Safe300.dat','Items\Safe300\English.dat','Items\Safe300\Safe300.unity3d','Effects\mre-decay-zone-visual\mre-decay-zone-visual.dat','Effects\mre-decay-zone-visual\mre-decay-zone-visual.unity3d')
$actual=@(Get-ChildItem -LiteralPath $collectionPath -Recurse -File)
if($actual.Count -ne $expected.Count){throw 'Unexpected collection file count.'}
foreach($rel in $expected){if(-not(Test-Path -LiteralPath (Join-Path $collectionPath $rel) -PathType Leaf)){throw "Missing package file: $rel"}}
New-Item -ItemType Directory -Path $work | Out-Null
$map=Join-Path $work 'MREDecayTest'
Copy-Item -LiteralPath $source -Destination $map -Recurse
Copy-Item -LiteralPath $collectionPath -Destination (Join-Path $map 'Bundles') -Recurse
foreach($rel in $expected){if((Get-FileHash -Algorithm SHA256 (Join-Path $collectionPath $rel)).Hash -ne (Get-FileHash -Algorithm SHA256 (Join-Path $map "Bundles\$rel")).Hash){throw "Copy mismatch: $rel"}}
Get-ChildItem -LiteralPath $map -Recurse -File | Sort-Object FullName | ForEach-Object {$h=Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName; '{0}  {1}' -f $h.Hash.ToLowerInvariant(),$_.FullName.Substring($map.Length+1).Replace('\','/')} | Set-Content -Encoding ASCII (Join-Path $work 'SHA256SUMS.txt')
Write-Output "Isolated staging map: $map"
Write-Output "SHA256: $work\SHA256SUMS.txt"
