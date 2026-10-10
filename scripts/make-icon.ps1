# Draws the Claude Code mascot from the same block characters the menu uses and
# saves it as assets/icon.ico (PNG images 16..256 px).

Add-Type -AssemblyName System.Drawing
Set-Location (Split-Path $PSScriptRoot)

$rows = @(' ▐▛███▜▌ ', '▝▜█████▛▘', '  ▘▘ ▝▝  ')

# Filled quadrants of each block character: upper-left, upper-right, lower-left, lower-right.
$quadrants = @{
    ' ' = 0, 0, 0, 0; '▘' = 1, 0, 0, 0; '▝' = 0, 1, 0, 0; '▖' = 0, 0, 1, 0; '▗' = 0, 0, 0, 1
    '▀' = 1, 1, 0, 0; '▄' = 0, 0, 1, 1; '▌' = 1, 0, 1, 0; '▐' = 0, 1, 0, 1
    '▛' = 1, 1, 1, 0; '▜' = 1, 1, 0, 1; '▙' = 1, 0, 1, 1; '▟' = 0, 1, 1, 1; '█' = 1, 1, 1, 1
}

# A terminal cell is twice as tall as wide, so one quadrant is 1 unit wide and 2 units tall.
$columns = $rows[0].Length * 2
$unit = [Math]::Floor(256 / $columns)
$big = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($big)
$g.Clear([System.Drawing.Color]::Transparent)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 215, 119, 87))
$left = [Math]::Floor((256 - $columns * $unit) / 2)
$top = [Math]::Floor((256 - $rows.Count * 4 * $unit) / 2)
for ($r = 0; $r -lt $rows.Count; $r++) {
    for ($c = 0; $c -lt $rows[$r].Length; $c++) {
        $q = $quadrants[[string]$rows[$r][$c]]
        for ($k = 0; $k -lt 4; $k++) {
            if (-not $q[$k]) { continue }
            $x = $left + ($c * 2 + $k % 2) * $unit
            $y = $top + ($r * 2 + [Math]::Floor($k / 2)) * 2 * $unit
            $g.FillRectangle($brush, $x, $y, $unit, 2 * $unit)
        }
    }
}
$g.Dispose()

$images = foreach ($size in 256, 64, 48, 32, 24, 16) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($big, 0, 0, $size, $size)
    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    [pscustomobject]@{ Size = $size; Data = $stream.ToArray() }
}
$big.Dispose()

# ICO: header, one directory entry per image, then the PNG data.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $side = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $w.Write([byte]$side); $w.Write([byte]$side); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$image.Data.Length); $w.Write([UInt32]$offset)
    $offset += $image.Data.Length
}
foreach ($image in $images) { $w.Write($image.Data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path (Split-Path $PSScriptRoot) 'assets\icon.ico'), $out.ToArray())
