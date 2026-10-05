$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../../src/Scrunch/Assets'
$source = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'scrunch-master.png'))
$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = @()
try { foreach ($size in $sizes) {
    $small=[Drawing.Bitmap]::new($size,$size)
    $g=[Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode='HighQualityBicubic'; $g.PixelOffsetMode='HighQuality'
    $g.DrawImage($source,0,0,$size,$size); $g.Dispose()
    $stream=[IO.MemoryStream]::new()
    $small.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray(); $stream.Dispose()
    $small.Save((Join-Path $PSScriptRoot "scrunch-$size.png"),[Drawing.Imaging.ImageFormat]::Png)
    $small.Dispose()
} } finally { $source.Dispose() }
$stream=[IO.File]::Create((Join-Path $assetDirectory 'Scrunch.ico'))
$writer=[IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for ($i=0;$i -lt $sizes.Count;$i++) {
        $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
} finally { $writer.Dispose(); $stream.Dispose() }
# Actual-size rows on light and dark backgrounds, plus enlarged 16px pixel preview.
$sheet=[Drawing.Bitmap]::new(560,210)
$g=[Drawing.Graphics]::FromImage($sheet)
$g.Clear([Drawing.Color]::FromArgb(246,246,243))
$brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(32,32,32))
$g.FillRectangle($brush,0,105,560,105);$brush.Dispose()
foreach($y in @(0,105)){
    $x=10
    foreach($size in @(16,20,24,32,48,64)){
        $img=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot "scrunch-$size.png"))
        $g.DrawImageUnscaled($img,$x,$y+15);$img.Dispose()
        $x += $size+18
    }
}
$img=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'scrunch-16.png'))
$g.InterpolationMode='NearestNeighbor';$g.PixelOffsetMode='Half';$g.DrawImage($img,440,25,96,96);$img.Dispose()
$g.Dispose();$sheet.Save((Join-Path $PSScriptRoot 'size-proof.png'));$sheet.Dispose()
