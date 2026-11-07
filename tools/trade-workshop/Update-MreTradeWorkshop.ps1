[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'MreTradeWorkshopUpdate.csproj'
$mode = if ($ValidateOnly) { 'validate-only' } else { 'publish' }
& dotnet run --project $project --configuration Release -- $mode ([IO.Path]::GetFullPath($PackagePath))
if ($LASTEXITCODE -ne 0) { throw "Проверка или обновление MRE Trade Workshop завершилось с кодом $LASTEXITCODE." }
