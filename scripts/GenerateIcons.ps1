# Generates the SW2GZ ribbon icon set (square, transparent background) at the
# SOLIDWORKS-standard sizes. Design: a rounded-square badge with a SolidWorks
# blue -> ROS indigo vertical gradient and a white 2-link robot-arm glyph with a
# joint dot. Strong silhouette, no fine detail, so it stays crisp at 20px.
#
# Output: SW2GZ\UI\Resources\Icons\sw2gz_<size>.png
# Run:    powershell -ExecutionPolicy Bypass -File scripts\GenerateIcons.ps1

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$outDir = Join-Path $PSScriptRoot '..\SW2GZ\UI\Resources\Icons'
$outDir = [System.IO.Path]::GetFullPath($outDir)
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# SolidWorks blue -> ROS indigo.
$cTop    = [System.Drawing.Color]::FromArgb(255, 0x1B, 0x6E, 0xC2)  # #1B6EC2
$cBottom = [System.Drawing.Color]::FromArgb(255, 0x22, 0x31, 0x4E)  # #22314E
$white   = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)

function New-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- rounded-square badge ---
    $pad    = [Math]::Max(1.0, $size * 0.06)
    $rect   = New-Object System.Drawing.RectangleF($pad, $pad, ($size - 2*$pad), ($size - 2*$pad))
    $radius = $size * 0.22
    $d      = $radius * 2

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cTop, $cBottom, 90.0)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # --- white 2-link robot arm glyph ---
    # Geometry in a 0..1 unit square, scaled to the badge interior.
    $ix = $rect.X; $iy = $rect.Y; $iw = $rect.Width; $ih = $rect.Height
    function P([double]$ux, [double]$uy) {
        return New-Object System.Drawing.PointF(($ix + $ux*$iw), ($iy + $uy*$ih))
    }

    $linkW = [Math]::Max(1.6, $size * 0.085)
    $pen   = New-Object System.Drawing.Pen($white, $linkW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Base (bottom-left) -> elbow (mid) -> end-effector (upper-right).
    $base  = P 0.30 0.74
    $elbow = P 0.50 0.46
    $tip   = P 0.74 0.30
    $g.DrawLine($pen, $base, $elbow)
    $g.DrawLine($pen, $elbow, $tip)
    $pen.Dispose()

    $wb = New-Object System.Drawing.SolidBrush($white)

    # Base mount block.
    $baseR = $size * 0.11
    $g.FillEllipse($wb, ($base.X - $baseR), ($base.Y - $baseR), (2*$baseR), (2*$baseR))

    # Elbow joint dot (hollow look: white ring via blue inner).
    $jR = $size * 0.105
    $g.FillEllipse($wb, ($elbow.X - $jR), ($elbow.Y - $jR), (2*$jR), (2*$jR))
    $innerBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cTop, $cBottom, 90.0)
    $jIn = $jR * 0.45
    $g.FillEllipse($innerBrush, ($elbow.X - $jIn), ($elbow.Y - $jIn), (2*$jIn), (2*$jIn))
    $innerBrush.Dispose()

    # End-effector dot.
    $tR = $size * 0.095
    $g.FillEllipse($wb, ($tip.X - $tR), ($tip.Y - $tR), (2*$tR), (2*$tR))

    $wb.Dispose()
    $g.Dispose()

    $file = Join-Path $outDir ("sw2gz_{0}.png" -f $size)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $file"
}

foreach ($s in 20, 32, 40, 64, 96, 128) { New-Icon $s }
Write-Host "done -> $outDir"
