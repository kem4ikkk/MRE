[CmdletBinding()]
param([string]$UnityEditorPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityProject = Join-Path $projectRoot 'ui\MreHud'
$versionFile = Join-Path $unityProject 'ProjectSettings\ProjectVersion.txt'
$versionLine = Get-Content -LiteralPath $versionFile -Encoding UTF8 | Where-Object { $_ -match '^m_EditorVersion: ' }
if ($versionLine -notmatch '^m_EditorVersion: (\S+)$') { throw 'Не удалось прочитать версию Unity проекта.' }
$requiredVersion = $Matches[1]
if (-not $UnityEditorPath) {
    $candidates = @(
        (Join-Path $projectRoot "runtime\tools\Unity\$requiredVersion\Editor\Unity.exe"),
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$requiredVersion\Editor\Unity.exe")
    )
    $unityCommand = Get-Command Unity.exe -ErrorAction SilentlyContinue
    if ($unityCommand) { $candidates += $unityCommand.Source }
    $UnityEditorPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $UnityEditorPath -or -not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Нужен установленный и активированный Unity Editor $requiredVersion. Передайте -UnityEditorPath '<путь к Unity.exe>'. DLL сервера не заменяют Editor; bundle не создан."
}
$UnityEditorPath = (Resolve-Path -LiteralPath $UnityEditorPath).Path
if ([IO.Path]::GetFileName($UnityEditorPath) -ne 'Unity.exe') { throw 'Укажите именно Unity.exe редактора.' }
$outputRoot = Join-Path $projectRoot 'runtime\ui-builds\MreHud\Windows'
$manifestPath = Join-Path $outputRoot 'bundle-build.json'
$bundlePath = Join-Path $outputRoot 'MreHud\MreHud.unity3d'
$datPath = Join-Path $outputRoot 'MreHud\MreHud.dat'
$logRoot = Join-Path $projectRoot 'runtime\ui-builds'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$buildLock = $null
try {
    $buildLock = [IO.File]::Open((Join-Path $logRoot '.mrehud-build.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $buildStarted = [DateTime]::UtcNow
    $logPath = Join-Path $logRoot ('MreHud-unity-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
    $unityArguments = @('-batchmode', '-quit', '-projectPath', ('"' + $unityProject + '"'),
        '-executeMethod', 'MreHudBuilder.BuildWindows', '-logFile', ('"' + $logPath + '"'))
    Write-Host "Unity ${requiredVersion}: экспорт HUD. Журнал: $logPath"
    $unityProcess = Start-Process -FilePath $UnityEditorPath -ArgumentList $unityArguments -PassThru -WindowStyle Hidden
    $unityProcess.WaitForExit()
    if ($unityProcess.ExitCode -ne 0) { throw "Unity завершился с кодом $($unityProcess.ExitCode). См. $logPath" }
    foreach ($resultPath in @($manifestPath, $bundlePath, $datPath)) {
        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw "Unity не создал результат: $resultPath. См. $logPath" }
        if ((Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $buildStarted.AddSeconds(-2)) {
            throw "Обнаружен устаревший результат предыдущей сборки: $resultPath"
        }
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.unity_version -ne $requiredVersion -or $manifest.target -ne 'StandaloneWindows64') {
        throw 'Версия Unity или платформа bundle не соответствует проекту.'
    }
    if ($manifest.effect_id -ne 48621 -or $manifest.effect_guid -ne '4f11731bb53e4a55b588b97c2c2f61b5' -or
        $manifest.prefab_address_verified -ne $true -or $manifest.in_game_verified -ne $false) {
        throw 'Manifest не подтверждает ожидаемый контракт Effect; игровая приёмка должна оставаться отдельной.'
    }
    $actualHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $manifest.bundle_sha256) { throw 'SHA256 bundle не совпадает с manifest.' }
    $stream = [IO.File]::OpenRead($bundlePath)
    try {
        $signature = New-Object byte[] 8
        $readCount = $stream.Read($signature, 0, $signature.Length)
        if ($readCount -ne 8 -or [Text.Encoding]::ASCII.GetString($signature) -ne "UnityFS`0") {
            throw 'Результат не является UnityFS AssetBundle.'
        }
    }
    finally { $stream.Dispose() }
    $dat = Get-Content -LiteralPath $datPath -Raw -Encoding UTF8
    foreach ($expectedLine in @('GUID 4f11731bb53e4a55b588b97c2c2f61b5', 'Type Effect', 'ID 48621', 'Asset_Bundle_Version 6', 'Exclude_From_Master_Bundle')) {
        if ($dat -notmatch ('(?m)^' + [regex]::Escape($expectedLine) + '\r?$')) { throw "Неверный .dat: отсутствует $expectedLine" }
    }
    if ($dat -match '(?m)^\s*Master_Bundle_Override(?:\s|$)') { throw 'Неверный .dat: Master_Bundle_Override имеет приоритет над Exclude_From_Master_Bundle.' }
    [pscustomobject]@{ Bundle=$bundlePath; SHA256=$actualHash; Unity=$manifest.unity_version; Log=$logPath; InGameVerified=$false }
    Write-Host 'Bundle собран и проверен. Следующий шаг: доставка серверу и клиентам, затем игровая приёмка.'
}
finally { if ($buildLock) { $buildLock.Dispose() } }
