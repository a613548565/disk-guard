# Generate crisp multi-size PNG-in-ICO icon for DiskGuard.exe and DiskGuard-Setup-6.0.exe
param([string]$OutPath = "E:\code\disk-guard\assets\DiskGuard.ico")
Add-Type -AssemblyName System.Drawing

$dir = Split-Path -Parent $OutPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

function Render-IconPng([int]$sz) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]($sz * 0.04)
    $w = [float]($sz - $pad * 2)
    $rad = [float]($w * 0.25)
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $gp.AddArc($pad, $pad, $d, $d, 180, 90)
    $gp.AddArc($pad + $w - $d, $pad, $d, $d, 270, 90)
    $gp.AddArc($pad + $w - $d, $pad + $w - $d, $d, $d, 0, 90)
    $gp.AddArc($pad, $pad + $w - $d, $d, $d, 90, 90)
    $gp.CloseFigure()

    $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($sz, $sz)),
        [System.Drawing.Color]::FromArgb(255, 14, 165, 233),
        [System.Drawing.Color]::FromArgb(255, 2, 132, 199))
    $g.FillPath($lg, $gp)

    $stroke = [Math]::Max(1.6, $sz * 0.075)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $sh = New-Object System.Drawing.Drawing2D.GraphicsPath
    $sh.AddLine([float]($sz*0.50), [float]($sz*0.18), [float]($sz*0.78), [float]($sz*0.29))
    $sh.AddLine([float]($sz*0.78), [float]($sz*0.29), [float]($sz*0.74), [float]($sz*0.60))
    $sh.AddLine([float]($sz*0.74), [float]($sz*0.60), [float]($sz*0.50), [float]($sz*0.82))
    $sh.AddLine([float]($sz*0.50), [float]($sz*0.82), [float]($sz*0.26), [float]($sz*0.60))
    $sh.AddLine([float]($sz*0.26), [float]($sz*0.60), [float]($sz*0.22), [float]($sz*0.29))
    $sh.CloseFigure()
    $g.DrawPath($pen, $sh)

    $g.DrawArc($pen, [float]($sz*0.37), [float]($sz*0.36), [float]($sz*0.26), [float]($sz*0.26), -60, 260)
    $g.FillEllipse([System.Drawing.Brushes]::White, [float]($sz*0.46), [float]($sz*0.45), [float]($sz*0.08), [float]($sz*0.08))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose(); $pen.Dispose(); $sh.Dispose(); $lg.Dispose(); $gp.Dispose(); $g.Dispose(); $bmp.Dispose()
    return ,$bytes
}

$sizes = @(16, 32, 48, 64, 128)
$pngs = @()
foreach ($s in $sizes) { $pngs += ,(Render-IconPng $s) }

$fs = [System.IO.File]::Create($OutPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR header
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $data = $pngs[$i]
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Output ("ICON_OK=" + $OutPath + "  Size=" + (Get-Item $OutPath).Length)
