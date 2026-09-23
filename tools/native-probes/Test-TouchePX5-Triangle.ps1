[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'C:\Dev\TouchePX5\artifacts\smoke-triangle'
New-Item -ItemType Directory -Path $root -Force | Out-Null
Remove-Item -LiteralPath (Join-Path $root 'verification.json') -Force -ErrorAction SilentlyContinue
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
    if($captures.Count -ne 3) { throw "Expected three swapchain captures: $log" }
    if([regex]::Matches($output,'vk.offscreen_draw mrt=1 size=320x180 textures=0').Count -ne 2 -or $output.Contains('agc.shader_translate_miss')) { throw 'Missing two successful translated raster draws.' }
    if(-not ('TrianglePixelVerifierV2' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class TrianglePixelVerifierV2 {
    public static int Check(byte[] data, int width, int height, bool blue) {
        if (data.Length != checked(width * height * 4)) throw new Exception("Invalid capture size");
        int redCount = 0;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int p=(y*width+x)*4;
            int channel=blue ? 0 : 2, other=blue ? 2 : 0;
            bool red=data[p+other]==0 && data[p+1]==0 && data[p+channel]==255;
            bool black=data[p]==0 && data[p+1]==0 && data[p+2]==0;
            if(data[p+other]!=0 || data[p+1]!=0 || data[p+3]!=data[p+channel]) throw new Exception("Unexpected color at " + x + "," + y);
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
    $verifiedFrames = @()
    Add-Type -AssemblyName System.Drawing
    foreach($frameIndex in 1,2) {
    $capture = $captures[$frameIndex].Groups[1].Value.Trim()
    $dimensions = [regex]::Match([IO.Path]::GetFileName($capture), '^present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm\.bgra$')
    if(-not $dimensions.Success) { throw 'Unsupported triangle capture format.' }
    $width=[int]$dimensions.Groups[1].Value; $height=[int]$dimensions.Groups[2].Value
    $gpuBytes=[IO.File]::ReadAllBytes($capture)
    $coloredPixels=[TrianglePixelVerifierV2]::Check($gpuBytes,$width,$height,($frameIndex -eq 2))
    if($frameIndex -eq 1) {
        $rejectedBlank=$false; $rejectedWrongColor=$false
        try { [TrianglePixelVerifierV2]::Check([byte[]]::new(320*180*4),320,180,$false) | Out-Null } catch { $rejectedBlank=$true }
        try { [TrianglePixelVerifierV2]::Check($gpuBytes,$width,$height,$true) | Out-Null } catch { $rejectedWrongColor=$true }
        if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'Triangle verifier accepted a negative control.' }
    }
    $color=if($frameIndex -eq 2) { 'blue' } else { 'red' }
    Write-Output "PASS: $color GPU triangle verified across $($width*$height) pixels ($coloredPixels colored)."
    $bitmap=[Drawing.Bitmap]::new($width,$height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $locked=$bitmap.LockBits([Drawing.Rectangle]::new(0,0,$width,$height),[Drawing.Imaging.ImageLockMode]::WriteOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try { [Runtime.InteropServices.Marshal]::Copy($gpuBytes,0,$locked.Scan0,$gpuBytes.Length) }
        finally { $bitmap.UnlockBits($locked) }
        $bitmap.Save((Join-Path $root "triangle-$color.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    Write-Output "Captured GPU output: $capture"
    $verifiedFrames += [ordered]@{ Color=$color; Width=$width; Height=$height; ColoredPixels=$coloredPixels; Capture=$capture; Sha256=(Get-FileHash -LiteralPath $capture).Hash }
    }
    $report = [ordered]@{
        Status='Passed'; CompletedUtc=[DateTime]::UtcNow.ToString('o'); Frames=$verifiedFrames
        ExecutableSha256=(Get-FileHash -LiteralPath 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe').Hash
        GuestSha256=(Get-FileHash -LiteralPath $guest).Hash
        SourceSha256=(Get-FileHash -LiteralPath (Join-Path $fixture 'triangle.c')).Hash
        RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
        NegativeControls='Blank frame and incorrect expected color rejected'
        Log=$log
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'verification.json') -Encoding utf8
}
finally {
    foreach($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
}
