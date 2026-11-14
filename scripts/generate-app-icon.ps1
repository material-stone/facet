param()

# Render the small, repo-owned SVG with Windows' existing WPF vector renderer.
# The source deliberately uses only rectangles, paths and linear gradients.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'impls/Facet/Design/AppIcon'
[xml]$svg = Get-Content -LiteralPath (Join-Path $assetRoot 'Facet.svg') -Raw

function Get-Paint([string]$value) {
    if ($value -match '^url\(#(.+)\)$') {
        $gradient = $svg.SelectSingleNode("//*[local-name()='linearGradient' and @id='$($Matches[1])']")
        if ($null -eq $gradient) { throw "Missing SVG gradient: $value" }
        $brush = [Windows.Media.LinearGradientBrush]::new()
        $brush.StartPoint = [Windows.Point]::new([double]$gradient.x1, [double]$gradient.y1)
        $brush.EndPoint = [Windows.Point]::new([double]$gradient.x2, [double]$gradient.y2)
        foreach ($stop in $gradient.ChildNodes) {
            $color = [Windows.Media.ColorConverter]::ConvertFromString($stop.GetAttribute('stop-color'))
            $brush.GradientStops.Add([Windows.Media.GradientStop]::new($color, [double]$stop.offset))
        }
        return $brush
    }
    return [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($value))
}

function Render-Icon([int]$size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0))
        foreach ($element in $svg.DocumentElement.ChildNodes) {
            switch ($element.LocalName) {
                'title' { }
                'defs' { }
                'rect' {
                    $bounds = [Windows.Rect]::new([double]$element.x, [double]$element.y, [double]$element.width, [double]$element.height)
                    $drawing.DrawRoundedRectangle((Get-Paint $element.fill), $null, $bounds, [double]$element.rx, [double]$element.rx)
                }
                'path' { $drawing.DrawGeometry((Get-Paint $element.fill), $null, [Windows.Media.Geometry]::Parse($element.d)) }
                default { throw "Unsupported SVG element: $($element.LocalName)" }
            }
        }
        $drawing.Pop()
    } finally { $drawing.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() } finally { $stream.Dispose() }
}

[IO.File]::WriteAllBytes((Join-Path $assetRoot 'Facet.png'), (Render-Icon 512))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @($sizes | ForEach-Object { ,(Render-Icon $_) })
$output = [IO.File]::Create((Join-Path $assetRoot 'Facet.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $side = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$side); $writer.Write([byte]$side)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output "Generated Facet.png and Facet.ico ($($sizes -join ', ') px) from Facet.svg."
