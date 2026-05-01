param(
    [Parameter(Mandatory=$true)] [string] $InputPng,
    [Parameter(Mandatory=$true)] [string] $OutputIco,
    [int[]] $Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $InputPng))
try {
    $images = @()
    foreach ($s in $Sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode    = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode        = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode      = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality   = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.DrawImage($src, 0, 0, $s, $s)
        } finally { $g.Dispose() }

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += ,@{ Size = $s; Bytes = $ms.ToArray() }
        $bmp.Dispose()
        $ms.Dispose()
    }
} finally { $src.Dispose() }

$ico = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter($ico)
$bw.Write([uint16]0)             # reserved
$bw.Write([uint16]1)             # type = ICO
$bw.Write([uint16]$images.Count) # image count

$offset = 6 + ($images.Count * 16)
foreach ($img in $images) {
    $w = if ($img.Size -ge 256) { 0 } else { [byte]$img.Size }
    $h = if ($img.Size -ge 256) { 0 } else { [byte]$img.Size }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)             # color count (0 = >256 colors)
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # color planes
    $bw.Write([uint16]32)          # bits per pixel
    $bw.Write([uint32]$img.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write($img.Bytes) }

[IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $OutputIco)).Path + "\" + (Split-Path $OutputIco -Leaf), $ico.ToArray())
"wrote $OutputIco ($((Get-Item $OutputIco).Length) bytes, $($images.Count) sizes)"
