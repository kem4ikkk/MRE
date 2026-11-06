[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$editor = Join-Path $root 'runtime/tools/Unity/2022.3.62f3/Editor/Unity.exe'
$source = Join-Path $root 'ui/MreDecayZoneHud'
if (-not (Test-Path -LiteralPath $editor -PathType Leaf)) { throw 'Unity 2022.3.62f3 не найден.' }
$out = Join-Path ([IO.Path]::GetTempPath()) ('mre-decay-hud-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $out 'Project'
$bundle = Join-Path $out 'Bundle'
New-Item -ItemType Directory -Path $project, $bundle | Out-Null
foreach ($name in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $project $name) -Recurse
}
$log = Join-Path $out 'unity.log'
$oldValue = $env:MRE_DECAY_HUD_OUTPUT
try {
    $env:MRE_DECAY_HUD_OUTPUT = $bundle
    $process = Start-Process -FilePath $editor -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '-batchmode', '-quit', '-projectPath', ('"' + $project + '"'),
        '-executeMethod', 'MreDecayZoneHudBuilder.BuildVisualBundle', '-logFile', ('"' + $log + '"')
    )
} finally { $env:MRE_DECAY_HUD_OUTPUT = $oldValue }
if ($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch 'MRE_DECAY_ZONE_BUNDLE_OK' -Quiet)) {
    throw "Сборка HUD не подтверждена; журнал: $log"
}
$manifest = Get-Content -LiteralPath (Join-Path $bundle 'bundle-build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$actual = (Get-FileHash -LiteralPath (Join-Path $bundle 'mre-decay-zone-visual.unity3d') -Algorithm SHA256).Hash.ToLowerInvariant()
$dat = [IO.File]::ReadAllText((Join-Path $bundle 'mre-decay-zone-visual.dat')).Replace("`r`n", "`n")
$expectedDat = "GUID 5d4628c25c73441a89ef5aed4db47713`nType Effect`nID 48623`nAsset_Bundle_Version 6`nExclude_From_Master_Bundle`n"
if ($manifest.status -ne 'TEST_ONLY_NOT_INSTALLED' -or $manifest.bundle_sha256 -ne $actual -or
    $manifest.effect_id -ne 48623 -or $manifest.effect_key -ne 14823 -or
    $manifest.effect_guid -ne '5d4628c25c73441a89ef5aed4db47713' -or $manifest.unity_version -ne '2022.3.62f3' -or $dat -cne $expectedDat) {
    throw 'Манифест HUD не совпадает с собранным файлом.'
}
[pscustomobject]@{ Bundle = $bundle; Log = $log; SHA256 = $actual; Installed = $false }
