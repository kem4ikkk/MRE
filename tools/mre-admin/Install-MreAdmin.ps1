[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$Apply,
    [string]$EffectManifest
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runtimeRoot = Join-Path $projectRoot 'runtime'
$instanceRoot = Join-Path $runtimeRoot 'server\Servers\MRE'
$pluginRoot = Join-Path $instanceRoot 'Rocket\Plugins'
$commandMapPath = Join-Path $instanceRoot 'Rocket\Commands.config.xml'
$permissionsPath = Join-Path $instanceRoot 'Rocket\Permissions.config.xml'
$lockPath = Join-Path $runtimeRoot '.mre-maintenance.lock'
$utf8 = New-Object Text.UTF8Encoding($false)

function Get-CanonicalPath([string]$Path) { [IO.Path]::GetFullPath($Path) }
function Get-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-JsonAtomic([string]$Path,$Value,[int]$Depth) {
    $directory = Split-Path -Parent $Path
    $temporary = Join-Path $directory (([IO.Path]::GetFileName($Path)) + '.mre-write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary,($Value | ConvertTo-Json -Depth $Depth),$utf8)
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary,$Path,$null) }
        else { [IO.File]::Move($temporary,$Path) }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
function Read-Xml([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Не найден файл: $Path" }
    [xml]$doc = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    return $doc
}
function Assert-ServerStopped {
    . (Join-Path $projectRoot 'scripts\Get-MreProcessPath.ps1')
    $serverRoot = Join-Path $runtimeRoot 'server'
    $serverPath = (Get-CanonicalPath $serverRoot).TrimEnd('\')
    $serverPrefix = $serverPath + '\'
    $serverExe = Get-CanonicalPath (Join-Path $serverRoot 'Unturned.exe')
    foreach ($processItem in @(Get-CimInstance Win32_Process -Filter "Name = 'Unturned.exe' OR Name = 'Unturned_Headless.exe'")) {
        $imagePath = Get-MreProcessPath $processItem
        if (-not $imagePath) { throw 'Не удалось проверить путь работающего Unturned; Apply отменён.' }
        $canonicalImagePath = Get-CanonicalPath $imagePath
        if ($canonicalImagePath.Equals($serverExe,[StringComparison]::OrdinalIgnoreCase) -or $canonicalImagePath.StartsWith($serverPrefix,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Сначала корректно остановите MRE; Apply изменяет файлы сервера.'
        }
    }
}
function Get-AdminMappings([xml]$Document) {
    @($Document.SelectNodes('/RocketCommands/Commands/Command'))
}
function Set-Mapping([xml]$Document,[string]$Name,[string]$TypeName) {
    $nodes = @(Get-AdminMappings $Document | Where-Object { $_.GetAttribute('Name') -eq $Name -and $_.InnerText -eq $TypeName })
    if ($nodes.Count -eq 0) {
        $node = $Document.CreateElement('Command')
        $node.SetAttribute('Name',$Name)
        [void]$node.SetAttribute('Enabled','true')
        [void]$node.SetAttribute('Priority','High')
        $node.InnerText = $TypeName
        [void]$Document.DocumentElement.SelectSingleNode('Commands').AppendChild($node)
        return
    }
    foreach ($node in $nodes) { $node.SetAttribute('Enabled','true'); $node.SetAttribute('Priority','High') }
}
function Update-CommandMap([xml]$Document) {
    $nativeGive = 'Rocket.Unturned.Commands.UnturnedCommands/give'
    $mreGive = 'MreAdminTools.CommandGive/give'
    $mreVeh = 'MreAdminTools.CommandVehicle/veh'
    foreach ($node in Get-AdminMappings $Document) {
        if ($node.GetAttribute('Name') -eq 'give' -and $node.GetAttribute('Enabled') -ne 'false' -and $node.InnerText -ne $nativeGive -and $node.InnerText -ne $mreGive) {
            throw "Неизвестный enabled mapping /give '$($node.InnerText)'; отказ закрыт. Разберите конфликт вручную."
        }
        if ($node.GetAttribute('Name') -eq 'give' -and $node.InnerText -eq $nativeGive) { $node.SetAttribute('Enabled','false') }
        if ($node.GetAttribute('Name') -eq 'veh' -and $node.GetAttribute('Enabled') -ne 'false' -and $node.InnerText -ne $mreVeh) {
            throw "Конфликт enabled mapping /veh '$($node.InnerText)'; отказ закрыт."
        }
        if ($node.GetAttribute('Name') -eq 'veh' -and $node.InnerText -eq $mreVeh) { $node.SetAttribute('Enabled','true'); $node.SetAttribute('Priority','High') }
    }
    Set-Mapping $Document 'give' $mreGive
    Set-Mapping $Document 'veh' $mreVeh
    # Native /vehicle is intentionally untouched.
    $vehicle = @(Get-AdminMappings $Document | Where-Object { $_.GetAttribute('Name') -eq 'vehicle' })
    if ($vehicle.Count -eq 0) { throw 'В карте команд отсутствует /vehicle; отказ закрыт, чтобы не скрыть изменение окружения.' }
}
function Update-AdminPermissions([xml]$Document) {
    $allGroups = @($Document.SelectNodes('/RocketPermissions/Groups/Group'))
    $seenIds = @{}
    foreach ($groupNode in $allGroups) {
        $idNode = $groupNode.SelectSingleNode('Id')
        $groupId = if ($idNode) { [string]$idNode.InnerText } else { '' }
        if ([string]::IsNullOrWhiteSpace($groupId) -or $seenIds.ContainsKey($groupId)) {
            throw 'В Rocket Permissions найдены пустой или повторяющийся Id группы (без учёта регистра).'
        }
        $seenIds[$groupId] = $true
    }
    $defaultNode = $Document.SelectSingleNode('/RocketPermissions/DefaultGroup')
    if (-not $defaultNode -or [string]$defaultNode.InnerText -ne 'default') {
        throw 'DefaultGroup должен быть default: назначение staff-группы по умолчанию запрещено.'
    }

    $admins = @($Document.SelectNodes('/RocketPermissions/Groups/Group[Id="head_admin"]'))
    if ($admins.Count -ne 1) { throw 'Ожидалась ровно одна группа head_admin в Rocket Permissions.' }
    $ownerGroups = @($Document.SelectNodes('/RocketPermissions/Groups/Group') | Where-Object { $_.SelectSingleNode('Id').InnerText -eq 'mre_owner' })
    if ($ownerGroups.Count -ne 1) { throw 'Ожидалась ровно одна группа mre_owner.' }
    $ownerParent = $ownerGroups[0].SelectSingleNode('ParentGroup')
    if (-not $ownerParent -or $ownerParent.InnerText -ne 'head_admin') { throw 'Группа mre_owner должна наследовать head_admin.' }
    $group = $admins[0]
    $permissions = $group.SelectSingleNode('Permissions')
    if (-not $permissions) { throw 'У head_admin отсутствует секция Permissions.' }

    # Refuse broad rights or a child role that would inherit the Head Admin nodes.
    # Check all groups before adding anything so a refusal leaves the in-memory plan intact.
    foreach ($groupNode in $allGroups) {
        $groupId = [string]$groupNode.SelectSingleNode('Id').InnerText
        if ($groupId -eq 'head_admin') { continue }
        $direct = @($groupNode.SelectNodes('Permissions/Permission') | Where-Object { $_.InnerText -in @('mre.admin.give','mre.admin.veh','*','mre.admin.*') })
        if ($direct.Count -gt 0) { throw "Широкое или head-only право найдено вне head_admin в группе '$groupId'; исправьте permissions перед установкой." }

        $parentId = [string]$groupNode.SelectSingleNode('ParentGroup').InnerText
        $visited = @{}
        while ($parentId) {
            if ($parentId -eq 'head_admin' -and $groupId -ne 'mre_owner') { throw "Группа '$groupId' наследует head_admin; установка прекращена." }
            if ($visited.ContainsKey($parentId)) { throw "Обнаружен цикл ParentGroup при проверке '$groupId'." }
            $visited[$parentId] = $true
            $parentNodes = @($Document.SelectNodes('/RocketPermissions/Groups/Group') | Where-Object { $_.SelectSingleNode('Id').InnerText -eq $parentId })
            if ($parentNodes.Count -eq 0) { break }
            if ($parentNodes.Count -ne 1) { throw "Id родительской группы '$parentId' не уникален." }
            $parentElement = $parentNodes[0].SelectSingleNode('ParentGroup')
            $parentId = if ($parentElement) { [string]$parentElement.InnerText } else { '' }
        }
    }

    foreach ($id in @('mre.admin.give','mre.admin.veh')) {
        $matches = @($permissions.SelectNodes('Permission') | Where-Object { $_.InnerText -eq $id })
        if ($matches.Count -eq 0) {
            $permission = $Document.CreateElement('Permission')
            $permission.SetAttribute('Cooldown','0')
            $permission.InnerText = $id
            [void]$permissions.AppendChild($permission)
        }
    }
}
function Get-EffectEvidence {
    if ([string]::IsNullOrWhiteSpace($EffectManifest)) { throw 'Apply заблокирован: передайте Effect manifest с предоставленным bundle и явным подтверждением клиентской доставки.' }
    $manifestPath = Get-CanonicalPath $EffectManifest
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Effect manifest не найден: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.effect_id -ne 48626 -or $manifest.effect_key -ne 14826 -or $manifest.guid -ne 'bc15863fb9c24a69a36bb733456cc3b2') { throw 'Effect manifest не совпадает с ID/key/GUID MRE Admin.' }
    if ($manifest.owner_approved -ne $true -or [string]::IsNullOrWhiteSpace([string]$manifest.client_delivery)) { throw 'Apply заблокирован: manifest должен фиксировать owner_approved=true и согласованный client_delivery.' }
    $base = Split-Path -Parent $manifestPath
    $bundlePath = Join-Path $base ([string]$manifest.bundle_file)
    $datPath = Join-Path $base ([string]$manifest.dat_file)
    $basePrefix = (Get-CanonicalPath $base).TrimEnd('\') + '\'
    if (-not (Get-CanonicalPath $bundlePath).StartsWith($basePrefix,[StringComparison]::OrdinalIgnoreCase) -or -not (Get-CanonicalPath $datPath).StartsWith($basePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Effect files должны находиться рядом с manifest, без выхода из его каталога.' }
    if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf) -or -not (Test-Path -LiteralPath $datPath -PathType Leaf)) { throw 'Effect bundle и dat должны быть предоставлены рядом с manifest.' }
    $bundleHash = Get-Hash $bundlePath
    $datHash = Get-Hash $datPath
    if ($bundleHash -ne ([string]$manifest.bundle_sha256).ToLowerInvariant() -or $datHash -ne ([string]$manifest.dat_sha256).ToLowerInvariant()) { throw 'SHA256 Effect files не совпадает с manifest.' }
    $datText = Get-Content -LiteralPath $datPath -Raw -Encoding UTF8
    $datFields = @{}
    foreach ($line in ($datText -split "`r?`n")) {
        if ($line -match '^\s*(GUID|Type|ID)\s+(.+?)\s*$') {
            $fieldName = $Matches[1]
            if (-not $datFields.ContainsKey($fieldName)) { $datFields[$fieldName] = @() }
            $datFields[$fieldName] += $Matches[2].Trim()
        }
    }
    if ($datFields['GUID'].Count -ne 1 -or $datFields['GUID'][0] -ne 'bc15863fb9c24a69a36bb733456cc3b2' -or
        $datFields['Type'].Count -ne 1 -or $datFields['Type'][0] -ne 'Effect' -or
        $datFields['ID'].Count -ne 1 -or $datFields['ID'][0] -ne '48626') { throw 'Effect .dat должен непосредственно содержать по одному ожидаемому полю GUID, Type Effect и ID 48626.' }
    [ordered]@{ manifest=(Get-CanonicalPath $manifestPath); effect_id=48626; effect_key=14826; guid='bc15863fb9c24a69a36bb733456cc3b2'; bundle_sha256=$bundleHash; dat_sha256=$datHash; client_delivery=[string]$manifest.client_delivery; owner_approved=$true }
}
function Get-SourceHashStatus([string]$Plugin,$Manifest) {
    $pluginSourceRoot = Join-Path $projectRoot ('src\' + $Plugin)
    $sourceFiles = @(Get-ChildItem -LiteralPath $pluginSourceRoot -File -Filter '*.cs')
    $manifestSources = @()
    if ($null -ne $Manifest.sources) { $manifestSources = @($Manifest.sources) }
    $sharedSource = Join-Path $projectRoot 'src\MreShared\MreChat.cs'
    if (@($manifestSources | Where-Object { $_.name -eq 'MreChat.cs' }).Count -gt 0 -and
        @($sourceFiles | Where-Object { $_.Name -eq 'MreChat.cs' }).Count -eq 0) {
        if (Test-Path -LiteralPath $sharedSource -PathType Leaf) { $sourceFiles += Get-Item -LiteralPath $sharedSource }
    }
    $issues = @()
    if ($manifestSources.Count -ne $sourceFiles.Count) { $issues += "Количество sources в manifest ($($manifestSources.Count)) не совпадает с текущими входами сборки ($($sourceFiles.Count))." }
    foreach ($source in $sourceFiles) {
        $matchingEntries = @($manifestSources | Where-Object { $_.name -eq $source.Name })
        if ($matchingEntries.Count -ne 1) {
            $issues += "Нет единственной корректной записи SHA256 для исходника $($source.Name)."
            continue
        }
        $expectedSourceHash = [string]$matchingEntries[0].sha256
        if ($expectedSourceHash -notmatch '^[0-9a-fA-F]{64}$') {
            $issues += "Некорректный SHA256 для исходника $($source.Name)."
            continue
        }
        $expectedSourceHash = $expectedSourceHash.ToLowerInvariant()
        if ((Get-Hash $source.FullName) -ne $expectedSourceHash) { $issues += "Исходник $($source.Name) изменился после сборки DLL." }
    }
    foreach ($entry in $manifestSources) {
        if (@($sourceFiles | Where-Object { $_.Name -eq $entry.name }).Count -ne 1) { $issues += "Manifest содержит неизвестный или дублирующий исходник $($entry.name)." }
    }
    [pscustomobject]@{ valid=($issues.Count -eq 0); issues=$issues }
}

# Always perform read-only preflight and emit a machine-readable plan. Nothing is written unless -Apply is supplied.
$commandMap = Read-Xml $commandMapPath
$permissions = Read-Xml $permissionsPath
Update-CommandMap $commandMap
$permissionPreflightError = $null
try { Update-AdminPermissions $permissions }
catch {
    if ($Apply) { throw }
    $permissionPreflightError = $_.Exception.Message
}
$artifacts = @()
foreach ($name in @('MreAdminTools','MreAdminMenu','MreInterface','MreFlight','MreModeration','MreInventoryAudit','MreAirdrop')) {
    $root = Join-Path $runtimeRoot ('builds\' + $name)
    $dll = Join-Path $root ($name + '.dll')
    $manifestPath = Join-Path $root 'build-manifest.json'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $artifacts += [ordered]@{ plugin=$name; available=$false; reason='DLL/build-manifest отсутствует; сборка здесь не выполнялась.' }
        continue
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $hash = Get-Hash $dll
    if ($manifest.plugin -ne $name -or $manifest.sha256 -ne $hash) { throw "Неверный build manifest или SHA256 для $name." }
    $sourceStatus = Get-SourceHashStatus $name $manifest
    $artifacts += [ordered]@{ plugin=$name; available=$true; source=$dll; sha256=$hash; source_hashes_valid=$sourceStatus.valid; source_hash_issues=@($sourceStatus.issues) }
}
$dependencyChecks = @()
$menuArtifact = $null
foreach ($artifact in $artifacts) { if ($artifact['plugin'] -eq 'MreAdminMenu' -and $artifact['available']) { $menuArtifact = $artifact; break } }
if ($menuArtifact) {
    $menuManifest = Get-Content -LiteralPath (Join-Path $runtimeRoot 'builds\MreAdminMenu\build-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($dependency in @('MreAdminTools.dll','MreInterface.dll','MreFlight.dll','MreModeration.dll','MreInventoryAudit.dll','MreAirdrop.dll')) {
        $references = @($menuManifest.references | Where-Object { $_.name -eq $dependency })
        $expected = if ($references.Count -eq 1 -and [string]$references[0].sha256 -match '^[0-9a-fA-F]{64}$') { ([string]$references[0].sha256).ToLowerInvariant() } else { $null }
        $actual = $null
        $artifactName = [IO.Path]::GetFileNameWithoutExtension($dependency)
        foreach ($artifact in $artifacts) { if ($artifact['plugin'] -eq $artifactName -and $artifact['available']) { $actual = [string]$artifact['sha256']; break } }
        $dependencyChecks += [ordered]@{ name=$dependency; reference_count=$references.Count; expected_sha256=$expected; actual_sha256=$actual; matched=([bool]$expected -and [bool]$actual -and $actual -eq $expected) }
    }
}
$effectEvidence = $null
if ($EffectManifest) { $effectEvidence = Get-EffectEvidence }
$plan = [ordered]@{
    mode=$(if ($Apply) { 'apply-requested' } else { 'preview' })
    writes=$(if ($Apply) { @('MreAdminTools.dll','MreAdminMenu.dll','MreInterface.dll','MreFlight.dll','MreModeration.dll','MreInventoryAudit.dll','MreAirdrop.dll','Commands.config.xml','Permissions.config.xml','install-receipt.json') } else { 'none in preview' })
    command_changes=@('disable only Rocket.Unturned.Commands.UnturnedCommands/give','enable High MreAdminTools.CommandGive/give','enable High MreAdminTools.CommandVehicle/veh','leave /vehicle unchanged')
    permission_changes=@('mre.admin.give -> head_admin only','mre.admin.veh -> head_admin only')
    permission_preflight_error=$permissionPreflightError
    artifacts=$artifacts
    menu_dependency_hashes=$dependencyChecks
    effect=$effectEvidence
    preview_no_write=(-not $Apply)
}
$plan | ConvertTo-Json -Depth 8
if (-not $Apply) { return }
$missingArtifact = $false
foreach ($artifact in $artifacts) { if (-not $artifact['available']) { $missingArtifact = $true } }
if ($missingArtifact) { throw 'Apply заблокирован: все семь DLL и их build-manifest обязательны.' }
$invalidDependencySources = @($artifacts | Where-Object { -not $_.source_hashes_valid })
if ($invalidDependencySources.Count -gt 0) { throw 'Apply заблокирован: один или несколько исходников DLL отсутствуют в manifest или не совпадают по SHA256.' }
$dependencyMismatch = $false
foreach ($dependency in $dependencyChecks) { if (-not $dependency['matched']) { $dependencyMismatch = $true } }
    if ($dependencyMismatch) { throw 'Apply заблокирован: DLL зависимостей не совпадают с hashes из MreAdminMenu build-manifest.' }
if (-not $effectEvidence) { throw 'Apply заблокирован: нет согласованного client Effect bundle.' }
if (-not $PSCmdlet.ShouldProcess($instanceRoot,'Установить MRE AdminTools/AdminMenu и точечные mappings/permissions')) { return }
Assert-ServerStopped
$lockHandle = $null
$backupRoot = $null
$receiptPath = $null
try {
    $lockHandle = [IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None')
    Assert-ServerStopped
    # Re-read under lock so the plan cannot overwrite intervening configuration edits.
    $commandMap = Read-Xml $commandMapPath
    $permissions = Read-Xml $permissionsPath
    Update-CommandMap $commandMap
    Update-AdminPermissions $permissions
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
    $backupRoot = Join-Path $runtimeRoot ('backups\mre-admin-' + $stamp)
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $targets = @($commandMapPath,$permissionsPath)
    foreach ($artifact in $artifacts) { $targets += Join-Path $pluginRoot ($artifact.plugin + '.dll') }
    $files = @()
    foreach ($target in $targets) {
        $exists = Test-Path -LiteralPath $target -PathType Leaf
        $relative = if ($target.StartsWith($pluginRoot,[StringComparison]::OrdinalIgnoreCase)) { 'Plugins\' + $target.Substring($pluginRoot.Length).TrimStart('\') } else { $target.Substring($instanceRoot.Length).TrimStart('\') }
        $backup = Join-Path $backupRoot ('before\' + $relative)
        if ($exists) { New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null; Copy-Item -LiteralPath $target -Destination $backup }
        $files += [ordered]@{ path=$target; existed=$exists; before_sha256=$(if ($exists) { Get-Hash $target } else { $null }); backup=$(if ($exists) { $backup } else { $null }) }
    }
    try {
        foreach ($artifact in $artifacts) {
            $target = Join-Path $pluginRoot ($artifact.plugin + '.dll')
            $temp = $target + '.mre-install.tmp'
            if ((Get-Hash $artifact.source) -ne $artifact.sha256) { throw "Сборка изменилась после preflight: $($artifact.plugin)." }
            Copy-Item -LiteralPath $artifact.source -Destination $temp -Force
            if ((Get-Hash $temp) -ne $artifact.sha256) { throw "Копия $($artifact.plugin) не прошла SHA256." }
            Move-Item -LiteralPath $temp -Destination $target -Force
        }
        $commandMap.Save($commandMapPath)
        $permissions.Save($permissionsPath)
        foreach ($file in $files) { $file['after_sha256'] = if (Test-Path -LiteralPath $file.path -PathType Leaf) { Get-Hash $file.path } else { $null } }
        $receipt = [ordered]@{ schema=1; status='installed'; created_utc=[DateTime]::UtcNow.ToString('o'); backup_root=$backupRoot; effect=$effectEvidence; files=$files; rollback_command="powershell -NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\Rollback-MreAdmin.ps1`" -Receipt `"RECEIPT_PATH`"" }
        $receiptPath = Join-Path $backupRoot 'install-receipt.json'
        [IO.File]::WriteAllText($receiptPath,($receipt | ConvertTo-Json -Depth 8),$utf8)
        Write-Host "Установлено. Receipt: $receiptPath"
    }
    catch {
        $installFailure = $_
        $compensationErrors = @()
        $recoveryPath = Join-Path $backupRoot 'install-failure-recovery.json'
        $recoveryFiles = @($files | ForEach-Object { [ordered]@{ path=$_.path; existed=$_.existed; before_sha256=$_.before_sha256; backup=$_.backup } })
        $recovery = [ordered]@{ schema=1; status='compensation_started'; failed_utc=[DateTime]::UtcNow.ToString('o'); failure=$installFailure.Exception.Message; files=$recoveryFiles; compensation_errors=@() }
        try { Write-JsonAtomic $recoveryPath $recovery 8 }
        catch { $compensationErrors += ("Не удалось сохранить recovery receipt {0}: {1}" -f $recoveryPath,$_.Exception.Message) }

        for ($index = $files.Count - 1; $index -ge 0; $index--) {
            $file = $files[$index]
            try {
                if ($file.existed) {
                    if (-not $file.backup -or -not (Test-Path -LiteralPath $file.backup -PathType Leaf)) { throw 'Исходный snapshot отсутствует.' }
                    if ((Get-Hash $file.backup) -ne ([string]$file.before_sha256).ToLowerInvariant()) { throw 'SHA256 исходного snapshot не совпадает.' }
                    Copy-Item -LiteralPath $file.backup -Destination $file.path -Force
                    if ((Get-Hash $file.path) -ne ([string]$file.before_sha256).ToLowerInvariant()) { throw 'Восстановленная цель не прошла SHA256.' }
                }
                elseif (Test-Path -LiteralPath $file.path -PathType Leaf) {
                    Remove-Item -LiteralPath $file.path -Force
                    if (Test-Path -LiteralPath $file.path -PathType Leaf) { throw 'Новая цель осталась после удаления.' }
                }
            }
            catch { $compensationErrors += ("цель {0}: {1}" -f $file.path,$_.Exception.Message) }
            $temporary = [string]$file.path + '.mre-install.tmp'
            try { if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force } }
            catch { $compensationErrors += ("временный файл {0}: {1}" -f $temporary,$_.Exception.Message) }
        }
        $recovery.status = if ($compensationErrors.Count -eq 0) { 'compensated' } else { 'compensation_incomplete' }
        $recovery.completed_utc = [DateTime]::UtcNow.ToString('o')
        $recovery.compensation_errors = @($compensationErrors)
        try { Write-JsonAtomic $recoveryPath $recovery 8 }
        catch { $compensationErrors += ("Не удалось обновить recovery receipt {0}: {1}" -f $recoveryPath,$_.Exception.Message) }
        if ($compensationErrors.Count -gt 0) {
            throw ("Install failed ({0}); compensation was incomplete: {1}. Recovery data: {2}; исходные snapshots: {3}" -f $installFailure.Exception.Message,($compensationErrors -join '; '),$recoveryPath,(Join-Path $backupRoot 'before'))
        }
        throw $installFailure
    }
}
finally { if ($lockHandle) { $lockHandle.Dispose() } }

