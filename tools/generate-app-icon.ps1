[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    throw "generate-app-icon.ps1 requires Windows."
}

try {
    Add-Type -AssemblyName System.Drawing
}
catch {
    throw ("Failed to load System.Drawing: {0}" -f $_.Exception.Message)
}

function New-RoundedRectanglePath {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.RectangleF]$Rect,

        [Parameter(Mandatory = $true)]
        [float]$Radius
    )

    $diameter = [Math]::Min($Radius * 2, [Math]::Min($Rect.Width, $Rect.Height))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath

    if ($diameter -le 0) {
        $path.AddRectangle($Rect)
        return $path
    }

    $arc = [System.Drawing.RectangleF]::new($Rect.X, $Rect.Y, $diameter, $diameter)
    $path.AddArc($arc, 180, 90)
    $arc.X = $Rect.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Rect.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $Rect.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()

    return $path
}

function Draw-RemoteInstallerIcon {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $canvas = [float]$Size
        $isTiny = $Size -le 24
        $isSmall = ($Size -ge 32 -and $Size -le 48)
        $isLarge = $Size -ge 64
        $showHighlight = $Size -ge 128
        $showIndicators = $Size -ge 64

        $outerMargin = if ($isTiny) { $canvas * 0.05 } else { $canvas * 0.08 }
        $baseRect = [System.Drawing.RectangleF]::new($outerMargin, $outerMargin, ($canvas - ($outerMargin * 2)), ($canvas - ($outerMargin * 2)))
        $baseRadius = if ($isTiny) { $canvas * 0.18 } else { $canvas * 0.20 }
        $basePath = New-RoundedRectanglePath -Rect $baseRect -Radius $baseRadius

        try {
            $baseBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                ([System.Drawing.PointF]::new($baseRect.Left, $baseRect.Top)),
                ([System.Drawing.PointF]::new($baseRect.Right, $baseRect.Bottom)),
                [System.Drawing.Color]::FromArgb(255, 24, 111, 230),
                [System.Drawing.Color]::FromArgb(255, 8, 57, 156)
            )
            $graphics.FillPath($baseBrush, $basePath)
            $baseBrush.Dispose()

            if ($showHighlight) {
                $highlightRect = [System.Drawing.RectangleF]::new(($baseRect.X + ($canvas * 0.04)), ($baseRect.Y + ($canvas * 0.04)), ($baseRect.Width * 0.72), ($baseRect.Height * 0.28))
                $highlightPath = New-RoundedRectanglePath -Rect $highlightRect -Radius ($canvas * 0.12)
                try {
                    $highlightBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                        ([System.Drawing.PointF]::new($highlightRect.Left, $highlightRect.Top)),
                        ([System.Drawing.PointF]::new($highlightRect.Left, $highlightRect.Bottom)),
                        [System.Drawing.Color]::FromArgb(36, 255, 255, 255),
                        [System.Drawing.Color]::FromArgb(0, 255, 255, 255)
                    )
                    $graphics.FillPath($highlightBrush, $highlightPath)
                    $highlightBrush.Dispose()
                }
                finally {
                    $highlightPath.Dispose()
                }
            }

            $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(96, 255, 255, 255), [Math]::Max(1.0, $canvas * 0.012))
            $graphics.DrawPath($borderPen, $basePath)
            $borderPen.Dispose()
        }
        finally {
            $basePath.Dispose()
        }

        if ($isTiny) {
            $cabinetRect = [System.Drawing.RectangleF]::new(($canvas * 0.20), ($canvas * 0.18), ($canvas * 0.32), ($canvas * 0.64))
            $slotRects = @(
                [System.Drawing.RectangleF]::new(($canvas * 0.25), ($canvas * 0.36), ($canvas * 0.20), ($canvas * 0.09)),
                [System.Drawing.RectangleF]::new(($canvas * 0.25), ($canvas * 0.51), ($canvas * 0.20), ($canvas * 0.09))
            )
            $terminalPoints = [System.Drawing.PointF[]]@(
                ([System.Drawing.PointF]::new(($canvas * 0.74), ($canvas * 0.35))),
                ([System.Drawing.PointF]::new(($canvas * 0.57), ($canvas * 0.50))),
                ([System.Drawing.PointF]::new(($canvas * 0.74), ($canvas * 0.65)))
            )
            $terminalLineStart = [System.Drawing.PointF]::new(($canvas * 0.74), ($canvas * 0.68))
            $terminalLineEnd = [System.Drawing.PointF]::new(($canvas * 0.84), ($canvas * 0.68))
            $slotColor = [System.Drawing.Color]::FromArgb(255, 92, 153, 238)
            $terminalWidth = [Math]::Max(2.0, $canvas * 0.09)
        }
        elseif ($isSmall) {
            $cabinetRect = [System.Drawing.RectangleF]::new(($canvas * 0.22), ($canvas * 0.18), ($canvas * 0.31), ($canvas * 0.63))
            $slotRects = @(
                [System.Drawing.RectangleF]::new(($canvas * 0.27), ($canvas * 0.31), ($canvas * 0.20), ($canvas * 0.07)),
                [System.Drawing.RectangleF]::new(($canvas * 0.27), ($canvas * 0.45), ($canvas * 0.20), ($canvas * 0.07)),
                [System.Drawing.RectangleF]::new(($canvas * 0.27), ($canvas * 0.59), ($canvas * 0.20), ($canvas * 0.07))
            )
            $terminalPoints = [System.Drawing.PointF[]]@(
                ([System.Drawing.PointF]::new(($canvas * 0.76), ($canvas * 0.33))),
                ([System.Drawing.PointF]::new(($canvas * 0.58), ($canvas * 0.49))),
                ([System.Drawing.PointF]::new(($canvas * 0.76), ($canvas * 0.65)))
            )
            $terminalLineStart = [System.Drawing.PointF]::new(($canvas * 0.73), ($canvas * 0.67))
            $terminalLineEnd = [System.Drawing.PointF]::new(($canvas * 0.85), ($canvas * 0.67))
            $slotColor = [System.Drawing.Color]::FromArgb(255, 84, 145, 232)
            $terminalWidth = [Math]::Max(1.8, $canvas * 0.075)
        }
        else {
            $cabinetRect = [System.Drawing.RectangleF]::new(($canvas * 0.24), ($canvas * 0.18), ($canvas * 0.31), ($canvas * 0.63))
            $slotRects = @(
                [System.Drawing.RectangleF]::new(($canvas * 0.29), ($canvas * 0.29), ($canvas * 0.20), ($canvas * 0.055)),
                [System.Drawing.RectangleF]::new(($canvas * 0.29), ($canvas * 0.41), ($canvas * 0.20), ($canvas * 0.055)),
                [System.Drawing.RectangleF]::new(($canvas * 0.29), ($canvas * 0.53), ($canvas * 0.20), ($canvas * 0.055)),
                [System.Drawing.RectangleF]::new(($canvas * 0.29), ($canvas * 0.65), ($canvas * 0.20), ($canvas * 0.055))
            )
            $terminalPoints = [System.Drawing.PointF[]]@(
                ([System.Drawing.PointF]::new(($canvas * 0.76), ($canvas * 0.32))),
                ([System.Drawing.PointF]::new(($canvas * 0.58), ($canvas * 0.49))),
                ([System.Drawing.PointF]::new(($canvas * 0.76), ($canvas * 0.66)))
            )
            $terminalLineStart = [System.Drawing.PointF]::new(($canvas * 0.73), ($canvas * 0.67))
            $terminalLineEnd = [System.Drawing.PointF]::new(($canvas * 0.86), ($canvas * 0.67))
            $slotColor = [System.Drawing.Color]::FromArgb(255, 74, 132, 222)
            $terminalWidth = [Math]::Max(1.6, $canvas * 0.055)
        }

        $cabinetPath = New-RoundedRectanglePath -Rect $cabinetRect -Radius ($canvas * 0.05)
        try {
            $cabinetBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                ([System.Drawing.PointF]::new($cabinetRect.Left, $cabinetRect.Top)),
                ([System.Drawing.PointF]::new($cabinetRect.Right, $cabinetRect.Bottom)),
                [System.Drawing.Color]::FromArgb(255, 245, 248, 255),
                [System.Drawing.Color]::FromArgb(255, 210, 226, 255)
            )
            $graphics.FillPath($cabinetBrush, $cabinetPath)
            $cabinetBrush.Dispose()

            $cabinetPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(148, 26, 78, 176), [Math]::Max(1.0, $canvas * 0.012))
            $graphics.DrawPath($cabinetPen, $cabinetPath)
            $cabinetPen.Dispose()
        }
        finally {
            $cabinetPath.Dispose()
        }

        foreach ($slotRect in $slotRects) {
            $slotPath = New-RoundedRectanglePath -Rect $slotRect -Radius ([Math]::Max(1.0, $slotRect.Height * 0.45))
            try {
                $slotBrush = New-Object System.Drawing.SolidBrush($slotColor)
                $graphics.FillPath($slotBrush, $slotPath)
                $slotBrush.Dispose()
            }
            finally {
                $slotPath.Dispose()
            }

            if ($showIndicators) {
                $indicatorSize = [Math]::Max(2.0, $canvas * 0.02)
                $indicatorRect = [System.Drawing.RectangleF]::new(($cabinetRect.Right - ($canvas * 0.055)), ($slotRect.Y + (($slotRect.Height - $indicatorSize) / 2)), $indicatorSize, $indicatorSize)
                $indicatorBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180, 38, 229, 184))
                $graphics.FillEllipse($indicatorBrush, $indicatorRect)
                $indicatorBrush.Dispose()
            }
        }

        $terminalPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $terminalWidth)
        $terminalPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $terminalPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLines($terminalPen, $terminalPoints)
        $graphics.DrawLine($terminalPen, $terminalLineStart, $terminalLineEnd)
        $terminalPen.Dispose()

        return $bitmap
    }
    catch {
        $bitmap.Dispose()
        throw
    }
    finally {
        $graphics.Dispose()
    }
}

function Get-PngBytes {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap]$Bitmap
    )

    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function Write-MultiSizeIco {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [hashtable]$FrameMap
    )

    $sizes = $FrameMap.Keys | Sort-Object
    $fileStream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = New-Object System.IO.BinaryWriter($fileStream)

    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        foreach ($size in $sizes) {
            $pngBytes = $FrameMap[$size]
            $dimensionByte = if ($size -eq 256) { 0 } else { [byte]$size }
            $writer.Write([byte]$dimensionByte)
            $writer.Write([byte]$dimensionByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$pngBytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $pngBytes.Length
        }

        foreach ($size in $sizes) {
            $writer.Write($FrameMap[$size])
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }
}

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptPath
$brandDirectory = Join-Path $repoRoot 'RemoteInstaller/Assets/Brand'

if (-not (Test-Path -LiteralPath $brandDirectory -PathType Container)) {
    New-Item -Path $brandDirectory -ItemType Directory -Force | Out-Null
}

$pngPath = Join-Path $brandDirectory 'remoteinstaller-icon-256.png'
$icoPath = Join-Path $brandDirectory 'remoteinstaller-icon.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frameMap = @{}

foreach ($size in $sizes) {
    $bitmap = Draw-RemoteInstallerIcon -Size $size
    try {
        if ($size -eq 256) {
            $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }

        $frameMap[$size] = Get-PngBytes -Bitmap $bitmap
    }
    finally {
        $bitmap.Dispose()
    }
}

Write-MultiSizeIco -OutputPath $icoPath -FrameMap $frameMap

Write-Output ('Generated PNG: {0}' -f $pngPath)
Write-Output ('Generated ICO: {0}' -f $icoPath)
Write-Output ('ICO sizes: {0}' -f (($sizes | ForEach-Object { $_.ToString() }) -join ', '))
