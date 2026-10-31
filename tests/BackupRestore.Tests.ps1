# Изолированные проверки резервирования: реальный сервер MRE не изменяется.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repoRoot 'runtime\operations-tests'
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$fixture = Join-Path $fixtureRoot ('fixture-' + [Guid]::NewGuid().ToString('N'))
$fixtureScripts = Join-Path $fixture 'scripts'
$dataRoot = Join-Path $fixture 'runtime\server\Servers\MRE'
New-Item -ItemType Directory -Path $fixtureScripts,$dataRoot -Force | Out-Null
foreach ($name in @('Backup-MreServer.ps1','Restore-MreServer.ps1','Get-MreProcessPath.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot ('scripts\' + $name)) -Destination (Join-Path $fixtureScripts $name)
}
$backupScript = Join-Path $fixtureScripts 'Backup-MreServer.ps1'
$restoreScript = Join-Path $fixtureScripts 'Restore-MreServer.ps1'
$utf8 = New-Object Text.UTF8Encoding($false)
New-Item -ItemType Directory -Path (Join-Path $dataRoot 'Rocket\Plugins\Example'),(Join-Path $dataRoot 'Empty') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $dataRoot 'Config.txt'),'Version 1',$utf8)
[IO.File]::WriteAllText((Join-Path $dataRoot 'Rocket\Plugins\Example\данные.json'),'{"value":"проверка"}',$utf8)
[IO.File]::WriteAllBytes((Join-Path $dataRoot '.hidden'),[byte[]]@(0,1,2,3,255))
[IO.File]::WriteAllText((Join-Path $dataRoot 'empty.dat'),'',$utf8)
$baselineHash = (Get-FileHash -LiteralPath (Join-Path $dataRoot 'Config.txt') -Algorithm SHA256).Hash
$passed = New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$Condition,[string]$Label) { if (-not $Condition) { throw "FAIL: $Label" }; $passed.Add($Label) }
function Expect-Failure([scriptblock]$Action,[string]$Label) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Check $failed $Label
}

$created = & $backupScript -Name original
Check ($created.FileCount -eq 4) 'backup includes nested Unicode hidden and empty files'
Check (Test-Path -LiteralPath $created.ManifestPath) 'backup creates manifest'
$manifest = Get-Content -LiteralPath $created.ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($manifest.ArchiveSha256 -eq (Get-FileHash -LiteralPath $created.ArchivePath -Algorithm SHA256).Hash) 'archive checksum matches manifest'
Expect-Failure { & $backupScript -Name original } 'existing backup cannot be overwritten'

[IO.File]::WriteAllText((Join-Path $dataRoot 'Config.txt'),'changed-before-restore',$utf8)
$preview = & $restoreScript -ArchivePath $created.ArchivePath
Check ($preview.Result -eq 'Preview' -and [IO.File]::ReadAllText((Join-Path $dataRoot 'Config.txt')) -eq 'changed-before-restore') 'preview preserves current data'
$restored = & $restoreScript -ArchivePath $created.ArchivePath -Apply -Confirm:$false
Check ((Get-FileHash -LiteralPath (Join-Path $dataRoot 'Config.txt') -Algorithm SHA256).Hash -eq $baselineHash) 'restore returns original data'
Check ([IO.File]::ReadAllText((Join-Path $restored.PreviousDataPath 'Config.txt')) -eq 'changed-before-restore') 'restore keeps previous full directory'
Check ([IO.File]::ReadAllText((Join-Path $dataRoot 'Rocket\Plugins\Example\данные.json')) -eq '{"value":"проверка"}') 'restore preserves Unicode plugin state'
Check (Test-Path -LiteralPath (Join-Path $dataRoot 'Empty') -PathType Container) 'restore preserves empty directories'

$lockPath = Join-Path $fixture 'runtime\.mre-maintenance.lock'
$heldLock = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    Expect-Failure { & $backupScript -Name locked } 'backup refuses maintenance contention'
    Expect-Failure { & $restoreScript -ArchivePath $created.ArchivePath -Apply -Confirm:$false } 'restore refuses maintenance contention'
} finally { $heldLock.Dispose() }

$corruptPath = Join-Path $fixture 'backups\corrupt.zip'
[IO.File]::WriteAllBytes($corruptPath,[byte[]]@(1,2,3))
$corruptManifest = $manifest | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$corruptManifest.ArchiveFile = 'corrupt.zip'
[IO.File]::WriteAllText(($corruptPath + '.manifest.json'),($corruptManifest | ConvertTo-Json -Depth 8),$utf8)
Expect-Failure { & $restoreScript -ArchivePath $corruptPath -Apply -Confirm:$false } 'corrupted archive rejected'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$maliciousPath = Join-Path $fixture 'backups\traversal.zip'
$zip = [IO.Compression.ZipFile]::Open($maliciousPath,[IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = $zip.CreateEntry('../outside.txt')
    $stream = $entry.Open()
    try { $bytes = [Text.Encoding]::UTF8.GetBytes('outside'); $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
} finally { $zip.Dispose() }
$badManifest = [ordered]@{ FormatVersion=1; Instance='MRE'; SourceRelativePath='runtime/server/Servers/MRE'; ArchiveFile='traversal.zip'; ArchiveSha256=(Get-FileHash -LiteralPath $maliciousPath -Algorithm SHA256).Hash; Files=@([pscustomobject]@{Path='outside.txt';Length=7;Sha256=('a'*64)}) }
[IO.File]::WriteAllText(($maliciousPath + '.manifest.json'),($badManifest | ConvertTo-Json -Depth 8),$utf8)
Expect-Failure { & $restoreScript -ArchivePath $maliciousPath -Apply -Confirm:$false } 'ZIP traversal rejected before target switch'
Check ((Get-FileHash -LiteralPath (Join-Path $dataRoot 'Config.txt') -Algorithm SHA256).Hash -eq $baselineHash) 'failed restores preserve target'
Check (-not (Test-Path -LiteralPath (Join-Path $fixture 'backups\outside.txt'))) 'ZIP traversal writes nothing outside stage'

$outsideArchive = Join-Path $fixture 'outside.zip'
Copy-Item -LiteralPath $created.ArchivePath -Destination $outsideArchive
Expect-Failure { & $restoreScript -ArchivePath $outsideArchive -Apply -Confirm:$false } 'archive outside backup root rejected'

Push-Location $fixture
try { $relativePreview = & $restoreScript -ArchivePath '.\backups\original.zip'; Check ($relativePreview.Result -eq 'Preview') 'relative archive path uses PowerShell location' }
finally { Pop-Location }

[pscustomobject]@{Result='PASS';Checks=$passed.Count;Passed=@($passed);Fixture=$fixture} | ConvertTo-Json -Depth 6
