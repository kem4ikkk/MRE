param(
    [string]$Unity = (Join-Path $PSScriptRoot '..\runtime\tools\Unity\2022.3.62f3\Editor\Unity.exe'),
    [string]$WorkDirectory = (Join-Path $env:TEMP ('mre-safe-package-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$work = [IO.Path]::GetFullPath($WorkDirectory)
& (Join-Path $PSScriptRoot 'Build-MreDecaySafe.ps1') -Unity $Unity -WorkDirectory $work
$collection = Join-Path $work 'collection'
$items = Join-Path $collection 'Items'
New-Item -ItemType Directory -Path $items | Out-Null
$definitions = @(
    @{ Cells=100; Id=60971; Guid='bf233036b60d403c836deb8ac0f03292'; X=10; Y=10 },
    @{ Cells=200; Id=60972; Guid='1668b7ad4220431488a714a85719c1ee'; X=10; Y=20 },
    @{ Cells=300; Id=60973; Guid='3b716600a696477487302a6faf595464'; X=15; Y=20 }
)
foreach ($d in $definitions) {
    $name = 'Safe' + $d.Cells
    $dir = Join-Path $items $name
    New-Item -ItemType Directory -Path $dir | Out-Null
    $lines = @( ('GUID ' + $d.Guid), 'Type Storage', 'Useable Barricade', 'Build Storage', ('ID ' + $d.Id), 'Size_X 2', 'Size_Y 2', 'Health 600', 'Range 4', 'Radius 0.5', 'Offset 0.75', ('Storage_X ' + $d.X), ('Storage_Y ' + $d.Y), 'Locked', 'Has_Clip_Prefab false', 'Asset_Bundle_Version 6', 'Exclude_From_Master_Bundle' )
    [IO.File]::WriteAllLines((Join-Path $dir "$name.dat"), [string[]]$lines, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllLines((Join-Path $dir 'English.dat'), [string[]]@("Name TEST MRE Safe $($d.Cells)",'Description EXPERIMENTAL TEST ONLY'), [Text.Encoding]::UTF8)
    Copy-Item -LiteralPath (Join-Path $work "bundles\safe$($d.Cells).unity3d") -Destination (Join-Path $dir "$name.unity3d")
}
Get-ChildItem -File -Recurse -LiteralPath $collection | Sort-Object FullName | ForEach-Object { $h=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; '{0}  {1}' -f $h.Hash.ToLowerInvariant(), $_.FullName.Substring($collection.Length+1).Replace('\','/') } | Set-Content -Encoding ASCII -LiteralPath (Join-Path $work 'SHA256SUMS.txt')
Write-Output "Experimental collection: $collection"
Write-Output "Manifest: $work\SHA256SUMS.txt"

