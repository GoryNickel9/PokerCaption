param([Parameter(Mandatory = $true)][string]$OutputPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $radius
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$palette = @{
    Background = [System.Drawing.Color]::FromArgb(46, 37, 87)
    BackCard = [System.Drawing.Color]::FromArgb(183, 166, 251)
    FrontCard = [System.Drawing.Color]::FromArgb(248, 245, 255)
    Suit = [System.Drawing.Color]::FromArgb(94, 68, 176)
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object System.IO.MemoryStream
    $background = New-RoundedPath 1 1 62 62 15
    $backCard = New-RoundedPath 15 14 24 33 4
    $frontCard = New-RoundedPath 28 17 24 33 4
    $backgroundBrush = New-Object System.Drawing.SolidBrush $palette.Background
    $backBrush = New-Object System.Drawing.SolidBrush $palette.BackCard
    $frontBrush = New-Object System.Drawing.SolidBrush $palette.FrontCard
    $suitBrush = New-Object System.Drawing.SolidBrush $palette.Suit
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 64.0, $size / 64.0)
        $graphics.FillPath($backgroundBrush, $background)
        $state = $graphics.Save()
        $graphics.TranslateTransform(26, 31)
        $graphics.RotateTransform(-13)
        $graphics.TranslateTransform(-26, -31)
        $graphics.FillPath($backBrush, $backCard)
        $graphics.Restore($state)
        $graphics.FillPath($frontBrush, $frontCard)
        $diamond = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(40, 24), [System.Drawing.PointF]::new(47, 33.5),
            [System.Drawing.PointF]::new(40, 43), [System.Drawing.PointF]::new(33, 33.5)
        )
        $graphics.FillPolygon($suitBrush, $diamond)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        ,($stream.ToArray())
    } finally {
        foreach ($resource in @($suitBrush, $frontBrush, $backBrush, $backgroundBrush, $frontCard,
            $backCard, $background, $stream, $graphics, $bitmap)) { $resource.Dispose() }
    }
}

$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = [byte]($sizes[$i] % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
    $writer.Flush()
    $temp = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllBytes($temp, $stream.ToArray())
    if ([IO.File]::Exists($output)) { [IO.File]::Replace($temp, $output, $null) }
    else { [IO.File]::Move($temp, $output) }
} finally {
    $writer.Dispose()
}
