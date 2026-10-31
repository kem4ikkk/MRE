[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-MreProcessPath.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$serverRoot = Join-Path $projectRoot 'runtime\server'
$instanceRoot = Join-Path $serverRoot 'Servers\MRE'
$gameplayPath = Join-Path $instanceRoot 'Config.txt'
$permissionsPath = Join-Path $instanceRoot 'Rocket\Permissions.config.xml'
$commandsPath = Join-Path $instanceRoot 'Server\Commands.dat'
$settingsPath = Join-Path $projectRoot 'secrets\mre.local.json'
$utf8 = New-Object Text.UTF8Encoding($false)

function Get-Overrides([string[]]$Lines) {
    $sections = [ordered]@{}
    $depth = 0; $pending = ''; $section = ''
    foreach ($line in $Lines) {
        $text = ($line -replace '\s*//.*$', '').Trim()
        if ($text -eq '{') {
            if ($depth -eq 0) { $section = $pending; $sections[$section] = [ordered]@{} }
            $depth++; continue
        }
        if ($text -eq '}') { $depth--; continue }
        if ($depth -eq 0 -and $text -match '^\w+$') { $pending = $text }
        if ($depth -eq 1 -and $text -match '^(\w+)\s+([\w.\-]+)$') {
            $sections[$section][$Matches[1]] = $Matches[2]
        }
    }
    if ($depth -ne 0) { throw 'Несогласованные скобки шаблона Config.txt.' }
    return ,$sections
}

function Merge-Gameplay([string[]]$Lines, $Overrides) {
    $result = New-Object 'System.Collections.Generic.List[string]'
    $foundSections = @{}; $seen = @{}
    $depth = 0; $pending = ''; $section = ''
    foreach ($line in $Lines) {
        $text = ($line -replace '\s*//.*$', '').Trim()
        if ($text -eq '{') {
            if ($depth -eq 0) { $section = $pending; $foundSections[$section] = $true; $seen = @{} }
            $depth++; $result.Add($line); continue
        }
        if ($text -eq '}') {
            if ($depth -eq 1 -and $Overrides.Contains($section)) {
                foreach ($key in $Overrides[$section].Keys) {
                    if (-not $seen.ContainsKey($key)) { $result.Add("`t$key $($Overrides[$section][$key])") }
                }
            }
            $depth--; $result.Add($line); continue
        }
        if ($depth -eq 0 -and $text -match '^\w+$') { $pending = $text }
        if ($depth -eq 1 -and $Overrides.Contains($section) -and $text -match '^(\w+)(?:\s+.*)?$') {
            $key = $Matches[1]
            if ($Overrides[$section].Contains($key)) {
                $result.Add("`t$key $($Overrides[$section][$key])"); $seen[$key] = $true; continue
            }
        }
        $result.Add($line)
    }
    if ($depth -ne 0) { throw 'Несогласованные скобки рабочего Config.txt.' }
    foreach ($name in $Overrides.Keys) {
        if ($foundSections.ContainsKey($name)) { continue }
        $result.Add(''); $result.Add($name); $result.Add('{')
        foreach ($key in $Overrides[$name].Keys) { $result.Add("`t$key $($Overrides[$name][$key])") }
        $result.Add('}')
    }
    return ($result -join "`r`n") + "`r`n"
}

$overrides = Get-Overrides (Get-Content -LiteralPath (Join-Path $projectRoot 'config\Gameplay.txt') -Encoding UTF8)
$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$adminId = [string]$settings.AdminSteamId
if ($adminId -and $adminId -notmatch '^7656119\d{10}$') { throw 'Некорректный AdminSteamId в локальных настройках.' }
Write-Host ('Секции gameplay: ' + ($overrides.Keys -join ', '))
Write-Host ('Назначение подтвержденного владельца: ' + [bool]$adminId)
if (-not $Apply) { Write-Host 'Предпросмотр; файлы не изменены.'; return }
if (-not $PSCmdlet.ShouldProcess($instanceRoot, 'Применить настройки MRE и права подтвержденного владельца')) { return }

$maintenanceLock = $null
try {
    $maintenanceLock = [IO.File]::Open((Join-Path $projectRoot 'runtime\.mre-maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $serverPrefix = [IO.Path]::GetFullPath($serverRoot).TrimEnd('\') + '\'
    foreach ($processItem in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $imagePath = Get-MreProcessPath $processItem
        if (-not $imagePath) { throw 'Не удалось определить путь работающего Unturned.' }
        if ([IO.Path]::GetFullPath($imagePath).StartsWith($serverPrefix,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Сначала корректно остановите сервер MRE.'
        }
    }
    $gameplay = Merge-Gameplay (Get-Content -LiteralPath $gameplayPath -Encoding UTF8) $overrides
    [xml]$permissions = Get-Content -LiteralPath $permissionsPath -Raw -Encoding UTF8
    [xml]$template = Get-Content -LiteralPath (Join-Path $projectRoot 'config\RocketPermissions.xml') -Raw -Encoding UTF8
    foreach ($templateGroup in $template.RocketPermissions.Groups.Group) {
        $groupId = [string]$templateGroup.Id
        $group = $permissions.SelectSingleNode("/RocketPermissions/Groups/Group[Id='$groupId']")
        if (-not $group) {
            [void]$permissions.RocketPermissions.Groups.AppendChild($permissions.ImportNode($templateGroup,$true))
            continue
        }
        $permissionList = $group.SelectSingleNode('Permissions')
        foreach ($permission in $templateGroup.SelectNodes('Permissions/Permission')) {
            $existing = @($permissionList.SelectNodes('Permission') | Where-Object InnerText -eq $permission.InnerText)
            if (-not $existing.Count) { [void]$permissionList.AppendChild($permissions.ImportNode($permission,$true)) }
        }
    }
    $commands = @(Get-Content -LiteralPath $commandsPath -Encoding UTF8)
    if ($adminId) {
        $adminGroup = $permissions.SelectSingleNode('/RocketPermissions/Groups/Group[Id="mre_admin"]')
        if (-not $adminGroup) { throw 'В шаблоне нет группы mre_admin.' }
        $members = $adminGroup.SelectSingleNode('Members')
        if (-not @($members.SelectNodes('Member') | Where-Object InnerText -eq $adminId).Count) {
            $member = $permissions.CreateElement('Member'); $member.InnerText = $adminId; [void]$members.AppendChild($member)
        }
        $existingOwners = @($commands | Where-Object { $_ -match '^\s*Owner\s+' })
        foreach ($ownerLine in $existingOwners) {
            if (($ownerLine -replace '^\s*Owner\s+', '').Trim() -ne $adminId) {
                throw 'У сервера уже назначен другой Owner; автоматическая замена запрещена.'
            }
        }
        $commands = @($commands | Where-Object { $_ -notmatch '^\s*Owner\s+' }) + ('Owner ' + $adminId)
        $settings.OwnerSteamId = $adminId
    }
    $backupRoot = Join-Path $projectRoot ('runtime\config-backups\settings-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    Copy-Item -LiteralPath $gameplayPath -Destination (Join-Path $backupRoot 'Config.txt')
    Copy-Item -LiteralPath $permissionsPath -Destination (Join-Path $backupRoot 'Permissions.config.xml')
    Copy-Item -LiteralPath $commandsPath -Destination (Join-Path $backupRoot 'Commands.dat')
    Copy-Item -LiteralPath $settingsPath -Destination ($settingsPath + '.before-admin-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    [IO.File]::WriteAllText($gameplayPath,$gameplay,$utf8)
    $permissions.Save($permissionsPath)
    [IO.File]::WriteAllText($commandsPath,($commands -join "`r`n") + "`r`n",$utf8)
    [IO.File]::WriteAllText($settingsPath,($settings | ConvertTo-Json),$utf8)
    Write-Host 'Настройки применены. Существующие группы, участники и посторонние параметры сохранены.'
}
finally { if ($maintenanceLock) { $maintenanceLock.Dispose() } }
