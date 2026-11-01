[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string]$ExpectedPluginSha256,
    [switch]$Apply,
    [switch]$EnableLocalTest
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runtime = Join-Path $root 'runtime'
$server = Join-Path $runtime 'server'
$plugins = Join-Path $server 'Servers\MRE\Rocket\Plugins'
$sourceDll = Join-Path $runtime 'plugin-builds\MreImmunity\MreImmunity.dll'
$sourceConfig = Join-Path $root 'config\plugins\MreImmunity\MreImmunity.configuration.xml'
$sourceLicense = Join-Path $root 'src\MreImmunity\LICENSE'
$targetDll = Join-Path $plugins 'MreImmunity.dll'
$targetDir = Join-Path $plugins 'MreImmunity'
$targetConfig = Join-Path $targetDir 'MreImmunity.configuration.xml'
$gameDll = Join-Path $server 'Unturned_Data\Managed\Assembly-CSharp.dll'
$harmonyDll = Join-Path $server 'Servers\MRE\Rocket\Libraries\0Harmony.dll'
$gameHash = '15666f1e20afc32a01a5510eb71eef2512a6eb4064ee628b806ee333ab4e4660'
$harmonyHash = '0de0118c8f1d4408de389ca33b46d2ff7778f3a8541b430cae729ec913d899c7'

function Assert-Hashes {
    foreach ($entry in @(
        @{ Path=$sourceDll; Hash=$ExpectedPluginSha256 },
        @{ Path=$gameDll; Hash=$gameHash },
        @{ Path=$harmonyDll; Hash=$harmonyHash }
    )) {
        if (-not (Test-Path -LiteralPath $entry.Path -PathType Leaf)) { throw "Отсутствует файл: $($entry.Path)" }
        if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ine $entry.Hash) {
            throw "SHA-256 отличается от проверенной версии: $($entry.Path)"
        }
    }
}
Assert-Hashes
if (-not (Test-Path -LiteralPath $sourceLicense -PathType Leaf)) { throw "Отсутствует лицензия: $sourceLicense" }
$config = [xml](Get-Content -LiteralPath $sourceConfig -Raw -Encoding UTF8)
if (-not $config.MreImmunityConfiguration -or $config.MreImmunityConfiguration.Enabled -ne 'false' -or
    $config.MreImmunityConfiguration.Verified -ne 'false') {
    throw 'Исходный конфиг должен содержать Enabled=false и Verified=false.'
}
if ($EnableLocalTest -and -not $Apply) { Write-Host 'Предпросмотр локального тестового включения; изменения не записаны.' }
if (-not $Apply) {
    [pscustomobject]@{ Plugin='MreImmunity'; SHA256=$ExpectedPluginSha256.ToLowerInvariant(); Target=$targetDll; LocalTest=[bool]$EnableLocalTest; Apply=$false }
    return
}
if (-not (Test-Path -LiteralPath $plugins -PathType Container)) { throw 'Каталог плагинов сервера не найден.' }
$lock = $null
try {
    $lock = [IO.File]::Open((Join-Path $runtime '.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $serverPrefix = [IO.Path]::GetFullPath($server).TrimEnd('\') + '\'
    foreach ($processInfo in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $path = Get-MreProcessPath $processInfo
        if ([string]::IsNullOrWhiteSpace($path)) { throw 'Не удалось проверить путь процесса Unturned; установка остановлена.' }
        if ([IO.Path]::GetFullPath($path).StartsWith($serverPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Сначала корректно остановите сервер MRE.'
        }
    }
    Assert-Hashes
    $backup = Join-Path (Join-Path $root 'backups') ('immunity-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    if (Test-Path -LiteralPath $targetDll) { Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backup 'MreImmunity.dll') }
    if (Test-Path -LiteralPath $targetConfig) { Copy-Item -LiteralPath $targetConfig -Destination (Join-Path $backup 'MreImmunity.configuration.xml') }
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
    if ($EnableLocalTest) {
        $config.MreImmunityConfiguration.Enabled = 'true'
        $config.MreImmunityConfiguration.Verified = 'true'
    }
    $config.Save($targetConfig)
    Copy-Item -LiteralPath $sourceLicense -Destination (Join-Path $targetDir 'LICENSE') -Force
    if ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -ine $ExpectedPluginSha256) { throw 'Проверка установленной DLL не прошла.' }
    Write-Host "MreImmunity установлен. Локальный тест: $([bool]$EnableLocalTest). Предыдущие файлы: $backup"
    Write-Host 'Реальная игровая приёмка ещё не подтверждена. Откат иммунитета в сохранениях требует отдельной резервной копии игроков.'
}
finally { if ($lock) { $lock.Dispose() } }
