$ErrorActionPreference='Stop'
$assetPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment/Assets'
New-Item -ItemType Directory -Path $assetPath -Force | Out-Null
Add-Type -AssemblyName System.Drawing

# Windows resolves the notification-area icon at SM_CXSMICON: 16px at 100% DPI,
# 20px at 125%, 24px at 150% and 32px at 200%. Each entry is drawn at its own
# whole-pixel geometry so the shell selects a purpose-built bitmap instead of
# resampling one large one.
$IconSizes=@(16,20,24,32,40,48,64,128,256)
$TraySizes=@(16,20,24,32,40,48,64)
$AccentInk=@(0,95,184)
$AccentHole=@(255,255,255)
$DarkTaskbarInk=@(255,255,255)
$LightTaskbarInk=@(26,26,26)

function Get-Rounded([double]$value){ [int][Math]::Floor($value+0.5) }

# Draws the calendar mark as a straight-alpha BGRA buffer. The body is a solid
# mass with the day cells punched out rather than an outlined frame: at 16px a
# filled shape carries far more ink than a stroke, which is what keeps the icon
# readable against the taskbar.
function New-GlyphPixels {
    param(
        [int]$Canvas,
        [int]$Glyph,
        [int[]]$Ink,
        [int[]]$Hole   # $null punches the cells through to transparency
    )

    $pixels=[byte[]]::new($Canvas*$Canvas*4)
    $n=$Glyph
    $inset=[int][Math]::Floor(($Canvas-$n)/2)

    $radius=[Math]::Max(2,(Get-Rounded ($n*0.13)))
    $bodyTop=[Math]::Max(2,(Get-Rounded ($n*0.125)))
    $sideMargin=[Math]::Max(1,(Get-Rounded ($n*0.125)))
    $cell=[Math]::Max(2,(Get-Rounded ($n*0.14)))
    $gapX=[Math]::Max(1,[int][Math]::Floor((($n-2*$sideMargin)-3*$cell)/2))
    $gapY=[Math]::Max(1,(Get-Rounded ($n*0.125)))
    $bottomPad=[Math]::Max(1,(Get-Rounded ($n*0.125)))
    $gridW=3*$cell+2*$gapX
    $gridX=[int][Math]::Floor(($n-$gridW)/2)
    $gridH=2*$cell+$gapY
    $gridY=$n-$bottomPad-$gridH
    $tickW=[Math]::Max(2,(Get-Rounded ($n*0.125)))
    $tickX1=[Math]::Max(1,(Get-Rounded ($n*0.1875)))
    $tickX2=$n-$tickX1-$tickW
    $tickRadius=[Math]::Max(1,[int][Math]::Floor($tickW/3))
    # Hard edges below 32px stop the small entries going soft. Larger entries are
    # big enough that the corner arc needs coverage blending to read as a curve.
    $antialias=$n -ge 32

    $inkB=[byte]$Ink[2];$inkG=[byte]$Ink[1];$inkR=[byte]$Ink[0]

    for($y=$bodyTop;$y -lt $n;$y++){
        for($x=0;$x -lt $n;$x++){
            $alpha=1.0
            $cx=$null
            if($x -lt $radius){$cx=$radius}elseif($x -ge $n-$radius){$cx=$n-$radius}
            $cy=$null
            if($y -lt $bodyTop+$radius){$cy=$bodyTop+$radius}elseif($y -ge $n-$radius){$cy=$n-$radius}
            if($null -ne $cx -and $null -ne $cy){
                $dx=($x+0.5)-$cx
                $dy=($y+0.5)-$cy
                $distance=[Math]::Sqrt($dx*$dx+$dy*$dy)
                if($antialias){
                    $alpha=[Math]::Min(1.0,[Math]::Max(0.0,$radius+0.5-$distance))
                } else {
                    $alpha=$(if($distance -le $radius){1.0}else{0.0})
                }
            }
            if($alpha -gt 0){
                $index=(($y+$inset)*$Canvas+($x+$inset))*4
                $pixels[$index]=$inkB
                $pixels[$index+1]=$inkG
                $pixels[$index+2]=$inkR
                $pixels[$index+3]=[byte][Math]::Round($alpha*255)
            }
        }
    }

    # The two hangers are the only mark above the body, so they stay square at
    # the smallest sizes and only take a rounded cap once there are pixels for it.
    foreach($tickX in @($tickX1,$tickX2)){
        for($y=0;$y -lt $bodyTop;$y++){
            for($x=$tickX;$x -lt $tickX+$tickW;$x++){
                $alpha=1.0
                $cx=$null
                if($x -lt $tickX+$tickRadius){$cx=$tickX+$tickRadius}
                elseif($x -ge $tickX+$tickW-$tickRadius){$cx=$tickX+$tickW-$tickRadius}
                if($null -ne $cx -and $y -lt $tickRadius){
                    $dx=($x+0.5)-$cx
                    $dy=($y+0.5)-$tickRadius
                    $distance=[Math]::Sqrt($dx*$dx+$dy*$dy)
                    if($antialias){
                        $alpha=[Math]::Min(1.0,[Math]::Max(0.0,$tickRadius+0.5-$distance))
                    } else {
                        $alpha=$(if($distance -le $tickRadius){1.0}else{0.0})
                    }
                }
                if($alpha -gt 0){
                    $index=(($y+$inset)*$Canvas+($x+$inset))*4
                    $pixels[$index]=$inkB
                    $pixels[$index+1]=$inkG
                    $pixels[$index+2]=$inkR
                    $pixels[$index+3]=[byte][Math]::Round($alpha*255)
                }
            }
        }
    }

    for($row=0;$row -lt 2;$row++){
        for($col=0;$col -lt 3;$col++){
            $cellX=$gridX+$col*($cell+$gapX)
            $cellY=$gridY+$row*($cell+$gapY)
            for($y=$cellY;$y -lt $cellY+$cell;$y++){
                for($x=$cellX;$x -lt $cellX+$cell;$x++){
                    $index=(($y+$inset)*$Canvas+($x+$inset))*4
                    if($null -eq $Hole){
                        $pixels[$index]=0;$pixels[$index+1]=0;$pixels[$index+2]=0;$pixels[$index+3]=0
                    } else {
                        $pixels[$index]=[byte]$Hole[2]
                        $pixels[$index+1]=[byte]$Hole[1]
                        $pixels[$index+2]=[byte]$Hole[0]
                        $pixels[$index+3]=255
                    }
                }
            }
        }
    }

    return ,$pixels
}

function ConvertTo-PixelBitmap([byte[]]$Pixels,[int]$Size){
    $bitmap=[Drawing.Bitmap]::new($Size,$Size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $data=$bitmap.LockBits(
        [Drawing.Rectangle]::new(0,0,$Size,$Size),
        [Drawing.Imaging.ImageLockMode]::WriteOnly,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for($y=0;$y -lt $Size;$y++){
            [Runtime.InteropServices.Marshal]::Copy(
                $Pixels,$y*$Size*4,
                [IntPtr]::new($data.Scan0.ToInt64()+$y*$data.Stride),$Size*4)
        }
    } finally { $bitmap.UnlockBits($data) }
    return $bitmap
}

# A 32bpp icon image: BITMAPINFOHEADER, bottom-up BGRA rows, then the 1bpp AND
# mask. Modern Windows honours the alpha channel, but the mask is still derived
# from it so the entry stays correct wherever the legacy path is used.
function ConvertTo-IconDib([byte[]]$Pixels,[int]$Size){
    $maskStride=((($Size+31) -shr 5)*4)
    $stream=[IO.MemoryStream]::new()
    $writer=[IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint32]40)
        $writer.Write([int32]$Size)
        $writer.Write([int32]($Size*2))  # biHeight spans the colour rows and the mask
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)         # BI_RGB
        $writer.Write([uint32]($Size*$Size*4+$maskStride*$Size))
        $writer.Write([int32]0);$writer.Write([int32]0)
        $writer.Write([uint32]0);$writer.Write([uint32]0)
        for($y=$Size-1;$y -ge 0;$y--){ $writer.Write($Pixels,$y*$Size*4,$Size*4) }
        $maskRow=[byte[]]::new($maskStride)
        for($y=$Size-1;$y -ge 0;$y--){
            [Array]::Clear($maskRow,0,$maskStride)
            for($x=0;$x -lt $Size;$x++){
                if($Pixels[($y*$Size+$x)*4+3] -eq 0){
                    $maskRow[$x -shr 3]=[byte]($maskRow[$x -shr 3] -bor (0x80 -shr ($x -band 7)))
                }
            }
            $writer.Write($maskRow,0,$maskStride)
        }
        $writer.Flush()
        return ,$stream.ToArray()
    } finally { $writer.Dispose() }
}

# Notification-area art follows the light line weight of the Windows system
# glyphs: a hollow calendar, two binding strokes and just two short agenda lines.
# Draw each size separately; align straight strokes to the pixel grid.
function New-TrayGlyphPixels([int]$Size,[int[]]$Ink){
    $bitmap=[Drawing.Bitmap]::new($Size,$Size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $stroke=[Math]::Max(1,[int][Math]::Floor($Size/16))
    $pen=[Drawing.Pen]::new([Drawing.Color]::FromArgb(255,$Ink[0],$Ink[1],$Ink[2]),$stroke)
    $path=[Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        # Small tray glyphs need solid pixels, not antialiased half-opacity edges.
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::None
        $offset=(($stroke+1) % 2)*0.5
        $edge=[Math]::Floor(($stroke-1)/2)+$offset
        $left=$edge
        $right=$Size-1-$edge
        $top=(Get-Rounded ($Size*2/16))+$offset
        $bottom=$Size-1-$edge
        $radius=[Math]::Max(1,$Size/16)
        $diameter=2*$radius
        $path.AddArc([single]$left,[single]$top,[single]$diameter,[single]$diameter,180,90)
        $path.AddArc([single]($right-$diameter),[single]$top,[single]$diameter,[single]$diameter,270,90)
        $path.AddArc([single]($right-$diameter),[single]($bottom-$diameter),[single]$diameter,[single]$diameter,0,90)
        $path.AddArc([single]$left,[single]($bottom-$diameter),[single]$diameter,[single]$diameter,90,90)
        $path.CloseFigure()
        $graphics.DrawPath($pen,$path)
        $header=(Get-Rounded ($Size*5/16))+$offset
        $graphics.DrawLine($pen,[single]$left,[single]$header,[single]$right,[single]$header)
        foreach($binding in @(4,11)){
            $x=(Get-Rounded ($Size*$binding/16))+$offset
            $graphics.DrawLine($pen,[single]$x,[single]$edge,[single]$x,[single]((Get-Rounded ($Size*3/16))+$offset))
        }
        foreach($line in @(@(9,11),@(12,8))){
            $y=(Get-Rounded ($Size*$line[0]/16))+$offset
            $graphics.DrawLine($pen,[single]((Get-Rounded ($Size*5/16))+$offset),[single]$y,[single]((Get-Rounded ($Size*$line[1]/16))+$offset),[single]$y)
        }
        $pixels=[byte[]]::new($Size*$Size*4)
        $data=$bitmap.LockBits([Drawing.Rectangle]::new(0,0,$Size,$Size),[Drawing.Imaging.ImageLockMode]::ReadOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for($y=0;$y -lt $Size;$y++){
                [Runtime.InteropServices.Marshal]::Copy([IntPtr]::new($data.Scan0.ToInt64()+$y*$data.Stride),$pixels,$y*$Size*4,$Size*4)
            }
        } finally { $bitmap.UnlockBits($data) }
        return ,$pixels
    } finally { $path.Dispose();$pen.Dispose();$graphics.Dispose();$bitmap.Dispose() }
}

function New-AppLogoPixels([int]$Canvas,[int]$Glyph){
    $masterPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/branding/agenda-master.png'
    $master=[Drawing.Bitmap]::new($masterPath)
    $bitmap=[Drawing.Bitmap]::new($Canvas,$Canvas,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $inset=[int][Math]::Floor(($Canvas-$Glyph)/2)
        $graphics.DrawImage($master,[Drawing.Rectangle]::new($inset,$inset,$Glyph,$Glyph),0,0,$master.Width,$master.Height,[Drawing.GraphicsUnit]::Pixel)
        $pixels=[byte[]]::new($Canvas*$Canvas*4)
        $data=$bitmap.LockBits([Drawing.Rectangle]::new(0,0,$Canvas,$Canvas),[Drawing.Imaging.ImageLockMode]::ReadOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for($y=0;$y -lt $Canvas;$y++){
                [Runtime.InteropServices.Marshal]::Copy([IntPtr]::new($data.Scan0.ToInt64()+$y*$data.Stride),$pixels,$y*$Canvas*4,$Canvas*4)
            }
        } finally { $bitmap.UnlockBits($data) }
        return ,$pixels
    } finally { $graphics.Dispose();$bitmap.Dispose();$master.Dispose() }
}

function Write-IconFile([string]$Path,[int[]]$Sizes,[int[]]$Ink,[int[]]$Hole,[switch]$Outline){
    $payloads=@()
    foreach($size in $Sizes){
        $pixels=if($Outline){New-TrayGlyphPixels $size $Ink}else{New-AppLogoPixels $size $size}
        if($size -ge 256){
            # Only the 256px entry is PNG compressed; every smaller entry stays an
            # uncompressed DIB, matching the layout Windows' own icons ship.
            $bitmap=ConvertTo-PixelBitmap $pixels $size
            $buffer=[IO.MemoryStream]::new()
            try {
                $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
                $payloads+=,$buffer.ToArray()
            } finally { $buffer.Dispose(); $bitmap.Dispose() }
        } else {
            $payloads+=,(ConvertTo-IconDib $pixels $size)
        }
    }
    $stream=[IO.File]::Create($Path)
    $writer=[IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$Sizes.Count)
        $offset=6+16*$Sizes.Count
        for($i=0;$i -lt $Sizes.Count;$i++){
            # 256 is recorded as 0 because the directory dimension is a single byte.
            $dimension=[byte]$(if($Sizes[$i] -ge 256){0}else{$Sizes[$i]})
            $writer.Write($dimension);$writer.Write($dimension)
            $writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([uint16]1);$writer.Write([uint16]32)
            $writer.Write([uint32]$payloads[$i].Length)
            $writer.Write([uint32]$offset)
            $offset+=$payloads[$i].Length
        }
        foreach($payload in $payloads){ $writer.Write($payload,0,$payload.Length) }
    } finally { $writer.Dispose() }
}

function Write-LogoPng([string]$Name,[int]$Canvas,[int]$Glyph){
    $pixels=New-AppLogoPixels $Canvas $Glyph
    $bitmap=ConvertTo-PixelBitmap $pixels $Canvas
    try {
        $bitmap.Save((Join-Path $assetPath $Name),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

Write-LogoPng 'Square44x44Logo.png' 44 44
Write-LogoPng 'Square150x150Logo.png' 150 100  # medium tiles keep the mark clear of the tile edge
Write-LogoPng 'StoreLogo.png' 50 50

# Explorer and package logos are resized from the generated artwork master.
Write-IconFile (Join-Path $assetPath 'Agenda.ico') $IconSizes $AccentInk $null

# The notification area is painted in the system theme and a static HICON cannot
# follow it, so both inks ship and ShellHost picks and swaps between them.
Write-IconFile (Join-Path $assetPath 'TrayDark.ico') $TraySizes $DarkTaskbarInk $null -Outline
Write-IconFile (Join-Path $assetPath 'TrayLight.ico') $TraySizes $LightTaskbarInk $null -Outline

Write-Output "Assets written to $assetPath"
