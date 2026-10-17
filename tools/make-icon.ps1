# Renders the PTZVC icon (the color logo mark on a light rounded tile) into a multi-size .ico file.
param(
    [string]$Mark = "$PSScriptRoot\..\src\assets\logo-mark-color.png",
    [string]$Out = "$PSScriptRoot\..\src\ptzvc.ico"
)

Add-Type -AssemblyName System.Drawing
$raw = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Mark))

# trim the transparent margins so the mark is centered exactly
function Opaque([int]$x, [int]$y) { $raw.GetPixel($x, $y).A -gt 8 }
$x0 = 0; while ($x0 -lt $raw.Width - 1 -and -not (0..($raw.Height - 1) | Where-Object { Opaque $x0 $_ } | Select-Object -First 1)) { $x0++ }
$x1 = $raw.Width - 1; while ($x1 -gt $x0 -and -not (0..($raw.Height - 1) | Where-Object { Opaque $x1 $_ } | Select-Object -First 1)) { $x1-- }
$y0 = 0; while ($y0 -lt $raw.Height - 1 -and -not ($x0..$x1 | Where-Object { Opaque $_ $y0 } | Select-Object -First 1)) { $y0++ }
$y1 = $raw.Height - 1; while ($y1 -gt $y0 -and -not ($x0..$x1 | Where-Object { Opaque $_ $y1 } | Select-Object -First 1)) { $y1-- }
$logo = $raw.Clone((New-Object System.Drawing.Rectangle $x0, $y0, ($x1 - $x0 + 1), ($y1 - $y0 + 1)), $raw.PixelFormat)
$raw.Dispose()

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # light rounded tile, so the grey mark reads on both light and dark taskbars
    $r = New-Object System.Drawing.RectangleF (6 * $s), (6 * $s), (244 * $s), (244 * $s)
    $d = 64 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $body = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, ([System.Drawing.Color]::FromArgb(250, 250, 252)), ([System.Drawing.Color]::FromArgb(214, 214, 219)), 90
    $g.FillPath($body, $path)
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 150, 156)), ([Math]::Max(1, 2 * $s))
    $g.DrawPath($edge, $path)

    # the mark, centered; a little larger at small sizes so the rings stay visible
    $fill = if ($size -le 24) { 0.86 } else { 0.78 }
    $box = 256 * $s * $fill
    $k = [Math]::Min($box / $logo.Width, $box / $logo.Height)
    $w = $logo.Width * $k; $h = $logo.Height * $k
    $g.DrawImage($logo, (New-Object System.Drawing.RectangleF (($size - $w) / 2), (($size - $h) / 2), $w, $h))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 256, 64, 48, 32, 24, 16
$images = @()
foreach ($sz in $sizes) { $images += ,(Render $sz) }
$logo.Dispose()

$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $b = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
Write-Host "Icon written to $Out"
