# Generates Plettro/Assets/plettro.ico (a guitar pick with a waveform) using System.Drawing.
param([string]$Out = "$PSScriptRoot\..\Plettro\Assets\plettro.ico")
Add-Type -AssemblyName System.Drawing

function New-PickPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Guitar pick: rounded top, pointed bottom
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier(128*$s, 244*$s, 70*$s, 190*$s, 14*$s, 120*$s, 22*$s, 64*$s)
    $path.AddBezier(22*$s, 64*$s, 30*$s, 10*$s, 226*$s, 10*$s, 234*$s, 64*$s)
    $path.AddBezier(234*$s, 64*$s, 242*$s, 120*$s, 186*$s, 190*$s, 128*$s, 244*$s)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255,255,170,80)), ([System.Drawing.Color]::FromArgb(255,230,90,30)), 90
    $g.FillPath($brush, $path)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,150,50,10)), ([Math]::Max(1, 6*$s))
    $g.DrawPath($pen, $path)

    # Waveform
    $wpen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, 14*$s))
    $wpen.StartCap = 'Round'; $wpen.EndCap = 'Round'; $wpen.LineJoin = 'Round'
    $pts = @()
    for ($i = 0; $i -le 40; $i++) {
        $x = 58 + $i * 3.5
        $y = 100 + [Math]::Sin($i / 40.0 * 2 * [Math]::PI * 1.5) * 26 * [Math]::Sin($i / 40.0 * [Math]::PI)
        $pts += New-Object System.Drawing.PointF ($x*$s), ($y*$s)
    }
    $g.DrawLines($wpen, [System.Drawing.PointF[]]$pts)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 256, 64, 48, 32, 16
$pngs = foreach ($sz in $sizes) { ,(New-PickPng $sz) }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out)).Path + "\" + (Split-Path $Out -Leaf))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $pngs[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($d in $pngs) { $bw.Write([byte[]]$d) }
$bw.Close()
Write-Output "Icon written to $Out"
