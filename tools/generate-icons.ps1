param()

$ErrorActionPreference = 'Continue'

function Ensure-Dir($path) {
  if (-not (Test-Path $path)) { New-Item -ItemType Directory -Path $path | Out-Null }
}

function New-Bitmap($w,$h) {
  return New-Object System.Drawing.Bitmap($w,$h)
}

function Draw-SessionIcon($bmp) {
  $URBlue = [System.Drawing.Color]::FromArgb(0x82,0xB2,0xC9)
  $Dark   = [System.Drawing.Color]::FromArgb(0x5A,0x5C,0x59)
  $Mid    = [System.Drawing.Color]::FromArgb(0x8F,0x8F,0x8C)
  $Light  = [System.Drawing.Color]::FromArgb(0xD0,0xD2,0xD0)

  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $bodyFill = New-Object System.Drawing.SolidBrush($Light)
    $bodyOutline = New-Object System.Drawing.Pen($Dark, 2.0)
    $bodyOutline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $prong = New-Object System.Drawing.Pen($Mid, 2.0)
    $cord = New-Object System.Drawing.Pen($URBlue, 2.2)

    $rect = New-Object System.Drawing.Rectangle(7,9,10,8)
    $g.FillRectangle($bodyFill, $rect)
    $g.DrawRectangle($bodyOutline, $rect)
    $g.DrawLine($prong, 10,7,10,9)
    $g.DrawLine($prong, 14,7,14,9)
    $g.DrawArc($cord, 5,14,14,8, 20,140)
  }
  finally { $g.Dispose() }
}

function Draw-ReadIcon($bmp) {
  $URBlue = [System.Drawing.Color]::FromArgb(0x82,0xB2,0xC9)
  $Dark   = [System.Drawing.Color]::FromArgb(0x5A,0x5C,0x59)
  $Mid    = [System.Drawing.Color]::FromArgb(0x8F,0x8F,0x8C)

  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $outline = New-Object System.Drawing.Pen($Dark, 2.0)
    $iris = New-Object System.Drawing.SolidBrush($URBlue)
    $shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(80, $Mid))

    $g.DrawEllipse($outline, 3,7,18,10)
    $g.FillEllipse($shadow, 6,9,12,6)
    $g.FillEllipse($iris, 10,10,4,4)
  }
  finally { $g.Dispose() }
}

function Draw-CommandIcon($bmp) {
  $URBlue = [System.Drawing.Color]::FromArgb(0x82,0xB2,0xC9)
  $Mid    = [System.Drawing.Color]::FromArgb(0x8F,0x8F,0x8C)

  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(90, $Mid))
    $primary = New-Object System.Drawing.SolidBrush($URBlue)

    $triShadow = @( (New-Object System.Drawing.Point(9,7)), (New-Object System.Drawing.Point(19,12)), (New-Object System.Drawing.Point(9,17)) )
    $tri = @( (New-Object System.Drawing.Point(8,6)), (New-Object System.Drawing.Point(18,12)), (New-Object System.Drawing.Point(8,18)) )
    $g.FillPolygon($shadow, $triShadow)
    $g.FillPolygon($primary, $tri)
  }
  finally { $g.Dispose() }
}

[void][System.Reflection.Assembly]::LoadWithPartialName('System.Drawing')

$outDir = Join-Path $PSScriptRoot '..\Resources\Icons'
Ensure-Dir $outDir

$targets = @(
  @{ Name='eye-duotone.png'; Drawer = { param($bmp) Draw-ReadIcon $bmp } },
  @{ Name='plug-duotone.png'; Drawer = { param($bmp) Draw-SessionIcon $bmp } },
  @{ Name='play-duotone.png'; Drawer = { param($bmp) Draw-CommandIcon $bmp } }
)

foreach ($t in $targets) {
  $path = Join-Path $outDir $t.Name
  if (Test-Path $path) { continue }
  $bmp = New-Bitmap 24 24
  try {
    & $t.Drawer $bmp
    try { $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) } catch { }
  }
  finally { $bmp.Dispose() }
}

Write-Host "Generated duotone icon PNGs in $outDir (if missing)."
