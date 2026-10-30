[CmdletBinding()]
param(
    [switch]$Validate
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$steamcmdRoot = Join-Path $runtimeRoot 'steamcmd'
$serverRoot = Join-Path $runtimeRoot 'server'
$downloadRoot = Join-Path $runtimeRoot 'downloads'
$steamcmdExe = Join-Path $steamcmdRoot 'steamcmd.exe'

foreach ($directory in @($steamcmdRoot, $serverRoot, $downloadRoot)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'" | Where-Object {
    $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath).StartsWith(
        [IO.Path]::GetFullPath($serverRoot) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}) {
    throw 'Сначала корректно остановите сервер MRE. Обновление работающего сервера запрещено.'
}

if (-not (Test-Path -LiteralPath $steamcmdExe)) {
    $archivePath = Join-Path $downloadRoot 'steamcmd.zip'
    $downloadUrl = 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip'
    Write-Host 'Загрузка официального SteamCMD...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
    Expand-Archive -LiteralPath $archivePath -DestinationPath $steamcmdRoot -Force
    if (-not (Test-Path -LiteralPath $steamcmdExe)) {
        throw 'Архив SteamCMD не содержит steamcmd.exe.'
    }
}

$steamArguments = @('+force_install_dir', $serverRoot, '+login', 'anonymous', '+app_info_update', '1', '+app_update', '1110390')
if ($Validate) { $steamArguments += 'validate' }
$steamArguments += '+quit'

Write-Host 'Установка Unturned Dedicated Server (1110390) в runtime/server...'
& $steamcmdExe @steamArguments
if ($LASTEXITCODE -eq 7) {
    Write-Host 'SteamCMD обновился или не успел получить конфигурацию. Повторяем один раз...'
    & $steamcmdExe @steamArguments
}
if ($LASTEXITCODE -ne 0) {
    throw "SteamCMD завершился с кодом $LASTEXITCODE. Проверьте runtime/steamcmd/logs."
}
if (-not (Test-Path -LiteralPath (Join-Path $serverRoot 'Unturned.exe'))) {
    throw 'SteamCMD не создал Unturned.exe в ожидаемом каталоге.'
}
Write-Host 'Файлы сервера установлены. Следующий шаг — настройка и проверочный запуск.'
