[CmdletBinding()]
param(
    [string]$UnityEditorPath,
    [switch]$RenderPreview
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityProject = Join-Path $projectRoot 'ui\MreHelp'
$requiredVersion = '2022.3.62f3'
$versionPath = Join-Path $unityProject 'ProjectSettings\ProjectVersion.txt'
$versionLine = Get-Content -LiteralPath $versionPath -Encoding UTF8 | Where-Object { $_ -match '^m_EditorVersion: ' }
if ($versionLine -notmatch '^m_EditorVersion: (\S+)$' -or $Matches[1] -ne $requiredVersion) { throw "Проект MreHelp должен использовать Unity $requiredVersion." }

if (-not $UnityEditorPath) {
    $candidates = @(
        (Join-Path $projectRoot "runtime\tools\Unity\$requiredVersion\Editor\Unity.exe"),
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$requiredVersion\Editor\Unity.exe")
    )
    $command = Get-Command Unity.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    $UnityEditorPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $UnityEditorPath -or -not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) { throw "Нужен Unity Editor $requiredVersion; передайте -UnityEditorPath." }
$UnityEditorPath = (Resolve-Path -LiteralPath $UnityEditorPath).Path
if ([IO.Path]::GetFileName($UnityEditorPath) -ne 'Unity.exe') { throw 'Укажите именно Unity.exe.' }

$output = Join-Path $unityProject 'Build\Windows'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$log = Join-Path $unityProject ('Build\MreHelp-unity-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
$started = [DateTime]::UtcNow
$method = if ($RenderPreview) { 'MreHelpBuilder.RenderPreview' } else { 'MreHelpBuilder.BuildWindows' }
$args = @('-batchmode','-quit','-projectPath',('"' + $unityProject + '"'),'-executeMethod',$method,'-logFile',('"' + $log + '"'))
Write-Host "Unity $requiredVersion; метод $method; журнал $log"
$process = Start-Process -FilePath $UnityEditorPath -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Unity завершился с кодом $($process.ExitCode). См. $log" }

if ($RenderPreview) {
    $preview = Join-Path $unityProject 'Build\MreHelp-preview.png'
    if (-not (Test-Path -LiteralPath $preview -PathType Leaf) -or (Get-Item -LiteralPath $preview).LastWriteTimeUtc -lt $started.AddSeconds(-2)) { throw "Unity не создал свежий предпросмотр: $preview" }
    Write-Output $preview
    return
}

$manifestPath = Join-Path $output 'effect-manifest.json'
$bundlePath = Join-Path $output 'MreHelp\MreHelp.unity3d'
$datPath = Join-Path $output 'MreHelp\MreHelp.dat'
foreach ($path in @($manifestPath,$bundlePath,$datPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).LastWriteTimeUtc -lt $started.AddSeconds(-2)) { throw "Не создан свежий результат: $path" }
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.effect_id -ne 48625 -or $manifest.effect_guid -ne '6e4a31b49c874f428a622e1a68e4d203' -or $manifest.effect_key -ne 14825 -or $manifest.unity_version -ne $requiredVersion -or $manifest.prefab_address -ne 'Effect' -or $manifest.prefab_address_verified -ne $true -or $manifest.in_game_verified -ne $false) { throw 'Манифест не соответствует контракту MRE Help.' }
$hash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $manifest.bundle_sha256) { throw 'Хэш bundle не совпадает с манифестом.' }
$stream = [IO.File]::OpenRead($bundlePath)
try { $sig = New-Object byte[] 8; if ($stream.Read($sig,0,8) -ne 8 -or [Text.Encoding]::ASCII.GetString($sig) -ne "UnityFS`0") { throw 'Файл не является UnityFS AssetBundle.' } }
finally { $stream.Dispose() }
$dat = Get-Content -LiteralPath $datPath -Raw -Encoding UTF8
foreach ($line in @('GUID 6e4a31b49c874f428a622e1a68e4d203','Type Effect','ID 48625','Asset_Bundle_Version 6','Exclude_From_Master_Bundle')) { if ($dat -notmatch ('(?m)^' + [regex]::Escape($line) + '\r?$')) { throw "MreHelp.dat: отсутствует $line" } }
if ($dat -match '(?m)^\s*Master_Bundle_Override(?:\s|$)') { throw 'Master_Bundle_Override запрещён для этого изолированного эффекта.' }
[pscustomobject]@{ Bundle=$bundlePath; SHA256=$hash; Unity=$manifest.unity_version; Log=$log; InGameVerified=$false; WorkshopPublished=$false }
