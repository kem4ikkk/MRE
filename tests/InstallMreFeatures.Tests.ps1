# Проверка обновления на изолированном экземпляре с фиктивными DLL.
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $repoRoot ('runtime\features-tests\' + [Guid]::NewGuid().ToString('N'))
$plugins=Join-Path $fixture 'runtime\server\Servers\MRE\Rocket\Plugins'
$utf8=New-Object Text.UTF8Encoding($false)
function Write-File([string]$Relative,[string]$Content) {
    $path=Join-Path $fixture $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path,$Content,$utf8)
}
$checks=0
function Check([bool]$Condition,[string]$Name) { if(-not $Condition){throw "FAIL: $Name"}; $script:checks++ }
function Must-Fail([scriptblock]$Action,[string]$Name) { $failed=$false;try{& $Action | Out-Null}catch{$failed=$true};Check $failed $Name }
foreach($name in @('MreEssentials','MreInterface','MreKits','MreModeration','MreInventoryAudit','MreFlight','MreVault')) {
    Write-File ("runtime\builds\$name\$name.dll") ("new-$name")
    Write-File ("src\$name\LICENSE") ("test-license-$name")
    $hash=(Get-FileHash -LiteralPath (Join-Path $fixture "runtime\builds\$name\$name.dll")).Hash
    Write-File ("runtime\builds\$name\build-manifest.json") (@{plugin=$name;sha256=$hash}|ConvertTo-Json)
}
Write-File 'runtime\plugin-builds\Teleportation.MRE\Teleportation.dll' 'new-TPA'
$tpaHash=(Get-FileHash -LiteralPath (Join-Path $fixture 'runtime\plugin-builds\Teleportation.MRE\Teleportation.dll')).Hash
Write-File 'runtime\plugin-builds\Teleportation.MRE\build.json' (@{patch_id='mre-recipient-guard-v1';sha256=$tpaHash}|ConvertTo-Json)
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\MreEssentials.dll' 'old-essentials'
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\MreEssentials\MreEssentials.configuration.xml' '<MreEssentialsConfiguration><MinimumPlayers>2</MinimumPlayers><VoteCooldownSeconds>987</VoteCooldownSeconds><Custom>keep</Custom></MreEssentialsConfiguration>'
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\MoreHomes\MoreHomes.configuration.xml' '<MoreHomesConfiguration><DefaultMaxHomes>1</DefaultMaxHomes><DefaultHomeDelay>15</DefaultHomeDelay><DefaultHomeCooldown>321</DefaultHomeCooldown><VIPMaxHomes><VIPPermission PermissionTag="custom" Value="7"/></VIPMaxHomes></MoreHomesConfiguration>'
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\MoreHomes\Homes.json' '{"home":"keep"}'
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\MreHud.dll' 'other-session'
$hudHash=(Get-FileHash -LiteralPath (Join-Path $plugins 'MreHud.dll')).Hash
$essentialsHash=(Get-FileHash -LiteralPath (Join-Path $fixture 'runtime\builds\MreEssentials\MreEssentials.dll')).Hash
$hudManifest=@{plugin='MreHud';sha256=$hudHash;essentials_sha256=$essentialsHash;teleportation_sha256=$tpaHash}
Write-File 'runtime\plugin-builds\MreHud\build.json' ($hudManifest|ConvertTo-Json)
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\Teleportation\Teleportation.configuration.xml' '<custom>keep</custom>'
Write-File 'runtime\server\Servers\MRE\Rocket\Commands.config.xml' '<RocketCommands><Commands><Command Name="admin" Enabled="true" Priority="Normal">Rocket.Unturned.Commands.CommandAdmin/admin</Command><Command Name="admin" Enabled="true" Priority="Low">Rocket.Unturned.Commands.UnturnedCommands/admin</Command><Command Name="custom" Enabled="false" Priority="Low">Custom.Command/custom</Command></Commands></RocketCommands>'
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Install-MreFeatures.ps1') -Destination (Join-Path $fixture 'scripts')
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Get-MreProcessPath.ps1') -Destination (Join-Path $fixture 'scripts')
$install=Join-Path $fixture 'scripts\Install-MreFeatures.ps1'
& $install | Out-Null
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreEssentials.dll')) -eq 'old-essentials') 'preview preserves installed DLL'
$hudManifest.teleportation_sha256=('a'*64)
Write-File 'runtime\plugin-builds\MreHud\build.json' ($hudManifest|ConvertTo-Json)
Must-Fail { & $install -Apply -Confirm:$false } 'incompatible HUD dependencies rejected'
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreEssentials.dll')) -eq 'old-essentials') 'HUD mismatch leaves existing binaries untouched'
$hudManifest.teleportation_sha256=$tpaHash
Write-File 'runtime\plugin-builds\MreHud\build.json' ($hudManifest|ConvertTo-Json)
$held=[IO.File]::Open((Join-Path $fixture 'runtime\.mre-maintenance.lock'),'OpenOrCreate','ReadWrite','None')
try { Must-Fail { & $install -Apply -Confirm:$false } 'busy lock rejects mutation' } finally {$held.Dispose()}
$source=Join-Path $fixture 'runtime\builds\MreKits\MreKits.dll'
[IO.File]::WriteAllText($source,'tampered',$utf8)
Must-Fail { & $install -Apply -Confirm:$false } 'mismatched build hash rejected'
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreEssentials.dll')) -eq 'old-essentials') 'invalid build leaves all installed binaries unchanged'
[IO.File]::WriteAllText($source,'new-MreKits',$utf8)
& $install -Apply -Confirm:$false | Out-Null
$fullReceipt=Get-Content -LiteralPath (Join-Path $fixture 'runtime\features-installed.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreEssentials.dll')) -eq 'new-MreEssentials') 'compiled DLL installed'
[xml]$essential=Get-Content -LiteralPath (Join-Path $plugins 'MreEssentials\MreEssentials.configuration.xml') -Raw -Encoding UTF8
Check ($essential.DocumentElement.MinimumPlayers -eq '1' -and $essential.DocumentElement.MaxSkillsEnabled -eq 'true') 'solo vote and skills applied'
Check ($essential.DocumentElement.VoteCooldownSeconds -eq '987' -and $essential.DocumentElement.Custom -eq 'keep') 'custom plugin settings preserved'
[xml]$homes=Get-Content -LiteralPath (Join-Path $plugins 'MoreHomes\MoreHomes.configuration.xml') -Raw -Encoding UTF8
Check ($homes.DocumentElement.DefaultMaxHomes -eq '2' -and $homes.DocumentElement.DefaultHomeDelay -eq '10') 'two homes and ten second delay applied'
Check ($homes.DocumentElement.DefaultHomeCooldown -eq '321' -and $homes.DocumentElement.VIPMaxHomes.VIPPermission.Value -eq '7') 'home cooldown and other ranks preserved'
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MoreHomes\Homes.json')) -eq '{"home":"keep"}') 'home state preserved'
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreHud.dll')) -eq 'other-session') 'other session UI preserved'
Check ([IO.File]::ReadAllText((Join-Path $plugins 'Teleportation\Teleportation.configuration.xml')) -eq '<custom>keep</custom>') 'TPA config preserved'
$hudManifest.sha256=('b'*64)
Write-File 'runtime\plugin-builds\MreHud\build.json' ($hudManifest|ConvertTo-Json)
& $install | Out-Null
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreHud.dll')) -eq 'other-session') 'new HUD build does not block unchanged installed dependencies'
$oldEssentials=(Get-FileHash -LiteralPath (Join-Path $plugins 'MreEssentials.dll')).Hash
$oldHomes=(Get-FileHash -LiteralPath (Join-Path $plugins 'MoreHomes\MoreHomes.configuration.xml')).Hash
$oldTpa=(Get-FileHash -LiteralPath (Join-Path $plugins 'Teleportation.dll')).Hash
& $install -Apply -InterfaceOnly -Confirm:$false | Out-Null
Check ((Get-FileHash -LiteralPath (Join-Path $plugins 'MreEssentials.dll')).Hash -eq $oldEssentials -and
       (Get-FileHash -LiteralPath (Join-Path $plugins 'MoreHomes\MoreHomes.configuration.xml')).Hash -eq $oldHomes -and
       (Get-FileHash -LiteralPath (Join-Path $plugins 'Teleportation.dll')).Hash -eq $oldTpa) 'interface only preserves UI dependencies and HOME settings'
& $install -Apply -AdminOnly -Confirm:$false | Out-Null
Check ((Get-FileHash -LiteralPath (Join-Path $plugins 'MreEssentials.dll')).Hash -eq $oldEssentials -and
       (Get-FileHash -LiteralPath (Join-Path $plugins 'MoreHomes\MoreHomes.configuration.xml')).Hash -eq $oldHomes -and
       (Get-FileHash -LiteralPath (Join-Path $plugins 'Teleportation.dll')).Hash -eq $oldTpa) 'admin-only installation preserves existing gameplay and UI dependencies'
Must-Fail { & $install -InterfaceOnly -AdminOnly } 'conflicting targeted installation modes rejected'
Must-Fail { & $install -VaultOnly -AdminOnly } 'vault and admin modes cannot be combined'
Must-Fail { & $install -VaultOnly -InterfaceOnly } 'vault and interface modes cannot be combined'
$oldCommandMap=[IO.File]::ReadAllText((Join-Path $fixture 'runtime\server\Servers\MRE\Rocket\Commands.config.xml'))
$oldEssentials=(Get-FileHash -LiteralPath (Join-Path $plugins 'MreEssentials.dll')).Hash
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\MreVault.dll' 'old-vault'
Write-File 'runtime\server\Servers\MRE\Rocket\Plugins\MreVault\vault-state.json' 'keep-vault-data'
& $install -Apply -VaultOnly -Confirm:$false | Out-Null
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreVault.dll')) -eq 'new-MreVault') 'vault-only installs the validated DLL'
Check ([IO.File]::ReadAllText((Join-Path $plugins 'MreVault\vault-state.json')) -eq 'keep-vault-data') 'vault-only preserves vault data'
Check ([IO.File]::ReadAllText((Join-Path $fixture 'runtime\server\Servers\MRE\Rocket\Commands.config.xml')) -eq $oldCommandMap) 'vault-only preserves command mappings exactly'
Check ((Get-FileHash -LiteralPath (Join-Path $plugins 'MreEssentials.dll')).Hash -eq $oldEssentials) 'vault-only preserves other plugins'
$vaultReceipt=Get-Content -LiteralPath (Join-Path $fixture 'runtime\features-installed.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ([IO.File]::ReadAllText((Join-Path $vaultReceipt.backup_path 'MreVault.dll')) -eq 'old-vault') 'vault-only backs up previous DLL'
[xml]$mapping=Get-Content -LiteralPath (Join-Path $fixture 'runtime\server\Servers\MRE\Rocket\Commands.config.xml') -Raw -Encoding UTF8
Check ($mapping.SelectSingleNode('/RocketCommands/Commands/Command[.="Rocket.Unturned.Commands.CommandAdmin/admin"]').GetAttribute('Enabled') -eq 'false') 'conflicting Rocket admin disabled'
Check ($mapping.SelectSingleNode('/RocketCommands/Commands/Command[.="Rocket.Unturned.Commands.UnturnedCommands/admin"]').GetAttribute('Enabled') -eq 'false') 'conflicting native wrapper disabled'
Check ($mapping.SelectSingleNode('/RocketCommands/Commands/Command[.="MreInterface.CommandAdmin/admin"]').GetAttribute('Priority') -eq 'High') 'persisted MRE admin priority set'
Check ($mapping.SelectSingleNode('/RocketCommands/Commands/Command[.="Custom.Command/custom"]').GetAttribute('Enabled') -eq 'false') 'unrelated command mapping preserved'
Check ([IO.File]::ReadAllText((Join-Path $fullReceipt.backup_path 'MreEssentials.dll')) -eq 'old-essentials') 'previous binary backed up'
[pscustomobject]@{Result='PASS';Checks=$checks;Fixture=$fixture}
