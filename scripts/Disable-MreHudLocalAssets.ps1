[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Apply, [string]$ClientDirectory = 'D:\Steam\steamapps\common\Unturned')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$serverRoot = Join-Path $root 'runtime\server'
$serverAssets = Join-Path $serverRoot 'Bundles\MreHud'
$clientRoot = [IO.Path]::GetFullPath($ClientDirectory)
$clientAssets = Join-Path $clientRoot 'Bundles\MreHud'
$config = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins\MreHud\MreHud.configuration.xml'
$names = @('MreHud.dat', 'MreHud.unity3d')

foreach ($dir in @($serverAssets, $clientAssets)) {
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw "Нет каталога HUD: $dir" }
    $items = @(Get-ChildItem -LiteralPath $dir -Force)
    if ($items.Count -ne $names.Count -or @($items | Where-Object { $_.PSIsContainer -or $_.Name -notin $names -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }).Count -gt 0) { throw "В каталоге есть неожиданные файлы или ссылки: $dir" }
    if (((Get-Item -LiteralPath $dir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Каталог является ссылкой: $dir" }
}
foreach ($name in $names) {
    if ((Get-FileHash -LiteralPath (Join-Path $serverAssets $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $clientAssets $name)).Hash) { throw "Копии $name отличаются; автоматическое отключение остановлено." }
}
$dat = Get-Content -LiteralPath (Join-Path $serverAssets 'MreHud.dat') -Raw -Encoding UTF8
if ($dat -notmatch '(?m)^GUID 4f11731bb53e4a55b588b97c2c2f61b5\s*$') { throw 'GUID ресурса не соответствует MRE HUD.' }
if (Test-Path -LiteralPath (Join-Path $serverRoot 'Servers\MRE\Bundles\MreHud\MreHud.dat')) { throw 'Найдена вторая серверная копия HUD; проверьте её вручную.' }
[xml]$settings = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if (-not $settings.SelectSingleNode('/MreHudConfiguration/AssetsVerified') -or -not $settings.SelectSingleNode('/MreHudConfiguration/HideNativeHud')) { throw 'Не найдены настройки безопасного отключения HUD.' }
if (-not $Apply) {
    [pscustomobject]@{Result='Preview';Client=$clientAssets;Server=$serverAssets;Config=$config;NativeHud='will_be_enabled';Requires='Закрыть клиент и остановить сервер';Apply=$false}
    return
}
if (-not $PSCmdlet.ShouldProcess($clientAssets, 'Сохранить копию HUD и убрать ресурс из общих каталогов клиента и сервера')) { return }

$lock = $null
try {
    $lock = [IO.File]::Open((Join-Path $root 'runtime\.mre-maintenance.lock'),'OpenOrCreate','ReadWrite','None')
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $image = Get-MreProcessPath $p
        if (-not $image) { throw 'Не удалось определить путь работающего Unturned.' }
        $full = [IO.Path]::GetFullPath($image)
        if ($full.StartsWith(([IO.Path]::GetFullPath($serverRoot).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase) -or
            $full.StartsWith(($clientRoot.TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Сначала закройте Unturned и корректно остановите MRE.' }
    }
    $backup = Join-Path $root ('backups\hud-client-compat-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
    foreach ($sub in @('client','server')) { New-Item -ItemType Directory -Path (Join-Path $backup $sub) -Force | Out-Null }
    Copy-Item -LiteralPath $config -Destination (Join-Path $backup 'MreHud.configuration.xml')
    foreach ($name in $names) {
        foreach ($pair in @(@($clientAssets,'client'),@($serverAssets,'server'))) {
            $file = Join-Path $pair[0] $name
            Copy-Item -LiteralPath $file -Destination (Join-Path (Join-Path $backup $pair[1]) $name)
        }
    }
    foreach ($name in $names) {
        if ((Get-FileHash -LiteralPath (Join-Path $clientAssets $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $serverAssets $name)).Hash) { throw 'Файлы изменились после проверки.' }
    }
    try {
        $settings.SelectSingleNode('/MreHudConfiguration/AssetsVerified').InnerText = 'false'
        $settings.SelectSingleNode('/MreHudConfiguration/HideNativeHud').InnerText = 'false'
        $temp = $config + '.mre-compat.tmp'
        if (Test-Path -LiteralPath $temp) { throw 'Обнаружен файл незавершённой операции.' }
        $settings.Save($temp)
        [IO.File]::Replace($temp, $config, (Join-Path $backup 'MreHud.configuration.before-replace.xml'))
        foreach ($name in $names) {
            Remove-Item -LiteralPath (Join-Path $clientAssets $name)
            Remove-Item -LiteralPath (Join-Path $serverAssets $name)
        }
    } catch {
        foreach ($name in $names) {
            foreach ($pair in @(@($clientAssets,'client'),@($serverAssets,'server'))) {
                $file = Join-Path $pair[0] $name
                if (-not (Test-Path -LiteralPath $file)) { Copy-Item -LiteralPath (Join-Path (Join-Path $backup $pair[1]) $name) -Destination $file }
            }
        }
        Copy-Item -LiteralPath (Join-Path $backup 'MreHud.configuration.xml') -Destination $config -Force
        throw
    }
    [pscustomobject]@{Result='Disabled';Backup=$backup;NativeHud='enabled';RestartClient=$true;RestartServer=$true}
} finally { if ($lock) { $lock.Dispose() } }
