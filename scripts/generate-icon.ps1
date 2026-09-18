param([string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\orbit.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16,24,32,48,64,128,256)
$outDir = Split-Path -Parent $Output
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$images = foreach($size in $sizes) {
  $bitmap = New-Object System.Drawing.Bitmap($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bitmap)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = $size / 256.0
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    try {
      $radius = [Math]::Max(2,[int](52*$scale));$tile.AddArc(0,0,$radius*2,$radius*2,180,90);$tile.AddArc($size-$radius*2,0,$radius*2,$radius*2,270,90);$tile.AddArc($size-$radius*2,$size-$radius*2,$radius*2,$radius*2,0,90);$tile.AddArc(0,$size-$radius*2,$radius*2,$radius*2,90,90);$tile.CloseFigure()
      $tileBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,21,23,28));$g.FillPath($tileBrush,$tile);$tileBrush.Dispose()
    } finally { $tile.Dispose() }
    $pen1 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,169,149,223)),(12*$scale)
    $pen2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(184,130,115,174)),(8*$scale)
    $pen1.StartCap = [System.Drawing.Drawing2D.LineCap]::Round;$pen1.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round;$pen2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.TranslateTransform(128*$scale,128*$scale);$g.RotateTransform(-38);$g.DrawEllipse($pen1,-94*$scale,-54*$scale,188*$scale,108*$scale);$g.ResetTransform()
    $g.TranslateTransform(128*$scale,128*$scale);$g.RotateTransform(40);$g.DrawEllipse($pen2,-85*$scale,-61*$scale,170*$scale,122*$scale);$g.ResetTransform()
    $coreBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,184,161,237));$holeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,21,23,28));$satelliteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,214,197,255))
    $g.FillEllipse($coreBrush,100*$scale,100*$scale,56*$scale,56*$scale);$g.FillEllipse($holeBrush,115*$scale,115*$scale,26*$scale,26*$scale);$g.FillEllipse($satelliteBrush,180*$scale,56*$scale,20*$scale,20*$scale)
    $pen1.Dispose();$pen2.Dispose();$coreBrush.Dispose();$holeBrush.Dispose();$satelliteBrush.Dispose()
  } finally { $g.Dispose() }
  $bitmap
}
try {
  $images[$images.Count-1].Save((Join-Path $outDir 'orbit-256.png'),[System.Drawing.Imaging.ImageFormat]::Png)
  $stream = [System.IO.File]::Open($Output,[System.IO.FileMode]::Create,[System.IO.FileAccess]::Write)
  $writer = New-Object System.IO.BinaryWriter $stream
  try {
    $writer.Write([UInt16]0);$writer.Write([UInt16]1);$writer.Write([UInt16]$images.Count)
    $offset = 6 + 16*$images.Count; $entries = @()
    foreach($bitmap in $images) {
      $width=$bitmap.Width;$height=$bitmap.Height;$xorBytes=$width*$height*4;$maskStride=([Math]::Ceiling($width/32.0)*4);$bytes=40+$xorBytes+$maskStride*$height
      $entries += ,@($width,$height,$bytes,$offset);$offset += $bytes
    }
    foreach($entry in $entries) {$writer.Write([byte]($entry[0] -band 255));$writer.Write([byte]($entry[1] -band 255));$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([UInt16]1);$writer.Write([UInt16]32);$writer.Write([UInt32]$entry[2]);$writer.Write([UInt32]$entry[3])}
    foreach($bitmap in $images) {
      $width=$bitmap.Width;$height=$bitmap.Height;$maskStride=([Math]::Ceiling($width/32.0)*4)
      $writer.Write([UInt32]40);$writer.Write([Int32]$width);$writer.Write([Int32]($height*2));$writer.Write([UInt16]1);$writer.Write([UInt16]32);$writer.Write([UInt32]0);$writer.Write([UInt32]($width*$height*4));$writer.Write([Int32]0);$writer.Write([Int32]0);$writer.Write([UInt32]0);$writer.Write([UInt32]0)
      for($y=$height-1;$y-ge 0;$y--){for($x=0;$x-lt $width;$x++){ $color=$bitmap.GetPixel($x,$y);$writer.Write($color.B);$writer.Write($color.G);$writer.Write($color.R);$writer.Write($color.A) }}
      $writer.Write((New-Object byte[] ($maskStride*$height)))
    }
  } finally { $writer.Dispose() }
} finally { foreach($bitmap in $images){$bitmap.Dispose()} }
