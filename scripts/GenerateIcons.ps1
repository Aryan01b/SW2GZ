# Generates the SW2GZ ribbon icon set (square, transparent background) at the
# SOLIDWORKS-standard sizes by resizing the source design asset
# assets\create-model.png. Aspect ratio is preserved and the image is centered
# on a transparent square canvas, with high-quality bicubic scaling.
#
# Source: assets\create-model.png   (edit this file to change the ribbon icon)
# Output: SW2GZ\UI\Resources\Icons\sw2gz_<size>.png
#         (the .csproj copies these to bin\<cfg>\images next to the DLL, where
#          Sw2gzIconList() resolves them at runtime)
# Run:    powershell -ExecutionPolicy Bypass -File scripts\GenerateIcons.ps1

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$src = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets\create-model.png'))
if (-not (Test-Path $src)) { throw "Source icon not found: $src" }

$outDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\SW2GZ\UI\Resources\Icons'))
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$img = [System.Drawing.Image]::FromFile($src)
try {
    foreach ($size in 20, 32, 40, 64, 96, 128) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g   = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        # Preserve aspect ratio; center on the square canvas.
        $scale = [Math]::Min($size / $img.Width, $size / $img.Height)
        $w = [int]($img.Width * $scale)
        $h = [int]($img.Height * $scale)
        $x = [int](($size - $w) / 2)
        $y = [int](($size - $h) / 2)
        $g.DrawImage($img, $x, $y, $w, $h)
        $g.Dispose()

        $file = Join-Path $outDir ("sw2gz_{0}.png" -f $size)
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "wrote $file"
    }
}
finally {
    $img.Dispose()
}
Write-Host "done -> $outDir  (source: $src)"
