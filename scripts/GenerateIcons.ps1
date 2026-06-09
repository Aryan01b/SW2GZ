# Generates the SW2GZ ribbon icon set at the SOLIDWORKS-standard sizes.
#
# v2.1.0 — 19 distinct line-art glyphs for the L3b ribbon (Mode flyout +
# Common cluster + per-mode panel clusters). Monochrome single-stroke line
# art (design A in the mockup), scales cleanly across 20/32/40/64/96/128 px.
#
# Output:
#   SW2GZ\UI\Resources\Icons\sw2gz_<size>.png        single mode-flyout glyph
#   SW2GZ\UI\Resources\Icons\sw2gz_strip_<size>.png  19-column sprite strip
#
# Strip column index = AddCommandItem2's `image` arg. Order is fixed and
# matches the IMG_* constants used in Sw2gzRibbonRegistrar.cs.
#
# Column layout:
#   0  Mode flyout (Robot+World+Asset trio)
#   1  Robot mode      2  World mode      3  Asset mode
#   4  Coord           5  Preview         6  Export
#   7  Links           8  Joints          9  Inertia
#  10  Sensors        11  Actuation      12  Stack
#  13  Ground         14  Assets         15  Physics      16  Scene
#  17  Body           18  Surface
#
# Run: powershell -ExecutionPolicy Bypass -File scripts\GenerateIcons.ps1

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$outDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\SW2GZ\UI\Resources\Icons'))
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# Line-art palette — bright white for SW's dark-theme ribbon. SW does NOT
# auto-invert icon colors. Export uses amber so the call-to-action stands
# apart from the rest of the line-art icons.
$cInk    = [System.Drawing.Color]::FromArgb(255, 240, 244, 250)   # F0F4FA off-white
$cAccent = [System.Drawing.Color]::FromArgb(255, 245, 158,  11)   # F59E0B amber (Export)

function New-Pen([System.Drawing.Color]$color, [double]$thickness) {
    $p = New-Object System.Drawing.Pen($color, [single]$thickness)
    $p.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    return $p
}

# Stroke width scales with icon size. Heavier than typical line-art (s/9
# instead of s/12) so glyphs read at the 20px size SW often picks for the
# ribbon at standard DPI.
function Stroke-Width([double]$s) { [Math]::Max(1.6, $s / 9.0) }

function Map([scriptblock]$pad, [double]$ox, [double]$oy, [double]$s) {
    # Returns a function that maps normalized 0..1 coords into pixel coords
    # inside an icon cell with 12% padding (so strokes don't clip the edge).
    $p = 0.12
    $o = $ox + $p * $s
    $w = $s * (1.0 - 2.0 * $p)
    return { param($x, $y) New-Object System.Drawing.PointF([single]($o + $x * $w), [single]($oy + $p * $s + $y * $w)) }.GetNewClosure()
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

# ─── 19 glyph drawers ──────────────────────────────────────────────────

# 0 Mode flyout — small trio (cube + circle + ring) suggesting the 3 modes.
function Draw-ModeFlyout($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    # Small robot head (top-left)
    $g.DrawRectangle($pen, [single]($ox + 4*$u), [single]($oy + 4*$u), [single](7*$u), [single](6*$u))
    # Small globe (top-right)
    $g.DrawEllipse($pen, [single]($ox + 13*$u), [single]($oy + 4*$u), [single](7*$u), [single](7*$u))
    $g.DrawLine($pen, [single]($ox + 13*$u), [single]($oy + 7.5*$u), [single]($ox + 20*$u), [single]($oy + 7.5*$u))
    # Small cube (bottom)
    $g.DrawRectangle($pen, [single]($ox + 8.5*$u), [single]($oy + 13*$u), [single](7*$u), [single](7*$u))
    # Dropdown chevron at corner
    $g.DrawLine($pen, [single]($ox + 18*$u), [single]($oy + 18*$u), [single]($ox + 20*$u), [single]($oy + 20*$u))
    $g.DrawLine($pen, [single]($ox + 22*$u), [single]($oy + 18*$u), [single]($ox + 20*$u), [single]($oy + 20*$u))
    $pen.Dispose()
}

# 1 Robot mode — robot head (rounded rect + 2 eye dots + antenna)
function Draw-Robot($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    # Body
    $rect = New-Object System.Drawing.RectangleF([single]($ox + 5*$u), [single]($oy + 7*$u), [single](14*$u), [single](12*$u))
    $g.DrawRectangle($pen, $rect.X, $rect.Y, $rect.Width, $rect.Height)
    # Eyes (filled dots)
    $brush = New-Object System.Drawing.SolidBrush($cInk)
    $g.FillEllipse($brush, [single]($ox + 8*$u), [single]($oy + 11*$u), [single](2.4*$u), [single](2.4*$u))
    $g.FillEllipse($brush, [single]($ox + 13.6*$u), [single]($oy + 11*$u), [single](2.4*$u), [single](2.4*$u))
    # Antenna
    $g.DrawLine($pen, [single]($ox + 12*$u), [single]($oy + 3*$u), [single]($ox + 12*$u), [single]($oy + 7*$u))
    $g.DrawEllipse($pen, [single]($ox + 10.8*$u), [single]($oy + 2*$u), [single](2.4*$u), [single](2.4*$u))
    $brush.Dispose(); $pen.Dispose()
}

# 2 World mode — globe (circle + horizontal line + meridian arcs)
function Draw-World($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawEllipse($pen, [single]($ox + 3*$u), [single]($oy + 3*$u), [single](18*$u), [single](18*$u))
    $g.DrawLine($pen, [single]($ox + 3*$u), [single]($oy + 12*$u), [single]($ox + 21*$u), [single]($oy + 12*$u))
    # Meridian (vertical ellipse for 3D feel)
    $g.DrawEllipse($pen, [single]($ox + 9*$u), [single]($oy + 3*$u), [single](6*$u), [single](18*$u))
    $pen.Dispose()
}

# 3 Asset mode — isometric cube outline
function Draw-AssetMode($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    # Hex outline (isometric cube silhouette) + interior fold lines
    $pts = @(
        (New-Object System.Drawing.PointF([single]($ox + 12*$u), [single]($oy + 3*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 21*$u), [single]($oy + 8*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 21*$u), [single]($oy + 17*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 12*$u), [single]($oy + 22*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 3*$u),  [single]($oy + 17*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 3*$u),  [single]($oy + 8*$u)))
    )
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$pts)
    # Inner fold (three lines from centre)
    $cx = $ox + 12*$u; $cy = $oy + 12.5*$u
    $g.DrawLine($pen, [single]$cx, [single]$cy, [single]($ox + 12*$u), [single]($oy + 22*$u))
    $g.DrawLine($pen, [single]$cx, [single]$cy, [single]($ox + 21*$u), [single]($oy + 8*$u))
    $g.DrawLine($pen, [single]$cx, [single]$cy, [single]($ox + 3*$u), [single]($oy + 8*$u))
    $pen.Dispose()
}

# 4 Coord — 3-axis triad (3 arrows from origin)
function Draw-Coord($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $cx = $ox + 5*$u; $cy = $oy + 19*$u
    # +X right
    $g.DrawLine($pen, [single]$cx, [single]$cy, [single]($cx + 13*$u), [single]$cy)
    $g.DrawLine($pen, [single]($cx + 13*$u), [single]$cy, [single]($cx + 10*$u), [single]($cy - 2*$u))
    $g.DrawLine($pen, [single]($cx + 13*$u), [single]$cy, [single]($cx + 10*$u), [single]($cy + 2*$u))
    # +Y up
    $g.DrawLine($pen, [single]$cx, [single]$cy, [single]$cx, [single]($cy - 16*$u))
    $g.DrawLine($pen, [single]$cx, [single]($cy - 16*$u), [single]($cx - 2*$u), [single]($cy - 13*$u))
    $g.DrawLine($pen, [single]$cx, [single]($cy - 16*$u), [single]($cx + 2*$u), [single]($cy - 13*$u))
    # +Z out (diagonal)
    $g.DrawLine($pen, [single]$cx, [single]$cy, [single]($cx + 9*$u), [single]($cy - 9*$u))
    $g.DrawLine($pen, [single]($cx + 9*$u), [single]($cy - 9*$u), [single]($cx + 6*$u), [single]($cy - 8*$u))
    $g.DrawLine($pen, [single]($cx + 9*$u), [single]($cy - 9*$u), [single]($cx + 8*$u), [single]($cy - 6*$u))
    $pen.Dispose()
}

# 5 Preview — monitor frame + center circle (eye-on-screen)
function Draw-Preview($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawRectangle($pen, [single]($ox + 3*$u), [single]($oy + 6*$u), [single](18*$u), [single](12*$u))
    $g.DrawEllipse($pen, [single]($ox + 10*$u), [single]($oy + 10*$u), [single](4*$u), [single](4*$u))
    $g.DrawLine($pen, [single]($ox + 8*$u), [single]($oy + 20*$u), [single]($ox + 16*$u), [single]($oy + 20*$u))
    $pen.Dispose()
}

# 6 Export — page + corner fold + down arrow (accent blue)
function Draw-Export($g, $ox, $oy, $s) {
    $pen = New-Pen $cAccent (Stroke-Width $s)
    $u = $s / 24.0
    # Page outline
    $g.DrawLine($pen, [single]($ox + 4*$u), [single]($oy + 3*$u), [single]($ox + 15*$u), [single]($oy + 3*$u))
    $g.DrawLine($pen, [single]($ox + 15*$u), [single]($oy + 3*$u), [single]($ox + 20*$u), [single]($oy + 8*$u))
    $g.DrawLine($pen, [single]($ox + 20*$u), [single]($oy + 8*$u), [single]($ox + 20*$u), [single]($oy + 21*$u))
    $g.DrawLine($pen, [single]($ox + 20*$u), [single]($oy + 21*$u), [single]($ox + 4*$u), [single]($oy + 21*$u))
    $g.DrawLine($pen, [single]($ox + 4*$u), [single]($oy + 21*$u), [single]($ox + 4*$u), [single]($oy + 3*$u))
    # Fold
    $g.DrawLine($pen, [single]($ox + 15*$u), [single]($oy + 3*$u), [single]($ox + 15*$u), [single]($oy + 8*$u))
    $g.DrawLine($pen, [single]($ox + 15*$u), [single]($oy + 8*$u), [single]($ox + 20*$u), [single]($oy + 8*$u))
    # Down arrow
    $g.DrawLine($pen, [single]($ox + 12*$u), [single]($oy + 11*$u), [single]($ox + 12*$u), [single]($oy + 17*$u))
    $g.DrawLine($pen, [single]($ox + 9*$u), [single]($oy + 14*$u), [single]($ox + 12*$u), [single]($oy + 17*$u))
    $g.DrawLine($pen, [single]($ox + 15*$u), [single]($oy + 14*$u), [single]($ox + 12*$u), [single]($oy + 17*$u))
    $pen.Dispose()
}

# 7 Links — 2 interlocked circles (chain)
function Draw-Links($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawEllipse($pen, [single]($ox + 2*$u), [single]($oy + 9*$u), [single](7*$u), [single](7*$u))
    $g.DrawEllipse($pen, [single]($ox + 15*$u), [single]($oy + 9*$u), [single](7*$u), [single](7*$u))
    $g.DrawLine($pen, [single]($ox + 9*$u), [single]($oy + 12.5*$u), [single]($ox + 15*$u), [single]($oy + 12.5*$u))
    $pen.Dispose()
}

# 8 Joints — cross with center dot (axis-of-rotation indicator)
function Draw-Joints($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $cx = $ox + 12*$u; $cy = $oy + 12*$u
    $g.DrawEllipse($pen, [single]($cx - 3*$u), [single]($cy - 3*$u), [single](6*$u), [single](6*$u))
    $g.DrawLine($pen, [single]$cx, [single]($oy + 3*$u), [single]$cx, [single]($cy - 3*$u))
    $g.DrawLine($pen, [single]$cx, [single]($cy + 3*$u), [single]$cx, [single]($oy + 21*$u))
    $g.DrawLine($pen, [single]($ox + 3*$u), [single]$cy, [single]($cx - 3*$u), [single]$cy)
    $g.DrawLine($pen, [single]($cx + 3*$u), [single]$cy, [single]($ox + 21*$u), [single]$cy)
    $pen.Dispose()
}

# 9 Inertia — circle with center dot + cross-hairs
function Draw-Inertia($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $cx = $ox + 12*$u; $cy = $oy + 12*$u
    $g.DrawEllipse($pen, [single]($cx - 9*$u), [single]($cy - 9*$u), [single](18*$u), [single](18*$u))
    $g.DrawLine($pen, [single]$cx, [single]($cy - 9*$u), [single]$cx, [single]($cy + 9*$u))
    $g.DrawLine($pen, [single]($cx - 9*$u), [single]$cy, [single]($cx + 9*$u), [single]$cy)
    $brush = New-Object System.Drawing.SolidBrush($cInk)
    $g.FillEllipse($brush, [single]($cx - 1.5*$u), [single]($cy - 1.5*$u), [single](3*$u), [single](3*$u))
    $brush.Dispose(); $pen.Dispose()
}

# 10 Sensors — camera body (rect) + lens cone
function Draw-Sensors($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawRectangle($pen, [single]($ox + 3*$u), [single]($oy + 7*$u), [single](11*$u), [single](10*$u))
    # Lens cone (right side)
    $g.DrawLine($pen, [single]($ox + 14*$u), [single]($oy + 10*$u), [single]($ox + 21*$u), [single]($oy + 7*$u))
    $g.DrawLine($pen, [single]($ox + 14*$u), [single]($oy + 14*$u), [single]($ox + 21*$u), [single]($oy + 17*$u))
    $g.DrawLine($pen, [single]($ox + 21*$u), [single]($oy + 7*$u), [single]($ox + 21*$u), [single]($oy + 17*$u))
    $pen.Dispose()
}

# 11 Actuation — motor body + shaft
function Draw-Actuation($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawRectangle($pen, [single]($ox + 5*$u), [single]($oy + 9*$u), [single](11*$u), [single](6*$u))
    $g.DrawEllipse($pen, [single]($ox + 7*$u), [single]($oy + 11*$u), [single](2.5*$u), [single](2.5*$u))
    # Shaft (right)
    $g.DrawLine($pen, [single]($ox + 16*$u), [single]($oy + 12*$u), [single]($ox + 21*$u), [single]($oy + 12*$u))
    # Power lines (left)
    $g.DrawLine($pen, [single]($ox + 3*$u), [single]($oy + 10.5*$u), [single]($ox + 5*$u), [single]($oy + 10.5*$u))
    $g.DrawLine($pen, [single]($ox + 3*$u), [single]($oy + 13.5*$u), [single]($ox + 5*$u), [single]($oy + 13.5*$u))
    $pen.Dispose()
}

# 12 Stack — 3 stacked horizontal plates
function Draw-Stack($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawRectangle($pen, [single]($ox + 3*$u), [single]($oy + 4*$u), [single](18*$u), [single](4*$u))
    $g.DrawRectangle($pen, [single]($ox + 3*$u), [single]($oy + 10*$u), [single](18*$u), [single](4*$u))
    $g.DrawRectangle($pen, [single]($ox + 3*$u), [single]($oy + 16*$u), [single](18*$u), [single](4*$u))
    $pen.Dispose()
}

# 13 Ground — horizon line + terrain bumps
function Draw-Ground($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawLine($pen, [single]($ox + 3*$u), [single]($oy + 18*$u), [single]($ox + 21*$u), [single]($oy + 18*$u))
    $pts = @(
        (New-Object System.Drawing.PointF([single]($ox + 5*$u), [single]($oy + 14*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 8*$u), [single]($oy + 11*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 12*$u), [single]($oy + 13*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 16*$u), [single]($oy + 9*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 20*$u), [single]($oy + 12*$u)))
    )
    $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)
    $pen.Dispose()
}

# 14 Assets — 3 small scattered cubes
function Draw-Assets($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawRectangle($pen, [single]($ox + 2*$u), [single]($oy + 10*$u), [single](6*$u), [single](6*$u))
    $g.DrawRectangle($pen, [single]($ox + 13*$u), [single]($oy + 5*$u), [single](6*$u), [single](6*$u))
    $g.DrawRectangle($pen, [single]($ox + 9*$u), [single]($oy + 14*$u), [single](6*$u), [single](6*$u))
    $pen.Dispose()
}

# 15 Physics — pendulum (vertical line + ball)
function Draw-Physics($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $g.DrawLine($pen, [single]($ox + 4*$u), [single]($oy + 4*$u), [single]($ox + 20*$u), [single]($oy + 4*$u))
    $g.DrawLine($pen, [single]($ox + 12*$u), [single]($oy + 4*$u), [single]($ox + 16*$u), [single]($oy + 17*$u))
    $g.DrawEllipse($pen, [single]($ox + 13*$u), [single]($oy + 16*$u), [single](6*$u), [single](6*$u))
    $pen.Dispose()
}

# 16 Scene — sun with 4 short rays
function Draw-Scene($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    $cx = $ox + 12*$u; $cy = $oy + 12*$u
    $g.DrawEllipse($pen, [single]($cx - 5*$u), [single]($cy - 5*$u), [single](10*$u), [single](10*$u))
    foreach ($ang in 0, 45, 90, 135, 180, 225, 270, 315) {
        $rad = $ang * [Math]::PI / 180.0
        $x1 = $cx + 7*$u * [Math]::Cos($rad);  $y1 = $cy + 7*$u * [Math]::Sin($rad)
        $x2 = $cx + 10*$u * [Math]::Cos($rad); $y2 = $cy + 10*$u * [Math]::Sin($rad)
        $g.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
    }
    $pen.Dispose()
}

# 17 Body — single isometric cube outline (asset, solo body)
function Draw-Body($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    # Top diamond + side panels
    $pts = @(
        (New-Object System.Drawing.PointF([single]($ox + 12*$u), [single]($oy + 4*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 20*$u), [single]($oy + 9*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 20*$u), [single]($oy + 17*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 12*$u), [single]($oy + 22*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 4*$u),  [single]($oy + 17*$u))),
        (New-Object System.Drawing.PointF([single]($ox + 4*$u),  [single]($oy + 9*$u)))
    )
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$pts)
    # Vertical fold
    $g.DrawLine($pen, [single]($ox + 12*$u), [single]($oy + 13*$u), [single]($ox + 12*$u), [single]($oy + 22*$u))
    $g.DrawLine($pen, [single]($ox + 12*$u), [single]($oy + 13*$u), [single]($ox + 20*$u), [single]($oy + 9*$u))
    $g.DrawLine($pen, [single]($ox + 12*$u), [single]($oy + 13*$u), [single]($ox + 4*$u), [single]($oy + 9*$u))
    $pen.Dispose()
}

# 18 Surface — texture/friction wave lines
function Draw-Surface($g, $ox, $oy, $s) {
    $pen = New-Pen $cInk (Stroke-Width $s)
    $u = $s / 24.0
    # Ground line
    $g.DrawLine($pen, [single]($ox + 3*$u), [single]($oy + 20*$u), [single]($ox + 21*$u), [single]($oy + 20*$u))
    # Friction hatches
    foreach ($x in 4..9) {
        $px = $ox + (2 + 3*$x*$u/2)
        $g.DrawLine($pen, [single]($ox + (3 + ($x-4)*3)*$u), [single]($oy + 17*$u),
                          [single]($ox + (5 + ($x-4)*3)*$u), [single]($oy + 14*$u))
    }
    $pen.Dispose()
}

$drawers = @(
    'Draw-ModeFlyout', 'Draw-Robot',  'Draw-World',     'Draw-AssetMode',
    'Draw-Coord',      'Draw-Preview','Draw-Export',
    'Draw-Links',      'Draw-Joints', 'Draw-Inertia',
    'Draw-Sensors',    'Draw-Actuation', 'Draw-Stack',
    'Draw-Ground',     'Draw-Assets', 'Draw-Physics',  'Draw-Scene',
    'Draw-Body',       'Draw-Surface'
)
$columns = $drawers.Count

foreach ($size in 20, 32, 40, 64, 96, 128) {
    # MainIconList glyph: the Mode flyout symbol (group glyph).
    $c = New-Canvas $size $size; $bmp = $c[0]; $g = $c[1]
    & 'Draw-ModeFlyout' $g 0 0 $size
    $g.Dispose()
    $f = Join-Path $outDir ("sw2gz_{0}.png" -f $size)
    $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "wrote $f"

    # Strip — one glyph per column.
    $c = New-Canvas ($columns * $size) $size; $bmp = $c[0]; $g = $c[1]
    for ($i = 0; $i -lt $columns; $i++) {
        & $drawers[$i] $g ($i * $size) 0 $size
    }
    $g.Dispose()
    $f = Join-Path $outDir ("sw2gz_strip_{0}.png" -f $size)
    $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "wrote $f"
}

Write-Host "done -> $outDir ($columns glyphs per strip)"
