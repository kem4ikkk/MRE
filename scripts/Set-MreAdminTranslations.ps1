[CmdletBinding()]
param([string]$GameDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $GameDirectory) { $GameDirectory = Join-Path $projectRoot 'runtime\server' }
$path = Join-Path $GameDirectory 'Servers\MRE\Rocket\Rocket.Unturned.en.translation.xml'
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Не найден перевод Rocket: $path" }

$original = [IO.File]::ReadAllText($path)
$pattern = '(<Translation\s+Id="command_(?:god|vanish|duty|i|z|v|p|tp|tphere|clear|investigate|heal|rocket|more|tps)_[^"]*"\s+Value=")(?!\[MRE-ADMIN\])'
$updated = [regex]::Replace($original, $pattern, '$1[MRE-ADMIN] ')
if ($updated -eq $original) { Write-Host 'Переводы Rocket уже отмечены или подходящие строки не найдены.'; return }

$backupDir = Join-Path $projectRoot ('runtime\backups\chat-prefix-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Copy-Item -LiteralPath $path -Destination (Join-Path $backupDir 'Rocket.Unturned.en.translation.xml')
[IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($false))
Write-Host "Переводы администраторских команд обновлены. Копия: $backupDir"
