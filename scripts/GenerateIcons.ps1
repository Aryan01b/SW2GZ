# Generates the SW2GZ ribbon icon set at the SOLIDWORKS-standard sizes.
#
# Icons are drawn directly with GDI+ vector primitives (no external source
# asset), so they are crisp at every size and are 100% original work — no
# third-party licensing or attribution required.
#
# Two glyphs, sharing an isometric-cube motif so the group reads as one set:
#   * Create Model : a solid isometric cube (the assembly/model).
#   * Export       : the cube with an arrow leaving it (export the model).
#
# Output (the .csproj copies these to bin\<cfg>\images next to the DLL, where
# Sw2gzIconList()/Sw2gzMainIconList() resolve them at runtime):
#   SW2GZ\UI\Resources\Icons\sw2gz_<size>.png        single cube  (group glyph)
#   SW2GZ\UI\Resources\Icons\sw2gz_strip_<size>.png  [cube|export] sprite strip
#
# The strip is what ICommandGroup.IconList wants: every button's icon laid out
# horizontally, one column per command, selected by AddCommandItem2's image
# index (0 = Create Model, 1 = Export).
#
# Run: powershell -ExecutionPolicy Bypass -File scripts\GenerateIcons.ps1

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$outDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\SW2GZ\UI\Resources\Icons'))
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# Flat, modern palette (ARGB).
$cTop      = [System.Drawing.Color]::FromArgb(255, 111, 177, 252)  # 6FB1FC light blue
$cLeft     = [System.Drawing.Color]::FromArgb(255,  46, 111, 184)  # 2E6FB8 mid blue
$cRight    = [System.Drawing.Color]::FromArgb(255,  31,  78, 130)  # 1F4E82 dark blue
$cEdge     = [System.Drawing.Color]::FromArgb(255,  18,  40,  64)  # 122840 outline
$cArrow    = [System.Drawing.Color]::FromArgb(255, 243, 156,  18)  # F39C12 orange
$cArrowEdg = [System.Drawing.Color]::FromArgb(255, 176, 108,   8)

function New-Pt([double]$x, [double]$y) { New-Object System.Drawing.PointF([single]$x, [single]$y) }

# Draw an isometric cube filling the square region (ox,oy,s).
function Draw-Cube {
    param($g, [double]$ox, [double]$oy, [double]$s, [double]$pad = 0.10)

    $o   = $ox + $pad * $s
    $oyP = $oy + $pad * $s
    $w   = $s * (1.0 - 2.0 * $pad)
    $sw  = [Math]::Max(1.0, $s / 16.0)

    $P = { param($nx, $ny) New-Pt ($o + $nx * $w) ($oyP + $ny * $w) }
    # NB: PowerShell variables are case-insensitive, so the bottom vertex must
    # NOT be named $G — it would clobber the $g (Graphics) parameter.
    $vA = & $P 0.50 0.00; $vB = & $P 1.00 0.25; $vC = & $P 0.00 0.25
    $vD = & $P 0.50 0.50; $vE = & $P 1.00 0.75; $vF = & $P 0.00 0.75
    $vG = & $P 0.50 1.00

    $pen = New-Object System.Drawing.Pen($cEdge, [single]$sw)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $top   = [System.Drawing.PointF[]]@($vA, $vB, $vD, $vC)
    $left  = [System.Drawing.PointF[]]@($vC, $vD, $vG, $vF)
    $right = [System.Drawing.PointF[]]@($vB, $vE, $vG, $vD)

    $bTop   = New-Object System.Drawing.SolidBrush($cTop)
    $bLeft  = New-Object System.Drawing.SolidBrush($cLeft)
    $bRight = New-Object System.Drawing.SolidBrush($cRight)

    $g.FillPolygon($bLeft, $left); $g.FillPolygon($bRight, $right); $g.FillPolygon($bTop, $top)
    $g.DrawPolygon($pen, $top); $g.DrawPolygon($pen, $left); $g.DrawPolygon($pen, $right)

    $bTop.Dispose(); $bLeft.Dispose(); $bRight.Dispose(); $pen.Dispose()
}

# Draw the export glyph: a small cube in the lower-left + an arrow leaving it
# toward the upper-right.
function Draw-Export {
    param($g, [double]$ox, [double]$oy, [double]$s)

    Draw-Cube $g ($ox + $s * 0.02) ($oy + $s * 0.30) ($s * 0.66) 0.06

    $sw  = [Math]::Max(1.5, $s / 11.0)
    $p1  = New-Pt ($ox + 0.50 * $s) ($oy + 0.52 * $s)   # tail (near cube)
    $tip = New-Pt ($ox + 0.90 * $s) ($oy + 0.12 * $s)   # head tip (upper-right)

    $dx = $tip.X - $p1.X; $dy = $tip.Y - $p1.Y
    $len = [Math]::Sqrt($dx * $dx + $dy * $dy)
    $ux = $dx / $len; $uy = $dy / $len
    $hl = $s * 0.30; $hw = $s * 0.22
    $bx = $tip.X - $ux * $hl; $by = $tip.Y - $uy * $hl
    $px = -$uy; $py = $ux
    $h1 = New-Pt ($bx + $px * $hw * 0.5) ($by + $py * $hw * 0.5)
    $h2 = New-Pt ($bx - $px * $hw * 0.5) ($by - $py * $hw * 0.5)

    $penA = New-Object System.Drawing.Pen($cArrow, [single]$sw)
    $penA.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penA.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($penA, $p1, (New-Pt $bx $by))

    $bArrow  = New-Object System.Drawing.SolidBrush($cArrow)
    $penEdge = New-Object System.Drawing.Pen($cArrowEdg, [single]([Math]::Max(1.0, $s / 22.0)))
    $penEdge.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $head = [System.Drawing.PointF[]]@($tip, $h1, $h2)
    $g.FillPolygon($bArrow, $head); $g.DrawPolygon($penEdge, $head)

    $penA.Dispose(); $bArrow.Dispose(); $penEdge.Dispose()
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

foreach ($size in 20, 32, 40, 64, 96, 128) {
    # Single cube (group glyph / MainIconList).
    $c = New-Canvas $size $size; $bmp = $c[0]; $g = $c[1]
    Draw-Cube $g 0 0 $size 0.12
    $g.Dispose()
    $f = Join-Path $outDir ("sw2gz_{0}.png" -f $size)
    $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "wrote $f"

    # Sprite strip [cube | export] (toolbar IconList).
    $c = New-Canvas (2 * $size) $size; $bmp = $c[0]; $g = $c[1]
    Draw-Cube   $g 0     0 $size 0.12
    Draw-Export $g $size 0 $size
    $g.Dispose()
    $f = Join-Path $outDir ("sw2gz_strip_{0}.png" -f $size)
    $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "wrote $f"
}

Write-Host "done -> $outDir"
