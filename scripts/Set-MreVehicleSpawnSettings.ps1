[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runtime = Join-Path $root 'runtime'
$serverRoot = Join-Path $runtime 'server'
$config = Join-Path $serverRoot 'Servers\MRE\Config.txt'
$template = Join-Path $root 'config\Gameplay.txt'
$templateText = Get-Content -LiteralPath $template -Raw -Encoding UTF8
$vehicleTemplate = [regex]::Match($templateText, '(?ms)^Vehicles\s*\{(?<body>[^}]*)\}')
if (-not $vehicleTemplate.Success) { throw 'В шаблоне отсутствует секция Vehicles.' }
$settings = [ordered]@{}
foreach ($key in @('Max_Instances_Medium', 'Min_Natural_Vehicles')) {
    $match = [regex]::Match($vehicleTemplate.Groups['body'].Value, "(?m)^\s*$key\s+(\d+)\s*$")
    if (-not $match.Success) { throw "В шаблоне отсутствует $key." }
    $settings[$key] = [uint32]$match.Groups[1].Value
    if ($settings[$key] -lt 1 -or $settings[$key] -gt 128) { throw "Неверное значение $key." }
}
if ($settings.Min_Natural_Vehicles -gt $settings.Max_Instances_Medium) { throw 'Минимум транспорта превышает лимит Medium.' }
Write-Host "Параметры Vehicles: Medium=$($settings.Max_Instances_Medium), Natural=$($settings.Min_Natural_Vehicles)"
if (-not $Apply) { Write-Host 'Предпросмотр: сервер не изменён.'; return }
$lock = [IO.File]::Open((Join-Path $runtime '.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $prefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    foreach ($proc in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $path = Get-MreProcessPath $proc
        if (-not $path) { throw "Не удалось определить путь процесса $($proc.ProcessId)." }
        if ([IO.Path]::GetFullPath($path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Остановите MRE перед изменением настроек.' }
    }
    $text = Get-Content -LiteralPath $config -Raw -Encoding UTF8
    $section = [regex]::Match($text, '(?ms)^Vehicles\s*\{(?<body>[^}]*)\}')
    if (-not $section.Success) { throw 'В Config.txt отсутствует секция Vehicles.' }
    $body = $section.Groups['body'].Value
    foreach ($key in $settings.Keys) {
        $pattern = "(?m)^(?<indent>[\t ]*)$key(?:[\t ]+\d+)?[\t ]*\r?$"
        if ([regex]::Matches($body, $pattern).Count -ne 1) { throw "Не удалось однозначно найти $key в Vehicles." }
        $value = $settings[$key]
        $body = [regex]::Replace($body, $pattern, { param($m) $m.Groups['indent'].Value + $key + ' ' + $value })
    }
    $bodyGroup = $section.Groups['body']
    $updated = $text.Substring(0, $bodyGroup.Index) + $body + $text.Substring($bodyGroup.Index + $bodyGroup.Length)
    $backup = Join-Path (Join-Path $root 'backups') ('vehicle-config-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.txt')
    Copy-Item -LiteralPath $config -Destination $backup
    [IO.File]::WriteAllText($config, $updated, [Text.UTF8Encoding]::new($false))
    Write-Host "Параметры применены. Резервная копия Config.txt: $backup"
} finally { $lock.Dispose() }
