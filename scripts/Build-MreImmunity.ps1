[CmdletBinding()]
param([string]$ReferenceRoot, [string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReferenceRoot) { $ReferenceRoot = Join-Path $projectRoot 'runtime\server' }
if (-not $OutputRoot) { $OutputRoot = Join-Path ([IO.Path]::GetTempPath()) 'MreImmunity-build' }
$managed = Join-Path $ReferenceRoot 'Unturned_Data\Managed'
$rocket = Join-Path $ReferenceRoot 'Extras\Rocket.Unturned'
$sdkLine = @(& dotnet --list-sdks) | Where-Object { $_ -match '^\d' } | Select-Object -Last 1
if ($sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'Нужен .NET SDK.' }
$compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$output = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) 'MreImmunity.dll'
$refs = @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','System.Runtime.dll','Assembly-CSharp.dll','SDG.NetTransport.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.dll','UnityEngine.CoreModule.dll') | ForEach-Object { Join-Path $managed $_ }
$refs += @('Rocket.API.dll','Rocket.Core.dll','Rocket.Unturned.dll') | ForEach-Object { Join-Path $rocket $_ }
$refs += Join-Path $ReferenceRoot 'Servers\MRE\Rocket\Libraries\0Harmony.dll'
$argsList = @('/nologo','/target:library','/optimize+','/deterministic+','/langversion:7.3','/nostdlib+',('/out:"' + $output + '"'))
foreach ($ref in $refs) { if (-not (Test-Path -LiteralPath $ref)) { throw "Нет зависимости: $ref" }; $argsList += '/reference:"' + $ref + '"' }
$sources = @(Get-ChildItem (Join-Path $projectRoot 'src\MreImmunity') -Filter '*.cs' -File | Sort-Object FullName)
foreach ($source in $sources) { $argsList += '"' + $source.FullName + '"' }
$rsp = Join-Path $OutputRoot 'compile.rsp'
[IO.File]::WriteAllLines($rsp, $argsList, (New-Object Text.UTF8Encoding($true)))
& dotnet $compiler ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции: $LASTEXITCODE" }
Write-Host "Собрано: $output"
Write-Host "SHA256: $((Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Host 'Плагин не установлен и игровой сервер не изменён.'
