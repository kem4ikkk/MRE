param(
    [string]$Unity = (Join-Path $PSScriptRoot '..\runtime\tools\Unity\2022.3.62f3\Editor\Unity.exe'),
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-safe-build-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = [System.IO.Path]::GetFullPath($WorkDirectory)
$tempRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $work.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'WorkDirectory must be inside TEMP.' }
if (Test-Path -LiteralPath $work) { throw 'WorkDirectory must not already exist.' }
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw 'Unity executable not found.' }
New-Item -ItemType Directory -Path $work | Out-Null
$project = Join-Path $work 'project'
$output = Join-Path $work 'bundles'
New-Item -ItemType Directory -Path $project,$output | Out-Null
$source = Join-Path $repo 'ui\MreDecaySafe'
foreach ($dir in @('Assets','Packages','ProjectSettings')) { Copy-Item -LiteralPath (Join-Path $source $dir) -Destination $project -Recurse }
$env:MRE_SAFE_VISUAL_OUTPUT = $output
$log = Join-Path $work 'unity.log'
$argsList = @('-batchmode','-nographics','-quit','-projectPath',('"' + $project + '"'),'-executeMethod','MreDecaySafeBuilder.BuildExperimentalStorage','-logFile',('"' + $log + '"'))
try { $process = Start-Process -FilePath $Unity -ArgumentList $argsList -PassThru -Wait -WindowStyle Hidden } finally { Remove-Item Env:MRE_SAFE_VISUAL_OUTPUT -ErrorAction SilentlyContinue }
if ($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch 'MRE_SAFE_EXPERIMENTAL_OK' -Quiet)) { throw "Unity build failed; see $log" }
foreach ($cells in @(100,200,300)) { Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output "safe$cells.unity3d") }
Write-Output "Isolated build: $work"
