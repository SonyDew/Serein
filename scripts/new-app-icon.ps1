$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Serein.Desktop/Assets/Serein.ico'))
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($iconPath)) | Out-Null
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $edge = [single]($size - 2); $radius = [single]($size * .45)
    $path.AddArc([single]1,[single]1,$radius,$radius,[single]180,[single]90)
    $path.AddArc([single]($edge-$radius),[single]1,$radius,$radius,[single]270,[single]90)
    $path.AddArc([single]($edge-$radius),[single]($edge-$radius),$radius,$radius,[single]0,[single]90)
    $path.AddArc([single]1,[single]($edge-$radius),$radius,$radius,[single]90,[single]90); $path.CloseFigure()
    $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#8B9FFF')); $ink = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#0B1020'))
    $graphics.FillPath($background,$path)
    $font = [Drawing.Font]::new('Segoe UI',[single]($size*.65),[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new(); $format.Alignment='Center'; $format.LineAlignment='Center'
    $graphics.DrawString('S',$font,$ink,[Drawing.RectangleF]::new(0,-$size*.035,$size,$size),$format)
    $buffer = [IO.MemoryStream]::new(); $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png); $frames += ,@($size,$buffer.ToArray())
    $buffer.Dispose(); $format.Dispose(); $font.Dispose(); $ink.Dispose(); $background.Dispose(); $path.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create($iconPath); $writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count); $offset = 6 + 16*$frames.Count
    foreach ($frame in $frames) { $dimension = if ($frame[0] -eq 256) {0} else {$frame[0]}; $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset); $offset += $frame[1].Length }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output "Original Serein monogram icon generated: $iconPath"
