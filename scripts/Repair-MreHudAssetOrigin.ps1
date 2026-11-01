[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Apply, [switch]$AllowBuiltinTestOnly, [string]$ClientDirectory = 'D:\Steam\steamapps\common\Unturned')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$serverRoot = Join-Path $root 'runtime\server'
$source = Join-Path $serverRoot 'Servers\MRE\Bundles\MreHud'
$target = Join-Path $serverRoot 'Bundles\MreHud'
$client = Join-Path ([IO.Path]::GetFullPath($ClientDirectory)) 'Bundles\MreHud'
$names = @('MreHud.dat', 'MreHud.unity3d')

foreach ($name in $names) {
    $serverFile = Join-Path $source $name
    $clientFile = Join-Path $client $name
    if (-not (Test-Path -LiteralPath $serverFile -PathType Leaf) -or -not (Test-Path -LiteralPath $clientFile -PathType Leaf)) { throw "Отсутствует серверный или клиентский файл $name." }
    if ((Get-FileHash -LiteralPath $serverFile -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $clientFile -Algorithm SHA256).Hash) { throw "Серверный и клиентский $name различаются: автоматическое переключение запрещено." }
}
if (Test-Path -LiteralPath $target) { throw "Каталог назначения уже существует: $target. Требуется ручная проверка, перезапись запрещена." }
$actual = @(Get-ChildItem -LiteralPath $source -Force)
if ($actual.Count -ne $names.Count -or @($actual | Where-Object { $_.PSIsContainer -or $_.Name -notin $names -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }).Count -gt 0) { throw 'В старом каталоге есть неожиданные файлы, подпапки или ссылки.' }
foreach ($path in @($serverRoot, (Join-Path $serverRoot 'Bundles'), (Join-Path $serverRoot 'Servers'), (Join-Path $serverRoot 'Servers\MRE'), (Join-Path $serverRoot 'Servers\MRE\Bundles'), $source)) {
    if (((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Каталог не должен быть ссылкой: $path" }
}
$dat = Get-Content -LiteralPath (Join-Path $source 'MreHud.dat') -Raw -Encoding UTF8
if ($dat -notmatch '(?m)^GUID 4f11731bb53e4a55b588b97c2c2f61b5\s*$' -or $dat -notmatch '(?m)^Exclude_From_Master_Bundle\s*$') { throw 'Проверка GUID или режима legacy bundle не пройдена.' }

if (-not $Apply) {
    [pscustomobject]@{ Result='Preview'; Source=$source; Target=$target; Client=$client; Files=$names; Apply=$false; Warning='Встроенный клиентский HUD мешает входу на другие серверы. Предпочтителен Workshop.' }
    return
}
if (-not $AllowBuiltinTestOnly) { throw 'Эта локальная схема несовместима с другими серверами. Используйте Workshop; тестовый режим требует явного -AllowBuiltinTestOnly.' }
if (-not $PSCmdlet.ShouldProcess($target, 'Перенести ресурс HUD в источник Vanilla Built-in и сохранить старую копию')) { return }

$lock = $null
try {
    $lock = [IO.File]::Open((Join-Path $root 'runtime\.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $prefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $image = Get-MreProcessPath $p
        if (-not $image) { throw 'Не удалось проверить путь одного из процессов Unturned.' }
        if ([IO.Path]::GetFullPath($image).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Сначала корректно остановите сервер MRE.' }
    }
    if (Test-Path -LiteralPath $target) { throw 'Каталог назначения появился после предварительной проверки.' }
    $backup = Join-Path $root ('backups\hud-origin-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    foreach ($name in $names) {
        $from = Join-Path $source $name
        if ((Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $client $name) -Algorithm SHA256).Hash) { throw 'Файлы изменились во время проверки.' }
        Copy-Item -LiteralPath $from -Destination (Join-Path $backup $name)
    }
    New-Item -ItemType Directory -Path $target | Out-Null
    try {
        foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $target $name) }
        foreach ($name in $names) {
            if ((Get-FileHash -LiteralPath (Join-Path $target $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup $name)).Hash) { throw 'Не совпадает SHA256 новой копии.' }
        }
        foreach ($name in $names) { Move-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $backup ($name + '.previous')) }
    } catch {
        # Восстанавливаем только известные файлы и удаляем только новые файлы назначения.
        foreach ($name in $names) {
            $old = Join-Path $source $name
            if (-not (Test-Path -LiteralPath $old)) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination $old }
            $new = Join-Path $target $name
            if (Test-Path -LiteralPath $new) { Remove-Item -LiteralPath $new }
        }
        throw
    }
    [pscustomobject]@{ Result='Installed'; Target=$target; Backup=$backup; ClientUnchanged=$true; InGameVerified=$false }
} finally { if ($lock) { $lock.Dispose() } }
