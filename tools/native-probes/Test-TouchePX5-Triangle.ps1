[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'C:\Dev\TouchePX5\artifacts\smoke-triangle'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$fixture = Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$clang = 'C:\BuildTools\VC\Tools\Llvm\x64\bin\clang.exe'
& $clang --target=x86_64-pc-linux-gnu -O1 -ffreestanding -fno-builtin -fno-stack-protector -fpie -mno-sse -c (Join-Path $fixture 'triangle.c') -o (Join-Path $root 'triangle.o')
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
    if($captures.Count -ne 2) { throw "Expected two swapchain captures: $log" }
    $capture = $captures[1].Groups[1].Value.Trim()
    $dimensions = [regex]::Match([IO.Path]::GetFileName($capture), '^present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm\.bgra$')
    if(-not $dimensions.Success) { throw 'Unsupported triangle capture format.' }
    $width=[int]$dimensions.Groups[1].Value; $height=[int]$dimensions.Groups[2].Value
    $gpuBytes=[IO.File]::ReadAllBytes($capture)
    if(-not $output.Contains('vk.offscreen_draw mrt=1 size=320x180 textures=0') -or $output.Contains('agc.shader_translate_miss')) { throw 'Missing successful translated raster draw.' }
    if(-not ('TrianglePixelVerifier' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class TrianglePixelVerifier {
    public static int Check(byte[] data, int width, int height) {
        if (data.Length != checked(width * height * 4)) throw new Exception("Invalid capture size");
        int redCount = 0;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int p=(y*width+x)*4;
            bool red=data[p]==0 && data[p+1]==0 && data[p+2]==255;
            bool black=data[p]==0 && data[p+1]==0 && data[p+2]==0;
            if(data[p]!=0 || data[p+1]!=0 || data[p+3]!=data[p+2]) throw new Exception("Unexpected color at " + x + "," + y);
            if(red) redCount++;
            double sx=(x+0.5)*320/width, sy=(y+0.5)*180/height;
            double diagonal=(sx-40)/240.0+(sy-22.5)/135.0;
            // Non-integer GPU presentation uses bilinear filtering. Require exact colors away from the two-source-pixel edge band.
            bool inside=sx>42 && sy>24.5 && diagonal<0.98;
            bool outside=sx<38 || sy<20.5 || diagonal>1.02;
            if(inside && !red || outside && !black) throw new Exception("Triangle coverage mismatch at " + x + "," + y);
        }
        if(redCount < width*(long)height*0.27 || redCount > width*(long)height*0.30) throw new Exception("Wrong triangle area");
        return redCount;
    }
}
"@
    }
    $redPixels=[TrianglePixelVerifier]::Check($gpuBytes,$width,$height)
    Write-Output "PASS: GPU triangle shape and colors verified across $($width*$height) pixels ($redPixels red)."
    Add-Type -AssemblyName System.Drawing
    $bitmap=[Drawing.Bitmap]::new($width,$height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $locked=$bitmap.LockBits([Drawing.Rectangle]::new(0,0,$width,$height),[Drawing.Imaging.ImageLockMode]::WriteOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try { [Runtime.InteropServices.Marshal]::Copy($gpuBytes,0,$locked.Scan0,$gpuBytes.Length) }
        finally { $bitmap.UnlockBits($locked) }
        $bitmap.Save((Join-Path $root 'triangle.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    Write-Output "Captured GPU output: $capture"
}
finally {
    foreach($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
}
