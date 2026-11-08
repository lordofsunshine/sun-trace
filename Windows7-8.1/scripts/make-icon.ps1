param(
    [string]$Source = (Join-Path $PSScriptRoot "..\icon.png"),
    [string]$Output = (Join-Path $PSScriptRoot "..\src\SunTrace.App\Assets\SunTrace.ico")
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$sourcePath = [System.IO.Path]::GetFullPath($Source)
if (-not (Test-Path $sourcePath)) {
    throw "Не найден исходный файл иконки: $sourcePath"
}

$sourceBitmap = [System.Drawing.Bitmap]::FromFile($sourcePath)
$master = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.Clear([System.Drawing.Color]::Transparent)
$scale = [Math]::Max(256.0 / $sourceBitmap.Width, 256.0 / $sourceBitmap.Height)
$width = [int][Math]::Ceiling($sourceBitmap.Width * $scale)
$height = [int][Math]::Ceiling($sourceBitmap.Height * $scale)
$left = [int][Math]::Floor((256 - $width) / 2.0)
$top = [int][Math]::Floor((256 - $height) / 2.0)
$graphics.DrawImage($sourceBitmap, $left, $top, $width, $height)

$graphics.Dispose()
$sourceBitmap.Dispose()

$frames = @()
foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $target = [System.Drawing.Graphics]::FromImage($bitmap)
    $target.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $target.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $target.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $target.Clear([System.Drawing.Color]::Transparent)
    $target.DrawImage($master, 0, 0, $size, $size)
    $target.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $size; Data = $stream.ToArray() }
    $bitmap.Dispose()
    $stream.Dispose()
}
$pngPath = [System.IO.Path]::ChangeExtension($Output, ".png")
$directory = Split-Path -Parent $Output
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}
$master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$master.Dispose()

$file = [System.IO.File]::Create($Output)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $size = [byte]($frame.Size % 256)
    $writer.Write($size)
    $writer.Write($size)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) {
    $writer.Write($frame.Data)
}
$writer.Dispose()
$file.Dispose()

Write-Output ("Icon written: {0} ({1} bytes)" -f $Output, (Get-Item $Output).Length)
