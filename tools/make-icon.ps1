# Draws the Mullion icon (a window divided by mullions, one pane selected) and
# writes it as a multi-size .ico. Run again only when the design changes:
#
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\Mullion\Assets\mullion.ico')
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$size

    # Blue rounded tile.
    $tile = New-RoundedPath 0 0 $s $s ($s * 0.22)
    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 37, 99, 235))
    $g.FillPath($blue, $tile)

    # The window: an outline with a cross of mullions.
    $inset = [single][Math]::Round($s * 0.20)
    $stroke = [single][Math]::Max(1, [Math]::Round($s * 0.085))
    $inner = $s - (2 * $inset)
    $half = $stroke / 2

    # The selected pane, top-left.
    $pane = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
    $g.FillRectangle($pane, $inset, $inset, $inner / 2, $inner / 2)

    $white = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $g.DrawRectangle($white, $inset + $half, $inset + $half, $inner - $stroke, $inner - $stroke)
    $mid = $s / 2
    $g.DrawLine($white, $mid, $inset, $mid, $s - $inset)
    $g.DrawLine($white, $inset, $mid, $s - $inset, $mid)

    $g.Dispose()
    return $bmp
}

# Small sizes are stored as plain bitmaps, which every icon reader understands;
# the 256-pixel size is stored as PNG to keep the file small.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)

    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)

    $w.Write([int]40)              # header size
    $w.Write([int]$size)           # width
    $w.Write([int]($size * 2))     # height: colour rows plus mask rows
    $w.Write([int16]1)             # planes
    $w.Write([int16]32)            # bits per pixel
    $w.Write([int]0)               # no compression
    $w.Write([int]($size * $size * 4 + $maskStride * $size))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)

    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }

    # The mask is all zero: transparency comes from the alpha channel.
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    return ,$ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($size in $sizes) {
    $bmp = New-Frame $size
    if ($size -ge 256) { $bytes = Get-PngBytes $bmp } else { $bytes = Get-DibBytes $bmp }
    $images += ,@{ Size = $size; Bytes = $bytes }
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([int16]0)
$writer.Write([int16]1)
$writer.Write([int16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]$image.Bytes.Length)
    $writer.Write([int]$offset)
    $offset += $image.Bytes.Length
}

foreach ($image in $images) {
    $writer.Write([byte[]]$image.Bytes)
}

$writer.Flush()
$target = [System.IO.Path]::GetFullPath($OutFile)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Wrote $target ($($out.Length) bytes, sizes: $($sizes -join ', '))"
