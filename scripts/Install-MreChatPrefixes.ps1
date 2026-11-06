[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$serverRoot = Join-Path $projectRoot 'runtime\server'
$serverExe = [IO.Path]::GetFullPath((Join-Path $serverRoot 'Unturned.exe'))
$pluginRoot = Join-Path $serverRoot 'Servers\MRE\Rocket\Plugins'
foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe'")) {
    $imagePath = Get-MreProcessPath $process
    if (-not $imagePath) { throw 'Не удалось определить путь процесса Unturned.' }
    if ([IO.Path]::GetFullPath($imagePath) -eq $serverExe) { throw 'Сначала корректно остановите сервер MRE.' }
}

$names = @('MreChatStyle','MreInterface','MreFlight','MreInventoryAudit','MreModeration','MreAirdrop')
$entries = @($names | ForEach-Object {
    [pscustomobject]@{ Source = (Join-Path $projectRoot "runtime\builds\$_\$_.dll"); Target = (Join-Path $pluginRoot "$_.dll"); Backup = "$_.dll" }
})
$entries += [pscustomobject]@{ Source = (Join-Path $projectRoot 'runtime\plugin-builds\Teleportation.MRE\Teleportation.dll'); Target = (Join-Path $pluginRoot 'Teleportation.dll'); Backup = 'Teleportation.dll' }
$rocket = Join-Path $serverRoot 'Servers\MRE\Rocket\Rocket.Unturned.en.translation.xml'
$tpa = Join-Path $pluginRoot 'Teleportation\Teleportation.en.translation.xml'
foreach ($entry in $entries) {
    if (-not (Test-Path -LiteralPath $entry.Source -PathType Leaf) -or -not (Test-Path -LiteralPath $entry.Target -PathType Leaf)) { throw "Не найдена сборка или установленный плагин: $($entry.Source), $($entry.Target)" }
}
foreach ($file in @($rocket,$tpa)) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Не найден перевод: $file" } }

$backup = Join-Path $projectRoot ('runtime\backups\chat-prefix-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($entry in $entries) { Copy-Item -LiteralPath $entry.Target -Destination (Join-Path $backup $entry.Backup) }
Copy-Item -LiteralPath $rocket -Destination (Join-Path $backup 'Rocket.Unturned.en.translation.xml')
Copy-Item -LiteralPath $tpa -Destination (Join-Path $backup 'Teleportation.en.translation.xml')
try {
    foreach ($entry in $entries) {
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Target -Force
        if ((Get-FileHash -LiteralPath $entry.Source).Hash -ne (Get-FileHash -LiteralPath $entry.Target).Hash) { throw "Хеш DLL не совпадает: $($entry.Target)" }
    }
    & (Join-Path $PSScriptRoot 'Set-MreAdminTranslations.ps1') -GameDirectory $serverRoot
    $text = [IO.File]::ReadAllText($tpa)
    $text = $text.Replace('MRE · ТЕЛЕПОРТАЦИЯ', '[MRE] · ТЕЛЕПОРТАЦИЯ').Replace('[[MRE]] · ТЕЛЕПОРТАЦИЯ', '[MRE] · ТЕЛЕПОРТАЦИЯ')
    [void][xml]$text
    [IO.File]::WriteAllText($tpa, $text, [Text.UTF8Encoding]::new($false))
    [void][xml]([IO.File]::ReadAllText($rocket))
} catch {
    foreach ($entry in $entries) { Copy-Item -LiteralPath (Join-Path $backup $entry.Backup) -Destination $entry.Target -Force }
    Copy-Item -LiteralPath (Join-Path $backup 'Rocket.Unturned.en.translation.xml') -Destination $rocket -Force
    Copy-Item -LiteralPath (Join-Path $backup 'Teleportation.en.translation.xml') -Destination $tpa -Force
    throw
}
Write-Host "Плагины и переводы установлены. Копия для отката: $backup"
