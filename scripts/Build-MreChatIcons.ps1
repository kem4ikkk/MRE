[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'assets\chat-icons' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing

function Draw-Line($Graphics, $Pen, [single]$X1, [single]$Y1, [single]$X2, [single]$Y2) {
    $Graphics.DrawLine($Pen, $X1, $Y1, $X2, $Y2)
}
function Draw-Icon([string]$Name, [string]$Accent, [scriptblock]$Glyph) {
    $bitmap = New-Object System.Drawing.Bitmap(64,64,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $disc = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,36,46,39))
    $edge = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml($Accent),2.2)
    $stroke = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,232,226,202),3.2)
    $stroke.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $stroke.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $stroke.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.FillEllipse($disc,3,3,58,58)
    $graphics.DrawEllipse($edge,3,3,58,58)
    & $Glyph $graphics $stroke
    $small = New-Object System.Drawing.Bitmap(32,32,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $target = [System.Drawing.Graphics]::FromImage($small)
    $target.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $target.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $target.DrawImage($bitmap,0,0,32,32)
    $path = Join-Path $OutputDirectory ($Name + '.png')
    $small.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose(); $small.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    $disc.Dispose(); $edge.Dispose(); $stroke.Dispose()
    $file = Get-Item -LiteralPath $path
    if ($file.Length -lt 100 -or $file.Length -gt 100000) { throw "����������� ������ ������: $path" }
}

Draw-Icon 'system' '#71825A' { param($g,$p) Draw-Line $g $p 32 11 36 25; Draw-Line $g $p 36 25 50 30; Draw-Line $g $p 50 30 36 35; Draw-Line $g $p 36 35 32 50; Draw-Line $g $p 32 50 27 35; Draw-Line $g $p 27 35 13 30; Draw-Line $g $p 13 30 27 25; Draw-Line $g $p 27 25 32 11 }
Draw-Icon 'home' '#71825A' { param($g,$p) Draw-Line $g $p 13 29 32 13; Draw-Line $g $p 32 13 51 29; Draw-Line $g $p 18 26 18 49; Draw-Line $g $p 18 49 46 49; Draw-Line $g $p 46 49 46 26; Draw-Line $g $p 27 49 27 35; Draw-Line $g $p 27 35 37 35; Draw-Line $g $p 37 35 37 49 }
Draw-Icon 'tpa' '#71825A' { param($g,$p) Draw-Line $g $p 12 23 49 23; Draw-Line $g $p 49 23 42 17; Draw-Line $g $p 49 23 42 29; Draw-Line $g $p 52 41 15 41; Draw-Line $g $p 15 41 22 35; Draw-Line $g $p 15 41 22 47 }
Draw-Icon 'kits' '#8A7746' { param($g,$p) Draw-Line $g $p 13 23 32 13; Draw-Line $g $p 32 13 51 23; Draw-Line $g $p 13 23 32 34; Draw-Line $g $p 51 23 32 34; Draw-Line $g $p 13 23 13 44; Draw-Line $g $p 13 44 32 53; Draw-Line $g $p 51 23 51 44; Draw-Line $g $p 51 44 32 53; Draw-Line $g $p 32 34 32 53; Draw-Line $g $p 27 16 45 26; Draw-Line $g $p 27 16 27 25; Draw-Line $g $p 27 25 45 35 }
Draw-Icon 'trade' '#8A7746' { param($g,$p) Draw-Line $g $p 12 23 50 23; Draw-Line $g $p 50 23 43 17; Draw-Line $g $p 50 23 43 29; Draw-Line $g $p 52 41 14 41; Draw-Line $g $p 14 41 21 35; Draw-Line $g $p 14 41 21 47 }
Draw-Icon 'vault' '#78628F' { param($g,$p) $g.DrawRectangle($p,13,15,38,35); $g.DrawEllipse($p,23,23,18,18); $g.DrawEllipse($p,29,29,6,6); Draw-Line $g $p 32 35 32 47; Draw-Line $g $p 8 30 13 30; Draw-Line $g $p 51 30 56 30 }
Draw-Icon 'vote' '#8A7746' { param($g,$p) $g.DrawRectangle($p,16,14,32,38); Draw-Line $g $p 23 24 26 28; Draw-Line $g $p 26 28 32 21; Draw-Line $g $p 36 25 42 25; Draw-Line $g $p 23 39 26 43; Draw-Line $g $p 26 43 32 36; Draw-Line $g $p 36 40 42 40; Draw-Line $g $p 24 10 24 15; Draw-Line $g $p 40 10 40 15 }
Draw-Icon 'airdrop' '#946342' { param($g,$p) $g.DrawArc($p,12,9,40,32,180,180); Draw-Line $g $p 12 25 52 25; Draw-Line $g $p 32 26 32 39; Draw-Line $g $p 32 39 24 45; Draw-Line $g $p 24 45 40 45; Draw-Line $g $p 40 45 32 39; Draw-Line $g $p 18 16 32 27; Draw-Line $g $p 46 16 32 27 }
Draw-Icon 'staff' '#A64F45' { param($g,$p) Draw-Line $g $p 32 11 49 18; Draw-Line $g $p 49 18 47 36; Draw-Line $g $p 47 36 32 51; Draw-Line $g $p 32 51 17 36; Draw-Line $g $p 17 36 15 18; Draw-Line $g $p 15 18 32 11; Draw-Line $g $p 23 31 41 31; Draw-Line $g $p 32 22 32 40 }
Draw-Icon 'admin' '#A64F45' { param($g,$p) Draw-Line $g $p 32 11 48 18; Draw-Line $g $p 48 18 46 38; Draw-Line $g $p 46 38 32 51; Draw-Line $g $p 32 51 18 38; Draw-Line $g $p 18 38 16 18; Draw-Line $g $p 16 18 32 11; $g.DrawArc($p,25,20,14,15,180,180); $g.DrawRectangle($p,24,28,16,14); Draw-Line $g $p 32 33 32 38 }

Write-Host "Generated 10 PNG icons (32x32): $OutputDirectory"
