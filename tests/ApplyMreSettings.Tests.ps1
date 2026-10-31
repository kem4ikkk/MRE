# Изолированная проверка слияния настроек и назначения владельца.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repoRoot ('runtime\settings-tests\' + [Guid]::NewGuid().ToString('N'))
$instance = Join-Path $fixture 'runtime\server\Servers\MRE'
$scriptsRoot = Join-Path $fixture 'scripts'
foreach ($relative in @('scripts','config','secrets','runtime\server\Servers\MRE\Server','runtime\server\Servers\MRE\Rocket')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $relative) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Apply-MreSettings.ps1') -Destination $scriptsRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Get-MreProcessPath.ps1') -Destination $scriptsRoot
$applyScript = Join-Path $scriptsRoot 'Apply-MreSettings.ps1'
$utf8 = New-Object Text.UTF8Encoding($false)
function Write-Text([string]$Relative,[string]$Value) { [IO.File]::WriteAllText((Join-Path $fixture $Relative),$Value,$utf8) }
$checks = 0
function Check([bool]$Condition,[string]$Name) { if (-not $Condition) { throw "FAIL: $Name" }; $script:checks++ }
function Must-Fail([scriptblock]$Action,[string]$Name) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $failed $Name }
Write-Text 'config\Gameplay.txt' @'
Items
{
 Quality_Full_Chance 1
 Has_Durability false
}
Players
{
 Spawn_With_Max_Skills true
}
'@
Write-Text 'runtime\server\Servers\MRE\Config.txt' @'
Items
{
 Spawn_Chance 0.5
 Quality_Full_Chance 0.1
 Unknown 77
 Nested
 {
  Quality_Full_Chance 99
 }
}
Custom
{
 Enabled true
}
'@
Write-Text 'config\RocketPermissions.xml' '<RocketPermissions><DefaultGroup>default</DefaultGroup><Groups><Group><Id>default</Id><Members/><Permissions><Permission Cooldown="0">mre.kit</Permission></Permissions></Group><Group><Id>mre_admin</Id><Members/><Permissions><Permission Cooldown="0">mre.admin</Permission></Permissions></Group></Groups></RocketPermissions>'
Write-Text 'runtime\server\Servers\MRE\Rocket\Permissions.config.xml' '<RocketPermissions><DefaultGroup>default</DefaultGroup><Groups><Group><Id>default</Id><Members><Member>76561198000000002</Member></Members><Permissions><Permission Cooldown="8">custom</Permission></Permissions></Group><Group><Id>custom</Id><Members><Member>76561198000000003</Member></Members><Permissions/></Group></Groups></RocketPermissions>'
Write-Text 'runtime\server\Servers\MRE\Server\Commands.dat' "Name Test server`r`nPort 27015`r`n"
Write-Text 'secrets\mre.local.json' '{"AdminSteamId":"76561198000000001","OwnerSteamId":"","JoinPassword":"fixture-only"}'
$configPath=Join-Path $instance 'Config.txt'
$before=(Get-FileHash -LiteralPath $configPath).Hash
& $applyScript
Check ((Get-FileHash -LiteralPath $configPath).Hash -eq $before) 'preview preserves configuration'
& $applyScript -Apply -Confirm:$false
$text=[IO.File]::ReadAllText($configPath)
Check ($text -match 'Spawn_Chance 0.5') 'item density preserved'
Check ($text -match "(?m)^\s*Quality_Full_Chance 1\s*$") 'requested scalar changed'
Check ($text -match 'Quality_Full_Chance 99') 'nested custom value preserved'
Check ($text -match 'Unknown 77' -and $text -match 'Enabled true') 'unknown settings preserved'
Check ($text -match 'Has_Durability false' -and $text -match 'Spawn_With_Max_Skills true') 'missing fields and sections added'
[xml]$xml=Get-Content -LiteralPath (Join-Path $instance 'Rocket\Permissions.config.xml') -Raw -Encoding UTF8
Check ($null -ne $xml.SelectSingleNode('/RocketPermissions/Groups/Group[Id="custom"]/Members/Member')) 'custom group preserved'
Check ($xml.SelectSingleNode('/RocketPermissions/Groups/Group[Id="default"]/Members/Member').InnerText -eq '76561198000000002') 'existing membership preserved'
Check ($xml.SelectSingleNode('/RocketPermissions/Groups/Group[Id="default"]/Permissions/Permission[.="custom"]').GetAttribute('Cooldown') -eq '8') 'existing permission cooldown preserved'
Check ($xml.SelectSingleNode('/RocketPermissions/Groups/Group[Id="mre_admin"]/Members/Member').InnerText -eq '76561198000000001') 'only selected admin assigned'
$commands=[IO.File]::ReadAllText((Join-Path $instance 'Server\Commands.dat'))
Check ($commands -match 'Name Test server' -and $commands -match 'Owner 76561198000000001') 'native owner added and server name preserved'
$firstHash=(Get-FileHash -LiteralPath $configPath).Hash
& $applyScript -Apply -Confirm:$false
Check ((Get-FileHash -LiteralPath $configPath).Hash -eq $firstHash) 'gameplay merge idempotent'
[xml]$xml=Get-Content -LiteralPath (Join-Path $instance 'Rocket\Permissions.config.xml') -Raw -Encoding UTF8
Check (@($xml.SelectNodes('/RocketPermissions/Groups/Group[Id="mre_admin"]/Members/Member')).Count -eq 1) 'admin membership not duplicated'
$held=[IO.File]::Open((Join-Path $fixture 'runtime\.mre-maintenance.lock'),'OpenOrCreate','ReadWrite','None')
try { Must-Fail { & $applyScript -Apply -Confirm:$false } 'busy maintenance lock rejected' } finally { $held.Dispose() }
Write-Text 'runtime\server\Servers\MRE\Server\Commands.dat' 'Owner 76561198000000003'
$before=(Get-FileHash -LiteralPath $configPath).Hash
Must-Fail { & $applyScript -Apply -Confirm:$false } 'different owner rejected'
Check ((Get-FileHash -LiteralPath $configPath).Hash -eq $before) 'rejected owner change preserves config'
[pscustomobject]@{Result='PASS';Checks=$checks;Fixture=$fixture}
