param([string]$Out = (Join-Path $PSScriptRoot "Axe.ico"))
# Draws Axe's icon (v2.8.0): a chamfered badge in the 深空蓝 cyan-to-blue gradient with an angular "A"
# cut out of it, the same drawing as the title-bar logo (Theme.LogoBadge / Theme.LogoA, 30-unit box).
# Renders every usual Windows size and packs the PNGs into one .ico.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$sizes = 256, 128, 64, 48, 40, 32, 24, 20, 16
$badge = @(@(6, 0), @(24, 0), @(30, 6), @(30, 24), @(24, 30), @(6, 30), @(0, 24), @(0, 6))
$aOuter = @(@(8, 23), @(13.2, 7), @(16.8, 7), @(22, 23), @(18.4, 23), @(17.3, 19.4), @(12.7, 19.4), @(11.6, 23))
$aHole = @(@(13.3, 16.6), @(16.7, 16.6), @(15, 10.8))
$tick = @(@(2.0, 7.4), @(7.4, 2.0), @(8.6, 3.2), @(3.2, 8.6))
# The leading comma keeps PowerShell from unrolling the array into Object[] on return.
function Pts($list, $k, $o) { return , [System.Drawing.PointF[]]($list | ForEach-Object { New-Object System.Drawing.PointF ($o + $_[0] * $k), ($o + $_[1] * $k) }) }
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $pad = [math]::Max(0.5, $s * 0.02)
    $k = ($s - 2 * $pad) / 30.0
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 34, 211, 238)), ([System.Drawing.Color]::FromArgb(255, 44, 125, 244)), 45.0
    $g.FillPolygon($brush, (Pts $badge $k $pad))
    # Dark inner edge so the badge holds on light and dark taskbars alike (not at the smallest sizes).
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 3, 18, 26)), ([math]::Max(1.0, $s / 64.0))
        $g.DrawPolygon($pen, (Pts $badge $k $pad))
    }
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 3, 18, 26))
    $g.FillPolygon($dark, (Pts $aOuter $k $pad))
    # The A's counter: the badge gradient painted back in.
    $g.FillPolygon($brush, (Pts $aHole $k $pad))
    if ($s -ge 40) { $g.FillPolygon($dark, (Pts $tick $k $pad)) }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [IO.File]::Create($Out)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write([byte[]]$p) }
$bw.Close()
Get-Item $Out | Select-Object FullName, Length
