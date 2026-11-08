[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$stageRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'runtime\trade-stage\server'))
$stageExe = Join-Path $stageRoot 'Unturned.exe'
$instanceRoot = Join-Path $stageRoot 'Servers\MRE'
$pluginRoot = Join-Path $instanceRoot 'Rocket\Plugins\MreTrade'
$dllTarget = Join-Path $pluginRoot 'MreTrade.dll'
$configTarget = Join-Path $pluginRoot 'MreTrade.configuration.xml'
$workshopTarget = Join-Path $instanceRoot 'WorkshopDownloadConfig.json'
$workshopCache = Join-Path $instanceRoot 'Workshop\Steam\content\304930\3834982706'
$dllSource = Join-Path $projectRoot 'runtime\builds\MreTrade\MreTrade.dll'
$manifestPath = Join-Path $projectRoot 'runtime\builds\MreTrade\build-manifest.json'
$packageRoot = Join-Path $projectRoot 'runtime\workshop-packages\MreTrade-b142af4389594d07b087245db9f9a41a'
$publishedManifest = Join-Path $packageRoot 'package.json'
$packageContent = Join-Path $packageRoot 'Content'
$downloadedPayload = Join-Path $projectRoot 'runtime\steamcmd\steamapps\workshop\content\304930\3834982706'
$expectedWorkshopId = 3834982706L
$expectedStageExe = [IO.Path]::GetFullPath($stageExe)

if (-not (Test-Path -LiteralPath $stageExe -PathType Leaf)) { throw "Не найден stage-сервер: $stageExe" }
if (-not (Test-Path -LiteralPath $dllSource -PathType Leaf)) { throw "Сначала соберите MreTrade: $dllSource" }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Нет манифеста сборки: $manifestPath" }
if (-not (Test-Path -LiteralPath $workshopTarget -PathType Leaf)) { throw "Не найдена конфигурация Workshop: $workshopTarget" }
if (-not (Test-Path -LiteralPath $packageContent -PathType Container)) { throw "Не найден опубликованный пакет: $packageContent" }
if (-not (Test-Path -LiteralPath $downloadedPayload -PathType Container)) { throw "Не найден проверенный SteamCMD payload: $downloadedPayload" }

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$dllHash = (Get-FileHash -LiteralPath $dllSource -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.plugin -ne 'MreTrade' -or $manifest.sha256 -ne $dllHash) { throw 'Хеш DLL не совпадает с манифестом сборки.' }
$stageAssemblyHash = (Get-FileHash -LiteralPath (Join-Path $stageRoot 'Unturned_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
$buildAssemblyReference = @($manifest.references | Where-Object name -eq 'Assembly-CSharp.dll' | Select-Object -ExpandProperty sha256 -First 1)
if (-not $buildAssemblyReference -or $buildAssemblyReference.ToLowerInvariant() -ne $stageAssemblyHash) {
    throw 'Сборка MreTrade рассчитана не на этот stage U3DS. Соберите её командой Build-MreTrade.ps1 -GameDirectory runtime/trade-stage/server.'
}
$workshopManifest = Get-Content -LiteralPath $publishedManifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ([long]$workshopManifest.workshop_file_id -ne $expectedWorkshopId -or
    $workshopManifest.confirmation_protocol -ne 'nonce-input-v1' -or
    $workshopManifest.bundle_sha256 -ne '873d3c07ba57b646787930c6283b50078fbccd508b2ce0d6997662eb58848b22' -or
    -not $workshopManifest.published -or -not $workshopManifest.downloaded_payload_sha256_match) {
    throw 'Опубликованная UI-сборка Trade не прошла проверку receipt.'
}
$relativePayloadFiles = @(Get-ChildItem -LiteralPath $packageContent -File -Recurse | ForEach-Object {
    $_.FullName.Substring($packageContent.Length).TrimStart('\','/')
}) | Sort-Object
$relativeDownloadedFiles = @(Get-ChildItem -LiteralPath $downloadedPayload -File -Recurse | ForEach-Object {
    $_.FullName.Substring($downloadedPayload.Length).TrimStart('\','/')
}) | Sort-Object
if (($relativePayloadFiles -join "`n") -ne ($relativeDownloadedFiles -join "`n")) { throw 'SteamCMD payload не совпадает по списку файлов с опубликованным пакетом.' }
foreach ($relative in $relativePayloadFiles) {
    $packageFile = Join-Path $packageContent $relative
    $downloadedFile = Join-Path $downloadedPayload $relative
    if ((Get-FileHash -LiteralPath $packageFile -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $downloadedFile -Algorithm SHA256).Hash) {
        throw "SteamCMD payload не совпадает с Workshop-пакетом: $relative"
    }
}

$runningStage = @()
foreach ($process in @(Get-Process -Name Unturned -ErrorAction SilentlyContinue)) {
    $imagePath = $null
    try { $imagePath = $process.Path } catch { }
    if (-not $imagePath) {
        try {
            $imagePath = (Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $process.Id) -ErrorAction Stop).ExecutablePath
        } catch { }
    }
    if (-not $imagePath) { throw "Не удалось проверить путь процесса Unturned PID $($process.Id); установка остановлена." }
    if ([IO.Path]::GetFullPath($imagePath).Equals($expectedStageExe, [StringComparison]::OrdinalIgnoreCase)) {
        $runningStage += $process.Id
    }
}
if ($runningStage.Count -gt 0) { throw "Тестовый сервер всё ещё работает (PID $($runningStage -join ', ')). Выполните Save и Shutdown в его консоли." }

$workshopConfig = Get-Content -LiteralPath $workshopTarget -Raw -Encoding UTF8 | ConvertFrom-Json
$ids = @()
if ($workshopConfig.PSObject.Properties.Name -contains 'File_IDs') { $ids = @($workshopConfig.File_IDs | ForEach-Object { [long]$_ }) }
if ($expectedWorkshopId -notin $ids) { throw "Workshop ID $expectedWorkshopId отсутствует в stage-конфигурации; сначала добавьте его вручную после резервной копии." }

$configText = @'
<?xml version="1.0" encoding="utf-8"?>
<MreTradeConfiguration xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <ExperimentalTradeEnabled>false</ExperimentalTradeEnabled>
  <UiEnabled>false</UiEnabled>
  <AssetsVerified>false</AssetsVerified>
</MreTradeConfiguration>
'@
$backupRoot = Join-Path $projectRoot ('runtime\trade-stage\backups\MreTrade-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$report = [pscustomobject]@{
    Stage = $expectedStageExe
    WorkshopIdPresent = $expectedWorkshopId -in $ids
    PluginSha256 = $dllHash
    WorkshopBundleSha256 = $workshopManifest.bundle_sha256
    WorkshopPayloadFilesVerified = $relativePayloadFiles.Count
    FeatureFlags = 'false / false / false'
    BackupDirectory = $backupRoot
}
$report | Format-List
if (-not $Apply) { Write-Host 'Предпросмотр: файлы не изменены. Для установки добавьте -Apply.'; return }
if (-not $PSCmdlet.ShouldProcess($pluginRoot, 'Обновить MreTrade на изолированном stage-сервере')) { return }

New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
foreach ($target in @($dllTarget, $configTarget, $workshopTarget)) {
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        $destination = Join-Path $backupRoot ([IO.Path]::GetFileName($target))
        Copy-Item -LiteralPath $target -Destination $destination
    }
}
if (Test-Path -LiteralPath $workshopCache -PathType Container) {
    Copy-Item -LiteralPath $workshopCache -Destination (Join-Path $backupRoot 'WorkshopCache') -Recurse
}
$temporaryDll = $dllTarget + '.mre-install.tmp'
Copy-Item -LiteralPath $dllSource -Destination $temporaryDll -Force
if ((Get-FileHash -LiteralPath $temporaryDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dllHash) {
    Remove-Item -LiteralPath $temporaryDll -Force -ErrorAction SilentlyContinue
    throw 'Хеш установленной копии DLL изменился; целевой файл не заменён.'
}
if (Test-Path -LiteralPath $dllTarget) { [IO.File]::Replace($temporaryDll, $dllTarget, (Join-Path $backupRoot 'MreTrade.dll.previous')) }
else { [IO.File]::Move($temporaryDll, $dllTarget) }
[IO.File]::WriteAllText($configTarget, $configText + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
$temporaryCache = $workshopCache + '.mre-install.tmp'
if (Test-Path -LiteralPath $temporaryCache) { Remove-Item -LiteralPath $temporaryCache -Recurse -Force }
Copy-Item -LiteralPath $downloadedPayload -Destination $temporaryCache -Recurse
if (Test-Path -LiteralPath $workshopCache) { Remove-Item -LiteralPath $workshopCache -Recurse -Force }
Move-Item -LiteralPath $temporaryCache -Destination $workshopCache

$installedHash = (Get-FileHash -LiteralPath $dllTarget -Algorithm SHA256).Hash.ToLowerInvariant()
$installedConfig = [xml](Get-Content -LiteralPath $configTarget -Raw -Encoding UTF8)
if ($installedHash -ne $dllHash -or
    $installedConfig.MreTradeConfiguration.ExperimentalTradeEnabled -ne 'false' -or
    $installedConfig.MreTradeConfiguration.UiEnabled -ne 'false' -or
    $installedConfig.MreTradeConfiguration.AssetsVerified -ne 'false' -or
    (Get-FileHash -LiteralPath (Join-Path $workshopCache 'Bundles\MreTrade\MreTrade.unity3d') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $workshopManifest.bundle_sha256) {
    throw "Постпроверка установки не пройдена. Резервная копия: $backupRoot"
}
Write-Host "MreTrade установлен на остановленный stage-сервер. Обмен и UI оставлены выключенными. Резервная копия: $backupRoot"
