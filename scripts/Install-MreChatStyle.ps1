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
    if (-not $imagePath) { throw 'Could not determine the Unturned process path.' }
    if ([IO.Path]::GetFullPath($imagePath) -eq $serverExe) { throw 'Stop the MRE server before installing chat plugins.' }
}

$entries = @(
    [pscustomobject]@{ Name='MreChatStyle'; Source='runtime\builds\MreChatStyle\MreChatStyle.dll'; Target='MreChatStyle.dll'; Required=$true },
    [pscustomobject]@{ Name='MreInterface'; Source='runtime\builds\MreInterface\MreInterface.dll'; Target='MreInterface.dll'; Required=$true },
    [pscustomobject]@{ Name='MreFlight'; Source='runtime\builds\MreFlight\MreFlight.dll'; Target='MreFlight.dll'; Required=$true },
    [pscustomobject]@{ Name='MreInventoryAudit'; Source='runtime\builds\MreInventoryAudit\MreInventoryAudit.dll'; Target='MreInventoryAudit.dll'; Required=$true },
    [pscustomobject]@{ Name='MreModeration'; Source='runtime\builds\MreModeration\MreModeration.dll'; Target='MreModeration.dll'; Required=$true },
    [pscustomobject]@{ Name='MreAirdrop'; Source='runtime\builds\MreAirdrop\MreAirdrop.dll'; Target='MreAirdrop.dll'; Required=$true },
    [pscustomobject]@{ Name='MreKits'; Source='runtime\builds\MreKits\MreKits.dll'; Target='MreKits.dll'; Required=$true },
    [pscustomobject]@{ Name='MreVault'; Source='runtime\builds\MreVault\MreVault.dll'; Target='MreVault.dll'; Required=$true },
    [pscustomobject]@{ Name='MreEssentials'; Source='runtime\builds\MreEssentials\MreEssentials.dll'; Target='MreEssentials.dll'; Required=$true },
    [pscustomobject]@{ Name='MreTips'; Source='runtime\builds\MreTips\MreTips.dll'; Target='MreTips.dll'; Required=$true },
    [pscustomobject]@{ Name='MreTrade'; Source='runtime\builds\MreTrade\MreTrade.dll'; Target='MreTrade.dll'; Required=$false },
    [pscustomobject]@{ Name='Teleportation'; Source='runtime\plugin-builds\Teleportation.MRE\Teleportation.dll'; Target='Teleportation.dll'; Required=$true },
    [pscustomobject]@{ Name='MoreHomes'; Source='runtime\plugin-builds\MoreHomes.MRE\MoreHomes.dll'; Target='MoreHomes.dll'; Required=$true }
)

$active = New-Object 'System.Collections.Generic.List[object]'
$skipped = New-Object 'System.Collections.Generic.List[string]'
foreach ($entry in $entries) {
    $sourcePath = Join-Path $projectRoot $entry.Source
    $targetPath = Join-Path $pluginRoot $entry.Target
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        if ($entry.Required) { throw "Required installed plugin not found: $targetPath" }
        $skipped.Add($entry.Name)
        continue
    }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Build not found for $($entry.Name): $sourcePath" }
    $active.Add([pscustomobject]@{ Name=$entry.Name; Source=$sourcePath; Target=$targetPath; Backup=$entry.Target })
}

$rocketTranslation = Join-Path $serverRoot 'Servers\MRE\Rocket\Rocket.Unturned.en.translation.xml'
$tpaTranslation = Join-Path $pluginRoot 'Teleportation\Teleportation.en.translation.xml'
foreach ($file in @($rocketTranslation,$tpaTranslation)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required translation file not found: $file" }
}

$backup = Join-Path $projectRoot ('runtime\backups\chat-style-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($entry in $active) { Copy-Item -LiteralPath $entry.Target -Destination (Join-Path $backup $entry.Backup) }
Copy-Item -LiteralPath $rocketTranslation -Destination (Join-Path $backup 'Rocket.Unturned.en.translation.xml')
Copy-Item -LiteralPath $tpaTranslation -Destination (Join-Path $backup 'Teleportation.en.translation.xml')

try {
    foreach ($entry in $active) {
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Target -Force
        if ((Get-FileHash -LiteralPath $entry.Source).Hash -ne (Get-FileHash -LiteralPath $entry.Target).Hash) {
            throw "Installed DLL hash mismatch: $($entry.Name)"
        }
    }

    & (Join-Path $PSScriptRoot 'Set-MreAdminTranslations.ps1') -GameDirectory $serverRoot
    $tpaText = [IO.File]::ReadAllText($tpaTranslation)
    [void][xml]$tpaText
    [void][xml]([IO.File]::ReadAllText($rocketTranslation))
} catch {
    foreach ($entry in $active) { Copy-Item -LiteralPath (Join-Path $backup $entry.Backup) -Destination $entry.Target -Force }
    Copy-Item -LiteralPath (Join-Path $backup 'Rocket.Unturned.en.translation.xml') -Destination $rocketTranslation -Force
    Copy-Item -LiteralPath (Join-Path $backup 'Teleportation.en.translation.xml') -Destination $tpaTranslation -Force
    throw
}

Write-Host "Updated plugins: $($active.Count). Backup: $backup"
if ($skipped.Count -gt 0) { Write-Host "Skipped optional plugins not installed: $($skipped -join ', ')" }
