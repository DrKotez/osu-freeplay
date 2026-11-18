# Draws freeplay~'s icon (a trans flag circle with a tilde) into an .ico.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts/make-icon.ps1 -Output osu.Desktop/freeplay.ico

param([string]$Output)

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $margin = [Math]::Max(1, [int]($size * 0.04))
    $d = $size - 2 * $margin

    # Flag stripes clipped to a circle.
    $circle = New-Object System.Drawing.Drawing2D.GraphicsPath
    $circle.AddEllipse($margin, $margin, $d, $d)
    $g.SetClip($circle)

    $colours = @('#5bcefa', '#f5a9b8', '#ffffff', '#f5a9b8', '#5bcefa')
    $stripe = $d / 5.0
    for ($i = 0; $i -lt 5; $i++) {
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($colours[$i]))
        $g.FillRectangle($brush, [single]$margin, [single]($margin + $i * $stripe), [single]$d, [single]($stripe + 1))
        $brush.Dispose()
    }

    $g.ResetClip()

    # Dark ring, so the icon reads on light and dark backgrounds.
    $ring = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#17121f')), ([single]([Math]::Max(1, $size * 0.06)))
    $g.DrawEllipse($ring, [single]($margin + $ring.Width / 2), [single]($margin + $ring.Width / 2), [single]($d - $ring.Width), [single]($d - $ring.Width))
    $ring.Dispose()

    # A tilde in the middle (freeplay~), on larger sizes only where it stays legible.
    if ($size -ge 32) {
        $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * 0.55)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF 0, ([single](-$size * 0.06)), $size, $size

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddString('~', $font.FontFamily, [int]$font.Style, $font.Size, $rect, $format)

        $outline = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#17121f')), ([single]($size * 0.05))
        $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($outline, $path)
        $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#e2789a'))
        $g.FillPath($fill, $path)

        $outline.Dispose(); $fill.Dispose(); $path.Dispose(); $font.Dispose()
    }

    $g.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$stream.ToArray()
}

$sizes = @(256, 128, 64, 48, 32, 24, 16)
$images = @()
foreach ($s in $sizes) { $images += ,(New-IconPng $s) }

# An .ico holding PNG images (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()

[System.IO.File]::WriteAllBytes($Output, $out.ToArray())

# A preview, to look at the result.
[System.IO.File]::WriteAllBytes([System.IO.Path]::ChangeExtension($Output, '.png'), $images[0])
Write-Host "Wrote $Output"
