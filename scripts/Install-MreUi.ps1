[CmdletBinding()]
param([switch]$Apply, [switch]$UpdateConfiguration, [switch]$IncludeKits)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'runtime'
$pluginsRoot = Join-Path $runtimeRoot 'server\Servers\MRE\Rocket\Plugins'
$components = @(@{ Name='MreEssentials'; Build='builds\MreEssentials' })
if ($IncludeKits) { $components += @{ Name='MreKits'; Build='builds\MreKits' } }
foreach ($component in $components) {
    $component.Source = Join-Path (Join-Path $runtimeRoot $component.Build) ($component.Name + '.dll')
    $manifestPath = Join-Path (Join-Path $runtimeRoot $component.Build) 'build-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $actualHash = (Get-FileHash -LiteralPath $component.Source -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($manifest.plugin -ne $component.Name -or $actualHash -ne $manifest.sha256) {
        throw "Не прошла проверка сборки $($component.Name)."
    }
    $component.SHA256 = $actualHash
}
& (Join-Path $PSScriptRoot 'Install-MrePlugins.ps1')
& (Join-Path $PSScriptRoot 'Install-MreHud.ps1')
if (-not $Apply) {
    Write-Host 'Комплект серверного UI проверен. Для установки корректно остановите MRE и укажите -Apply.'
    return
}

& (Join-Path $PSScriptRoot 'Install-MrePlugins.ps1') -Apply -UpdateConfiguration:$UpdateConfiguration
$maintenanceLock = $null
try {
    $maintenanceLock = [IO.File]::Open((Join-Path $runtimeRoot '.mre-maintenance.lock'),'OpenOrCreate','ReadWrite','None')
    $serverExe = [IO.Path]::GetFullPath((Join-Path $runtimeRoot 'server\Unturned.exe'))
    foreach ($serverProcess in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'")) {
        $liveProcess = Get-Process -Id $serverProcess.ProcessId -ErrorAction SilentlyContinue
        if (-not $liveProcess -or $liveProcess.HasExited) { continue }
        if (-not $serverProcess.ExecutablePath) { throw 'Нельзя проверить путь работающего Unturned.' }
        if ([IO.Path]::GetFullPath($serverProcess.ExecutablePath) -eq $serverExe) { throw 'Сначала корректно остановите MRE.' }
    }
    $backupRoot = Join-Path (Join-Path $projectRoot 'backups') ('ui-core-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    foreach ($component in $components) {
        $target = Join-Path $pluginsRoot ($component.Name + '.dll')
        if (Test-Path -LiteralPath $target) {
            Copy-Item -LiteralPath $target -Destination (Join-Path $backupRoot ($component.Name + '.dll'))
        }
        Copy-Item -LiteralPath $component.Source -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $component.SHA256) {
            throw "Установленная DLL $($component.Name) не совпадает со сборкой."
        }
    }
    $receipt = [ordered]@{
        installed_at_utc=[DateTime]::UtcNow.ToString('o'); backup=$backupRoot
        components=@($components | ForEach-Object { [ordered]@{plugin=$_.Name; sha256=$_.SHA256} })
        client_bundle='not_installed_by_this_script'; in_game_verification='pending'
    }
    [IO.File]::WriteAllText((Join-Path $runtimeRoot 'ui-install-receipt.json'), ($receipt | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
} finally { if ($maintenanceLock) { $maintenanceLock.Dispose() } }
& (Join-Path $PSScriptRoot 'Install-MreHud.ps1') -Apply -UpdateConfiguration:$UpdateConfiguration
Write-Host 'Серверные модули UI установлены с резервными копиями. Настройки MreEssentials и наборов сохранены. Клиентский bundle собирается отдельно.'
