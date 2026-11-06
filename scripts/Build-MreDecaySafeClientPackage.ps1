param(
    [string]$Unity = (Join-Path $PSScriptRoot '..\runtime\tools\Unity\2022.3.62f3\Editor\Unity.exe'),
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-decay-client-package-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = [IO.Path]::GetFullPath($WorkDirectory)
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (-not $work.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $work)) { throw 'WorkDirectory must be a new directory inside TEMP.' }
New-Item -ItemType Directory -Path $work | Out-Null
$safeWork = Join-Path $work 'safe-build'
& (Join-Path $PSScriptRoot 'Build-MreDecaySafePackage.ps1') -Unity $Unity -WorkDirectory $safeWork
$hudProject = Join-Path $work 'hud-project'
$hudOutput = Join-Path $work 'hud-build'
New-Item -ItemType Directory -Path $hudProject,$hudOutput | Out-Null
foreach ($name in @('Assets','Packages','ProjectSettings')) { Copy-Item -LiteralPath (Join-Path $repo "ui\MreDecayZoneHud\$name") -Destination $hudProject -Recurse }
$old = $env:MRE_DECAY_HUD_OUTPUT
try {
    $env:MRE_DECAY_HUD_OUTPUT = $hudOutput
    $process = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-batchmode','-quit','-projectPath',('"'+$hudProject+'"'),'-executeMethod','MreDecayZoneHudBuilder.BuildVisualBundle','-logFile',('"'+(Join-Path $work 'hud-unity.log')+'"'))
} finally { $env:MRE_DECAY_HUD_OUTPUT = $old }
if ($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath (Join-Path $work 'hud-unity.log') -SimpleMatch 'MRE_DECAY_ZONE_BUNDLE_OK' -Quiet)) { throw 'HUD Unity build failed.' }
$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $hudOutput 'bundle-build.json') | ConvertFrom-Json
$hudBundle = Join-Path $hudOutput 'mre-decay-zone-visual.unity3d'
if ($manifest.status -ne 'TEST_ONLY_NOT_INSTALLED' -or $manifest.effect_id -ne 48623 -or $manifest.effect_guid -ne '5d4628c25c73441a89ef5aed4db47713' -or $manifest.effect_key -ne 14823 -or $manifest.unity_version -ne '2022.3.62f3' -or $manifest.bundle_sha256 -ne (Get-FileHash -Algorithm SHA256 $hudBundle).Hash.ToLowerInvariant()) { throw 'HUD manifest mismatch.' }
$expected = "GUID 5d4628c25c73441a89ef5aed4db47713`nType Effect`nID 48623`nAsset_Bundle_Version 6`nExclude_From_Master_Bundle`n"
if ([IO.File]::ReadAllText((Join-Path $hudOutput 'mre-decay-zone-visual.dat')).Replace("`r`n","`n") -cne $expected) { throw 'HUD DAT mismatch.' }
$collection = Join-Path $work 'collection'
Copy-Item -Recurse -LiteralPath (Join-Path $safeWork 'collection') -Destination $collection
$effect = Join-Path $collection 'Effects\mre-decay-zone-visual'
New-Item -ItemType Directory -Path $effect -Force | Out-Null
Copy-Item -LiteralPath $hudBundle,(Join-Path $hudOutput 'mre-decay-zone-visual.dat') -Destination $effect
$files = @(Get-ChildItem -File -Recurse -LiteralPath $collection)
if ($files.Count -ne 11) { throw 'Unexpected collection file count.' }
$files | Sort-Object FullName | ForEach-Object { $h=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; '{0}  {1}' -f $h.Hash.ToLowerInvariant(), $_.FullName.Substring($collection.Length+1).Replace('\','/') } | Set-Content -Encoding ASCII (Join-Path $work 'SHA256SUMS.txt')
Write-Output "TEST ONLY collection: $collection"
Write-Output "Manifest: $work\SHA256SUMS.txt"
