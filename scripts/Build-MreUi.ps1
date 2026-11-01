[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'src\Teleportation.MRE\Build.ps1')
& (Join-Path $projectRoot 'src\MoreHomes.MRE\Build.ps1')
& (Join-Path $PSScriptRoot 'Build-MreEssentials.ps1')
& (Join-Path $PSScriptRoot 'Build-MreHud.ps1')
Write-Host 'Серверные UI-модули собраны. DLL не установлены, клиентский Unity bundle собирается отдельно по ui/MreHud/README.md.'
