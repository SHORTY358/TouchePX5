[CmdletBinding()]
param([switch]$Perspective, [switch]$TextureUv, [switch]$TextureSelect, [switch]$Texture, [switch]$Depth, [switch]$Scissor, [switch]$Blend, [ValidateSet(0,16,32)][int]$IndexBits=0)
$ErrorActionPreference = 'Stop'
$root = 'C:\Dev\TouchePX5\artifacts\smoke-triangle'
if($Perspective) { $TextureUv=$true }
if($TextureUv) {
    if($Scissor -or $IndexBits) { throw 'UV oracle currently requires the ordinary triangle.' }
    $TextureSelect=$true
}
if($TextureSelect) { $Texture=$true }
if($Texture) { $root += '-texture' }
if($TextureSelect) { $root += '-select' }
if($TextureUv) { $root += '-uv' }
if($Perspective) { $root += '-perspective' }
if($Texture -and ($Depth -or $Blend)) { throw 'Texture currently requires a separate scenario.' }
if($Depth) { $root += '-depth' }
if($Depth -and $Blend) { throw 'Depth and Blend must be tested separately.' }
if($Blend) { $root += '-blend' }
if($Scissor) { $root += '-scissor' }
if($IndexBits) { $root += "-index$IndexBits" }
New-Item -ItemType Directory -Path $root -Force | Out-Null
Remove-Item -LiteralPath (Join-Path $root 'verification.json') -Force -ErrorAction SilentlyContinue
$fixture = Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$clang = 'C:\BuildTools\VC\Tools\Llvm\x64\bin\clang.exe'
$defines=@()
if($Texture) { $defines += '-DTEST_TEXTURE=1' }
if($TextureSelect) { $defines += '-DTEST_TEXTURE_SELECT=1' }
if($TextureUv) { $defines += '-DTEST_TEXTURE_UV=1' }
if($Perspective) { $defines += '-DTEST_PERSPECTIVE=1' }
if($Depth) { $defines += '-DTEST_DEPTH=1' }
if($Blend) { $defines += '-DTEST_BLEND=1' }
if($IndexBits) { $defines += "-DINDEX_BITS=$IndexBits" }
if($Scissor) { $defines += '-DTEST_SCISSOR=1' }
& $clang --target=x86_64-pc-linux-gnu -O1 -ffreestanding -fno-builtin -fno-stack-protector -fpie -mno-sse @defines -c (Join-Path $fixture 'triangle.c') -o (Join-Path $root 'triangle.o')
if ($LASTEXITCODE -ne 0) { throw 'Triangle guest compilation failed.' }
& $clang --target=x86_64-pc-linux-gnu -c (Join-Path $fixture 'imports.S') -o (Join-Path $root 'imports.o')
if ($LASTEXITCODE -ne 0) { throw 'Triangle imports compilation failed.' }
$guest = Join-Path $root 'triangle.elf'
& 'C:\BuildTools\VC\Tools\Llvm\x64\bin\ld.lld.exe' -T (Join-Path $PSScriptRoot 'fixtures\ps5-touche-input-demo\demo.ld') -o $guest (Join-Path $root 'triangle.o') (Join-Path $root 'imports.o')
if ($LASTEXITCODE -ne 0) { throw 'Triangle guest link failed.' }
$bytes = [IO.File]::ReadAllBytes($guest)
$bytes[7]=9; $bytes[8]=2; $bytes[16]=0x10; $bytes[17]=0xfe
[IO.File]::WriteAllBytes($guest,$bytes)
$log = Join-Path $root 'triangle.log'
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Write-Output "Log: $log"
$settings = @{
    SHARPEMU_LOG_AGC='1'
    SHARPEMU_TRACE_GUEST_IMAGES='present'
    SHARPEMU_GUEST_IMAGE_DUMP_DIR=(Join-Path $root ('gpu-' + [DateTime]::UtcNow.Ticks))
    SHARPEMU_GUEST_IMAGE_DUMP_CONTINUOUS='1'
    SHARPEMU_OVERLAY='0'
}
$previous = @{}
try {
    foreach($name in $settings.Keys) {
        $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
        [Environment]::SetEnvironmentVariable($name,$settings[$name],'Process')
    }
    & 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe' '--cpu-engine=native' '--fps-limit=60' '--log-level=info' "--log-file=$log" $guest
    if ($LASTEXITCODE -ne 0) { throw "Triangle launch failed: $log" }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $output = if (Test-Path -LiteralPath $log) { '' + (Get-Content -LiteralPath $log -Raw) } else { '' }
        if ($output.Contains('TouchePx5 execution completed.')) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $output.Contains('[RUNTIME] DispatchEntry returned: ORBIS_GEN2_OK')) { throw "Triangle guest failed: $log" }
    if ($output.Contains('agc.shader_color_clear')) { throw 'Triangle unexpectedly became a fullscreen clear.' }
    $captures = [regex]::Matches($output, 'vk.swapchain_dump path=([^\r\n]+)')
    $expectedDraws=if($Depth) { 4 } elseif($Texture) { 3 } else { 2 }
    $expectedCaptures=if($Depth -or $Texture) { 4 } else { 3 }
    if($captures.Count -ne $expectedCaptures) { throw "Unexpected number of swapchain captures: $log" }
    $textureCount=if($Texture) { 1 } else { 0 }
    if([regex]::Matches($output,"vk.offscreen_draw mrt=1 size=320x180 textures=$textureCount").Count -ne $expectedDraws -or $output.Contains('agc.shader_translate_miss')) { throw 'Missing expected successful translated raster draws.' }
    if(-not ('TrianglePixelVerifierV5' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class TrianglePixelVerifierV5 {
    public static int Check(byte[] data, int width, int height, bool blue, bool scissor, bool indexed, bool blend) {
        if (data.Length != checked(width * height * 4)) throw new Exception("Invalid capture size");
        int redCount = 0;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int p=(y*width+x)*4;
            int channel=blue ? 0 : 2, other=blue ? 2 : 0;
            bool red=data[p+other]==0 && data[p+1]==0 && data[p+channel]==255;
            bool black=data[p]==0 && data[p+1]==0 && data[p+2]==0;
            if(blue && blend) {
                red=data[p]>=127 && data[p]<=128 && data[p+2]>=127 && data[p+2]<=128 && data[p+3]==255;
                if(data[p+1]!=0 || Math.Abs(data[p]-data[p+2])>1 || Math.Abs(data[p+3]-2*data[p+2])>2) throw new Exception("Blend color mismatch at " + x + "," + y);
            } else if(data[p+other]!=0 || data[p+1]!=0 || data[p+3]!=data[p+channel]) throw new Exception("Unexpected color at " + x + "," + y);
            if(red) redCount++;
            double sx=(x+0.5)*320/width, sy=(y+0.5)*180/height;
            if(indexed) sx=320-sx;
            double diagonal=(sx-40)/240.0+(sy-22.5)/135.0;
            // Non-integer GPU presentation uses bilinear filtering. Require exact colors away from the two-source-pixel edge band.
            bool inside=sx>42 && sy>24.5 && diagonal<0.98;
            bool outside=sx<38 || sy<20.5 || diagonal>1.02;
            if(scissor) {
                inside=inside && sx>82 && sx<238 && sy>47 && sy<133;
                outside=outside || sx<78 || sx>242 || sy<43 || sy>137;
            }
            if(inside && !red || outside && !black) throw new Exception("Triangle coverage mismatch at " + x + "," + y);
        }
        double min=scissor ? 0.115 : 0.27, max=scissor ? 0.135 : 0.30;
        if(redCount < width*(long)height*min || redCount > width*(long)height*max) throw new Exception("Wrong triangle area");
        return redCount;
    }
}
"@
    }
    $verifiedFrames = @()
    if($TextureUv -and -not ('TextureUvVerifierV2' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class TextureUvVerifierV2 {
    public static int Check(byte[] data,int width,int height,bool swap,bool perspective) {
        if(data.Length!=checked(width*height*4)) throw new Exception("Invalid capture size");
        int count=0, red=0, green=0, blue=0;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int p=(y*width+x)*4;
            double sx=(x+0.5)*320/width,sy=(y+0.5)*180/height;
            double diagonal=(sx-40)/240+(sy-22.5)/135;
            bool inside=sx>42 && sy>24.5 && diagonal<0.98;
            bool outside=sx<38 || sy<20.5 || diagonal>1.02;
            if(data[p+3]==255) count++;
            if(outside && (data[p]!=0 || data[p+1]!=0 || data[p+2]!=0 || data[p+3]!=0)) throw new Exception("UV outside coverage");
            double u=(sx-40)/240, v=(sy-22.5)/135;
            if(perspective) { double reciprocalW=1-u*0.5; u=u*0.5/reciprocalW; v=v/reciprocalW; }
            if(!inside || Math.Abs(u-0.5)<0.02 || Math.Abs(v-0.5)<0.02) continue;
            int channel=v>0.5 ? 1 : u>0.5 ? 0 : 2;
            if(swap && channel!=1) channel=2-channel;
            if(data[p+3]!=255 || data[p+channel]!=255 || data[p+(channel+1)%3]!=0 || data[p+(channel+2)%3]!=0)
                throw new Exception("UV color mismatch at "+x+","+y+" source="+sx+","+sy+" channel="+channel);
            if(channel==0) blue++; else if(channel==1) green++; else red++;
        }
        if(count<width*(long)height*0.27 || count>width*(long)height*0.30 || red<100 || green<100 || blue<100) throw new Exception("UV area or color region missing");
        return count;
    }
}
"@
    }
    Add-Type -AssemblyName System.Drawing
    foreach($frameIndex in 1..($captures.Count-1)) {
    $expectBlue=($frameIndex -eq ($captures.Count-1)) -or ($Texture -and $frameIndex -ge 2)
    $capture = $captures[$frameIndex].Groups[1].Value.Trim()
    $dimensions = [regex]::Match([IO.Path]::GetFileName($capture), '^present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm\.bgra$')
    if(-not $dimensions.Success) { throw 'Unsupported triangle capture format.' }
    $width=[int]$dimensions.Groups[1].Value; $height=[int]$dimensions.Groups[2].Value
    $gpuBytes=[IO.File]::ReadAllBytes($capture)
    if($TextureUv) {
        $coloredPixels=[TextureUvVerifierV2]::Check($gpuBytes,$width,$height,$false,[bool]$Perspective)
        if($frameIndex -eq 1) {
            $rejectedBlank=$false; $rejectedWrongColor=$false
            try { [TextureUvVerifierV2]::Check([byte[]]::new(320*180*4),320,180,$false,[bool]$Perspective) | Out-Null } catch { $rejectedBlank=$true }
            try { [TextureUvVerifierV2]::Check($gpuBytes,$width,$height,$true,[bool]$Perspective) | Out-Null } catch { $rejectedWrongColor=$true }
            if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'UV verifier accepted a negative control.' }
            if($Perspective) {
                $rejectedAffine=$false
                try { [TextureUvVerifierV2]::Check($gpuBytes,$width,$height,$false,$false) | Out-Null } catch { $rejectedAffine=$true }
                if(-not $rejectedAffine) { throw 'Perspective output also passed the incorrect affine oracle.' }
            }
        }
    } else {
        $coloredPixels=[TrianglePixelVerifierV5]::Check($gpuBytes,$width,$height,$expectBlue,[bool]$Scissor,($IndexBits -ne 0),[bool]$Blend)
    }
    if($frameIndex -eq 1 -and -not $TextureUv) {
        $rejectedBlank=$false; $rejectedWrongColor=$false
        try { [TrianglePixelVerifierV5]::Check([byte[]]::new(320*180*4),320,180,$false,[bool]$Scissor,($IndexBits -ne 0),[bool]$Blend) | Out-Null } catch { $rejectedBlank=$true }
        try { [TrianglePixelVerifierV5]::Check($gpuBytes,$width,$height,$true,[bool]$Scissor,($IndexBits -ne 0),[bool]$Blend) | Out-Null } catch { $rejectedWrongColor=$true }
        if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'Triangle verifier accepted a negative control.' }
    }
    $color=if($TextureUv) { 'textured' } elseif($expectBlue -and $Blend) { 'purple' } elseif($expectBlue) { 'blue' } else { 'red' }
    Write-Output "PASS: $color GPU triangle verified across $($width*$height) pixels ($coloredPixels colored)."
    $bitmap=[Drawing.Bitmap]::new($width,$height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $locked=$bitmap.LockBits([Drawing.Rectangle]::new(0,0,$width,$height),[Drawing.Imaging.ImageLockMode]::WriteOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try { [Runtime.InteropServices.Marshal]::Copy($gpuBytes,0,$locked.Scan0,$gpuBytes.Length) }
        finally { $bitmap.UnlockBits($locked) }
        $bitmap.Save((Join-Path $root "triangle-$frameIndex-$color.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    Write-Output "Captured GPU output: $capture"
    $verifiedFrames += [ordered]@{ Color=$color; Width=$width; Height=$height; ColoredPixels=$coloredPixels; Capture=$capture; Sha256=(Get-FileHash -LiteralPath $capture).Hash }
    }
    $report = [ordered]@{
        Status='Passed'; CompletedUtc=[DateTime]::UtcNow.ToString('o'); Frames=$verifiedFrames; Scissor=[bool]$Scissor; IndexBits=$IndexBits; Blend=[bool]$Blend; Depth=[bool]$Depth
        Texture=[bool]$Texture
        TextureSelect=[bool]$TextureSelect
        TextureUv=[bool]$TextureUv
        Perspective=[bool]$Perspective
        ExecutableSha256=(Get-FileHash -LiteralPath 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe').Hash
        GuestSha256=(Get-FileHash -LiteralPath $guest).Hash
        SourceSha256=(Get-FileHash -LiteralPath (Join-Path $fixture 'triangle.c')).Hash
        RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
        NegativeControls='Blank frame and incorrect expected color rejected'
        AffineOracleRejected=[bool]$Perspective
        Log=$log
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'verification.json') -Encoding utf8
}
finally {
    foreach($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
}
