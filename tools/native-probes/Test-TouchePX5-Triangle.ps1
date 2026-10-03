[CmdletBinding()]
param([switch]$DmaImageComputeStore, [switch]$DmaImageComputeRead, [switch]$DmaImageSamplePreserved, [switch]$DmaImageSample, [switch]$DmaImageDraw, [switch]$DmaImageSpan, [switch]$DmaImagePrefix, [switch]$DmaImageCrossQueue, [switch]$DmaImage, [switch]$WindowRecovery, [switch]$CrossQueueDma, [switch]$CrossQueueWriteData, [switch]$CrossQueueReuse, [switch]$CrossQueue, [switch]$IndirectGpuArgs, [switch]$IndirectGpuCount, [switch]$IndirectMulti, [switch]$Indirect, [switch]$MipBc3, [switch]$MipBc1, [switch]$MipSrgb, [switch]$MipOdd, [switch]$Mip2D, [switch]$MipCachePressure, [switch]$MipBaseOnly, [switch]$MipLowerView, [switch]$Mip, [switch]$LinearFilter, [switch]$Perspective, [switch]$TextureUv, [switch]$TextureSelect, [switch]$Texture, [switch]$Depth, [switch]$Scissor, [switch]$Blend, [ValidateSet(0,16,32)][int]$IndexBits=0)
$ErrorActionPreference = 'Stop'
if($DmaImageComputeStore) { $DmaImageComputeRead=$true }
if($DmaImageComputeRead) { $DmaImageSamplePreserved=$true }
if($DmaImageSamplePreserved) { $DmaImageSample=$true }
if($DmaImageSample) {
    if($DmaImageDraw -or $DmaImageSpan) { throw 'Texture consumer requires its prefix layout.' }
    $DmaImagePrefix=$true
}
if($DmaImageDraw) {
    if($DmaImageSpan) { throw 'Dependent draw requires its prefix layout.' }
    $DmaImagePrefix=$true
}
if($DmaImageSpan) { $DmaImagePrefix=$true }
if($DmaImagePrefix) { $DmaImage=$true }
if($DmaImageCrossQueue) { $DmaImage=$true }
if($DmaImage -and @($PSBoundParameters.Keys | Where-Object { $_ -notin @('DmaImage','DmaImageCrossQueue','DmaImagePrefix','DmaImageSpan','DmaImageDraw','DmaImageSample','DmaImageSamplePreserved','DmaImageComputeRead','DmaImageComputeStore') }).Count -gt 0) { throw 'DMA image probe requires its standalone layout.' }
if($CrossQueueWriteData -and $CrossQueueReuse) { throw 'Choose either WRITE_DATA reset or cross-submission label reuse.' }
if($CrossQueueDma -and ($CrossQueueReuse -or $CrossQueueWriteData)) { throw 'DMA reset requires its standalone cross-queue mode.' }
if($CrossQueueReuse -or $CrossQueueWriteData -or $CrossQueueDma) { $CrossQueue=$true }
if($CrossQueue) { $IndirectGpuArgs=$true }
if($IndirectGpuArgs) {
    if($IndirectGpuCount -or $IndirectMulti) { throw 'GPU argument probe requires a single indirect draw.' }
    $Indirect=$true
}
if($IndirectGpuCount) { $IndirectMulti=$true }
if($IndirectMulti) { $Indirect=$true }
if($Indirect) {
    if($Scissor -or $Blend -or $IndexBits) { throw 'Indirect probe requires its standalone depth layout.' }
    $Depth=$true
}
$root = 'C:\Dev\TouchePX5\artifacts\smoke-triangle'
if($DmaImage) { $root += '-dma-image' }
if($DmaImageCrossQueue) { $root += '-cross-queue' }
if($DmaImagePrefix) { $root += '-prefix' }
if($DmaImageSpan) { $root += '-span' }
if($DmaImageDraw) { $root += '-draw' }
if($DmaImageSample) { $root += '-sample' }
if($DmaImageSamplePreserved) { $root += '-preserved' }
if($DmaImageComputeRead) { $root += '-compute-read' }
if($DmaImageComputeStore) { $root += '-store' }
if($MipBaseOnly -and $MipLowerView) { throw 'Choose only one restricted mip view.' }
if($MipOdd) { $Mip2D=$true }
if($MipBc3) {
    if($MipBc1 -or $MipSrgb -or $MipOdd -or $Mip2D -or $MipBaseOnly -or $MipLowerView -or $MipCachePressure -or $Scissor -or $IndexBits) { throw 'BC3 mip probe requires its standalone layout.' }
    $Mip=$true
}
if($MipBc1) {
    if($MipSrgb -or $MipOdd -or $MipBaseOnly -or $MipLowerView -or $MipCachePressure) { throw 'BC1 mip probe supports only its default or 2D layout.' }
    $Mip=$true
}
if($MipSrgb) {
    if($Mip2D -or $MipBaseOnly -or $MipLowerView -or $MipCachePressure -or $Scissor -or $IndexBits) { throw 'sRGB mip probe requires its standalone layout.' }
    $Mip=$true
}
if($MipBaseOnly -or $MipLowerView -or $MipCachePressure -or $Mip2D) { $Mip=$true }
if($Mip) {
    if($LinearFilter -or $Perspective -or $TextureUv) { throw 'Mip test uses explicit constant LOD.' }
    $TextureSelect=$true
}
if($Perspective) { $TextureUv=$true }
if($LinearFilter) {
    if($Perspective) { throw 'Linear filter oracle currently requires affine UVs.' }
    $TextureUv=$true
}
if($TextureUv) {
    if($Scissor -or $IndexBits) { throw 'UV oracle currently requires the ordinary triangle.' }
    $TextureSelect=$true
}
if($TextureSelect) { $Texture=$true }
if($Texture) { $root += '-texture' }
if($TextureSelect) { $root += '-select' }
if($TextureUv) { $root += '-uv' }
if($Perspective) { $root += '-perspective' }
if($LinearFilter) { $root += '-linear' }
if($Mip) { $root += '-mip' }
if($MipSrgb) { $root += '-srgb' }
if($MipBc1) { $root += '-bc1' }
if($MipBc3) { $root += '-bc3' }
if($MipBaseOnly) { $root += '-base-only' }
if($MipLowerView) { $root += '-lower-view' }
if($Mip2D) { $root += '-2d' }
if($MipOdd) { $root += '-odd' }
if($MipCachePressure) { $root += '-pressure' }
if($Texture -and ($Depth -or $Blend)) { throw 'Texture currently requires a separate scenario.' }
if($Depth) { $root += '-depth' }
if($Indirect) { $root += '-indirect' }
if($IndirectMulti) { $root += '-multi' }
if($IndirectGpuCount) { $root += '-gpu-count' }
if($IndirectGpuArgs) { $root += '-gpu-args' }
if($CrossQueue) { $root += '-cross-queue' }
if($CrossQueueReuse) { $root += '-reuse' }
if($CrossQueueWriteData) { $root += '-write-data' }
if($CrossQueueDma) { $root += '-dma' }
if($WindowRecovery) { $root += '-window-recovery' }
if($Depth -and $Blend) { throw 'Depth and Blend must be tested separately.' }
if($Blend) { $root += '-blend' }
if($Scissor) { $root += '-scissor' }
if($IndexBits) { $root += "-index$IndexBits" }
New-Item -ItemType Directory -Path $root -Force | Out-Null
Remove-Item -LiteralPath (Join-Path $root 'verification.json') -Force -ErrorAction SilentlyContinue
$fixture = Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$clang = 'C:\BuildTools\VC\Tools\Llvm\x64\bin\clang.exe'
$defines=@()
if($WindowRecovery) { $defines += '-DFRAME_PAUSE_US=20000000' }
if($Texture) { $defines += '-DTEST_TEXTURE=1' }
if($TextureSelect) { $defines += '-DTEST_TEXTURE_SELECT=1' }
if($TextureUv) { $defines += '-DTEST_TEXTURE_UV=1' }
if($Perspective) { $defines += '-DTEST_PERSPECTIVE=1' }
if($LinearFilter) { $defines += '-DTEST_LINEAR_FILTER=1' }
if($Mip) { $defines += '-DTEST_MIP=1' }
if($MipSrgb) { $defines += '-DTEST_MIP_SRGB=1' }
if($MipBc1) { $defines += '-DTEST_MIP_BC1=1' }
if($MipBc3) { $defines += '-DTEST_MIP_BC3=1' }
if($MipBaseOnly) { $defines += '-DTEST_MIP_BASE_ONLY=1' }
if($MipLowerView) { $defines += '-DTEST_MIP_LOWER_VIEW=1' }
if($Mip2D) { $defines += '-DTEST_MIP_2D=1' }
if($MipOdd) { $defines += '-DTEST_MIP_ODD=1' }
if($MipCachePressure) { $defines += '-DTEST_MIP_CACHE_PRESSURE=1' }
if($Depth) { $defines += '-DTEST_DEPTH=1' }
if($Indirect) { $defines += '-DTEST_INDIRECT=1' }
if($IndirectMulti) { $defines += '-DTEST_INDIRECT_MULTI=1' }
if($IndirectGpuCount) { $defines += '-DTEST_INDIRECT_GPU_COUNT=1' }
if($IndirectGpuArgs) { $defines += '-DTEST_INDIRECT_GPU_ARGS=1' }
if($CrossQueue) { $defines += '-DTEST_CROSS_QUEUE=1' }
if($CrossQueueReuse) { $defines += '-DTEST_CROSS_QUEUE_REUSE=1' }
if($CrossQueueWriteData) { $defines += '-DTEST_CROSS_QUEUE_WRITE_DATA=1' }
if($CrossQueueDma) { $defines += '-DTEST_CROSS_QUEUE_DMA=1' }
if($DmaImage) { $defines += '-DTEST_DMA_IMAGE=1' }
if($DmaImageCrossQueue) { $defines += '-DTEST_DMA_IMAGE_CROSS_QUEUE=1' }
if($DmaImagePrefix) { $defines += '-DTEST_DMA_IMAGE_PREFIX=1' }
if($DmaImageSpan) { $defines += '-DTEST_DMA_IMAGE_SPAN=1' }
if($DmaImageDraw) { $defines += '-DTEST_DMA_IMAGE_DRAW=1' }
if($DmaImageSample) { $defines += '-DTEST_DMA_IMAGE_SAMPLE=1' }
if($DmaImageSamplePreserved) { $defines += '-DTEST_DMA_IMAGE_SAMPLE_PRESERVED=1' }
if($DmaImageComputeRead) { $defines += '-DTEST_DMA_IMAGE_COMPUTE_READ=1' }
if($DmaImageComputeStore) { $defines += '-DTEST_DMA_IMAGE_COMPUTE_STORE=1' }
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
if($DmaImage) { $settings.SHARPEMU_TRACE_DRAWS='1' }
if($DmaImageCrossQueue) { $settings.SHARPEMU_GPU_WAIT_MODE='suspend' }
if($CrossQueue) { $settings.SHARPEMU_GPU_WAIT_MODE='suspend' }
if($CrossQueueReuse) { $settings.SHARPEMU_GPU_DEADLOCK_BREAK_MS='500' }
if($WindowRecovery) { $settings.SHARPEMU_STALL_WATCHDOG_SECONDS='120' }
try {
    foreach($name in $settings.Keys) {
        $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
        [Environment]::SetEnvironmentVariable($name,$settings[$name],'Process')
    }
    # The app host is a Windows GUI executable: direct invocation can return
    # before it exits. Wait for this exact child before inspecting its final log.
    $guestProcess=Start-Process -FilePath 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe' -ArgumentList @('--cpu-engine=native','--fps-limit=60','--log-level=info',"--log-file=$log",$guest) -WindowStyle Hidden -PassThru
    try {
        if(-not $guestProcess.WaitForExit($(if($WindowRecovery) { 120000 } else { 60000 }))) {
            $guestProcess.Kill()
            $guestProcess.WaitForExit(5000) | Out-Null
            throw "Triangle guest timed out: $log"
        }
        if($guestProcess.ExitCode -ne 0) { throw "Triangle launch failed ($($guestProcess.ExitCode)): $log" }
    } finally { $guestProcess.Dispose() }
    $output = if (Test-Path -LiteralPath $log) { '' + (Get-Content -LiteralPath $log -Raw) } else { '' }
    if (-not $output.Contains('[RUNTIME] DispatchEntry returned: ORBIS_GEN2_OK')) { throw "Triangle guest failed: $log" }
    if($WindowRecovery -and
       (-not $output.Contains('vk.present_deferred window-unavailable') -or
        -not $output.Contains('vk.present_resumed window-restored'))) {
        throw 'Window-recovery diagnostic did not observe both suspension and restoration.'
    }
    if ($output.Contains('agc.shader_color_clear')) { throw 'Triangle unexpectedly became a fullscreen clear.' }
    if($CrossQueue -and ($output.Contains('agc.deadlock_break ') -or
       $output.Contains('forced=True') -or $output.Contains('agc.compute_semantic_fast_path'))) {
        throw 'Cross-queue probe used a fallback instead of its ordered producer.'
    }
    if($CrossQueue -and
       ([regex]::Matches($output,'agc.driver_submit_acb owner=7 ').Count -ne 4 -or
        [regex]::Matches($output,'agc.dcb.suspended ').Count -lt 3)) { throw 'Missing cross-queue producer submissions or consumer suspensions.' }
    if($CrossQueueWriteData -and
       ([regex]::Matches($output,"vk.ordered_action queue=acb\.compute\[7\] [^\r\n]*name='write_data dst=0x[0-9A-F]+ count=2'").Count -ne 3 -or
        [regex]::Matches($output,'agc.dcb.write_data dst=5 [^\r\n]*count=2 increment=False[^\r\n]*wrote=True').Count -lt 1)) {
        throw 'Expected three nonincrementing WRITE_DATA signal/reset producers.'
    }
    if($CrossQueueDma -and
       ([regex]::Matches($output,"vk.ordered_action queue=acb\.compute\[7\] [^\r\n]*name='dma_data dst=0x[0-9A-F]+ bytes=4'").Count -ne 6 -or
        [regex]::Matches($output,'agc.dma_packet [^\r\n]*bytes=4 src_sel=2 fill=True copied=True').Count -ne 6)) {
        throw 'Expected six successful DMA fills forming three signal/reset pairs.'
    }
    $captures = [regex]::Matches($output, 'vk.swapchain_dump path=([^\r\n]+)')
    if(($IndirectGpuCount -or $IndirectGpuArgs) -and [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1').Count -ne 4) { throw 'Expected four GPU indirect-data-producing dispatches.' }
    # Detailed dcb.suspended packets are throttled; wait diagnostics retain both
    # this fixture's first producerless wait and its subsequent producer-backed wait.
    if($DmaImageCrossQueue -and
       ([regex]::Matches($output,'agc.driver_submit_acb owner=7 ').Count -ne 2 -or
        [regex]::Matches($output,'agc.wait_suspended [^\r\n]*queue=dcb.graphics ').Count -ne 2 -or
        [regex]::Matches($output,'agc.dcb.resumed ').Count -ne 2 -or
        $output.Contains('agc.deadlock_break ') -or $output.Contains('forced=True'))) {
        throw 'Expected two genuinely suspended/resumed graphics consumers and two ACB image producers.'
    }
    $dmaBytesPattern=if($DmaImageSpan) { '115840' } elseif($DmaImagePrefix) { '115200' } else { '230400' }
    $sampleConsumerWords=if($DmaImageComputeStore) { 139 } elseif($DmaImageComputeRead) { 133 } else { 65 }
    if($DmaImageSample -and $DmaImageCrossQueue -and
       ([regex]::Matches($output,'agc.driver_submit_acb owner=7 [^\r\n]*dwords=21 ').Count -ne 1 -or
        [regex]::Matches($output,'agc.driver_submit_acb owner=7 [^\r\n]*dwords=15 ').Count -ne 1 -or
        [regex]::Matches($output,"agc.queue_resumed queue=dcb.graphics [^\r\n]*remaining_dwords=$sampleConsumerWords ").Count -ne 2)) {
        throw 'Expected DMA-only producers and two texture draws in resumed graphics consumers.'
    }
    if($DmaImageDraw -and $DmaImageCrossQueue -and
       ([regex]::Matches($output,'agc.driver_submit_acb owner=7 [^\r\n]*dwords=21 ').Count -ne 1 -or
        [regex]::Matches($output,'agc.driver_submit_acb owner=7 [^\r\n]*dwords=15 ').Count -ne 1 -or
        [regex]::Matches($output,'agc.queue_resumed queue=dcb.graphics [^\r\n]*remaining_dwords=23 ').Count -ne 1 -or
        [regex]::Matches($output,'agc.queue_resumed queue=dcb.graphics [^\r\n]*remaining_dwords=17 ').Count -ne 1)) {
        throw 'Expected DMA-only ACB producers and dependent draws in the resumed graphics consumers.'
    }
    if($DmaImageCrossQueue -and
       ([regex]::Matches($output,"vk.ordered_action queue=acb\.compute\[7\] [^\r\n]*name='dma_data dst=0x[0-9A-F]+ bytes=$dmaBytesPattern'").Count -ne 2 -or
        [regex]::Matches($output,"vk.ordered_action queue=acb\.compute\[7\] [^\r\n]*name='dma_data dst=0x[0-9A-F]+ bytes=$dmaBytesPattern completion'").Count -ne 2)) {
        throw 'Expected both image DMA operations and their completion callbacks on the ACB queue.'
    }
    $dmaHasExactImage=if($DmaImageSpan) { 'False' } else { 'True' }
    if($DmaImage -and
       ([regex]::Matches($output,"\[DMA\] [^\r\n]*bytes=$dmaBytesPattern [^\r\n]*image=$dmaHasExactImage").Count -ne 2 -or
        [regex]::Matches($output,"agc.dma_packet [^\r\n]*bytes=$dmaBytesPattern [^\r\n]*copied=True").Count -ne 2)) {
        throw 'Expected two successful DMA updates of an existing backend image.'
    }
    if($DmaImageDraw -or $DmaImageSample) {
        # Each partial upload must execute within its own ordered DMA action,
        # before the next consumer. A frame-boundary upload may otherwise erase
        # an already executed blend, even when occasional runs happen to pass.
        $dmaActions=[regex]::Matches($output,"vk.ordered_action [^\r\n]*work_sequence=(\d+) name='dma_data dst=0x[0-9A-F]+ bytes=$dmaBytesPattern'")
        if($dmaActions.Count -ne 2) { throw 'Expected two ordered DMA actions.' }
        foreach($action in $dmaActions) {
            $sequence=$action.Groups[1].Value
            $uploads=[regex]::Matches($output,"vk.cpu_image_partial_upload [^\r\n]*work_sequence=$sequence success=True")
            if($uploads.Count -ne 1 -or $uploads[0].Index -ge $action.Index) {
                throw "Partial image upload did not execute within DMA action $sequence."
            }
        }
    }
    if($DmaImageComputeRead) {
        # Each image instruction has its own binding, all aliasing one image.
        # The shader summary is cached; actual dispatch traces must occur three times.
        $computeImageBindings=if($DmaImageComputeStore) { 4 } else { 2 }
        if([regex]::Matches($output,"vk.compute_dispatch groups=1x1x1 [^\r\n]*textures=$computeImageBindings ").Count -ne 3 -or
           $output -notmatch 'agc.compute_shader[^\r\n]*gpu=True[^\r\n]*global_writes=True' -or
           $output.Contains('agc.compute_semantic_fast_path')) {
            throw 'Expected three translated image-reading compute dispatches with GPU readback.'
        }
        Write-Output 'PASS: Three GPU compute image reads returned exact RGBA values from updated and preserved texels; output guard words remain unchanged.'
        if($DmaImageComputeStore) { Write-Output 'PASS: GPU image stores wrote yellow at (96,48); compute readback verified it without changing sampled texels.' }
    }
    $expectedDraws=if($DmaImageSample) { 3 } elseif($DmaImageDraw) { 3 } elseif($DmaImage) { 1 } elseif($Depth) { 4 } elseif($Texture) { 3 } else { 2 }
    if($MipCachePressure) { $expectedDraws += 40 }
    $expectedCaptures=if($DmaImage -or $Depth -or $Texture) { 4 } else { 3 }
    if($captures.Count -ne $expectedCaptures) { throw "Unexpected number of swapchain captures: $log" }
    $textureCount=if($Texture -or $DmaImageSample) { 1 } else { 0 }
    if([regex]::Matches($output,"vk.offscreen_draw mrt=1 size=320x180 textures=$textureCount").Count -ne $expectedDraws -or $output.Contains('agc.shader_translate_miss')) { throw 'Missing expected successful translated raster draws.' }
    if($DmaImageSample -and [regex]::Matches($output,'vk.offscreen_draw mrt=1 size=320x180 textures=0').Count -ne 1) { throw 'Missing initial GPU-only source draw.' }
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
    if(($MipSrgb -or $MipBc3) -and -not ('UniformMipVerifierV2' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class UniformMipVerifierV2 {
    public static int Check(byte[] data,int width,int height,bool blue,int encoded,bool srgbOutput,bool decode,int expectedAlpha) {
        if(data.Length!=checked(width*height*4)) throw new Exception("Invalid capture size");
        double c=encoded/255.0;
        if(decode) c=c<=0.04045 ? c/12.92 : Math.Pow((c+0.055)/1.055,2.4);
        c=Math.Round(c*255)/255; // the guest color target is RGBA8 UNORM
        if(srgbOutput) c=c<=0.0031308 ? c*12.92 : 1.055*Math.Pow(c,1/2.4)-0.055;
        int expected=(int)Math.Round(c*255), count=0, channel=blue?0:2, other=blue?2:0;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int p=(y*width+x)*4;
            double sx=(x+0.5)*320/width,sy=(y+0.5)*180/height;
            double diagonal=(sx-40)/240+(sy-22.5)/135;
            bool inside=sx>42 && sy>24.5 && diagonal<0.98;
            bool outside=sx<38 || sy<20.5 || diagonal>1.02;
            bool color=Math.Abs(data[p+channel]-expected)<=1 && data[p+3]==expectedAlpha;
            bool black=data[p]==0 && data[p+1]==0 && data[p+2]==0 && data[p+3]==0;
            if(data[p+other]!=0 || data[p+1]!=0 || data[p+3]>expectedAlpha ||
               data[p+channel]>expected+1 || (inside && !color) || (outside && !black))
                throw new Exception("Mip color/alpha/coverage mismatch at "+x+","+y+" expected="+expected);
            if(color) count++;
        }
        if(count<width*(long)height*0.27 || count>width*(long)height*0.30) throw new Exception("Wrong mip triangle area");
        return count;
    }
}
"@
    }
    if($DmaImage -and -not ('DmaImageVerifierV1' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class DmaImageVerifierV1 {
    public static int Check(byte[] data,int width,int height,bool blue) {
        if(data.Length!=checked(width*height*4)) throw new Exception("Invalid DMA capture size");
        for(int p=0;p<data.Length;p+=4) {
            if(data[p]!=(blue?255:0) || data[p+1]!=0 || data[p+2]!=(blue?0:255) || data[p+3]!=255)
                throw new Exception("DMA image mismatch at pixel "+(p/4));
        }
        return width*height;
    }
}
"@
    }
    if($DmaImagePrefix -and -not ('DmaPrefixVerifierV4' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class DmaPrefixVerifierV4 {
    public static byte[] MakeSample(byte[] baseline,int width,int height,bool blue,int first,int end) {
        var result=(byte[])baseline.Clone();
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int pixel=(int)((y+0.5)*180/height)*320+(int)((x+0.5)*320/width);
            if(pixel<first || pixel>=end) continue;
            int p=(y*width+x)*4;
            result[p]=(byte)(blue?255:0); result[p+1]=0;
            result[p+2]=(byte)(blue?0:255); result[p+3]=255;
        }
        return result;
    }
    public static int Check(byte[] data,byte[] baseline,int width,int height,bool blue,int first,int end) {
        if(data.Length!=checked(width*height*4) || baseline.Length!=data.Length)
            throw new Exception("Invalid prefix image size");
        int colored=0;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int p=(y*width+x)*4;
            if(data[p+1]!=0 || Math.Abs(data[p]+data[p+2]-data[p+3])>1)
                throw new Exception("Invalid span image channels");
            if(data[p+3]==255) colored++;
            double sx=(x+0.5)*320/width-0.5, sy=(y+0.5)*180/height-0.5;
            int changed=0;
            for(int dy=0;dy<2;dy++) for(int dx=0;dx<2;dx++) {
                int tx=Math.Clamp((int)Math.Floor(sx)+dx,0,319);
                int ty=Math.Clamp((int)Math.Floor(sy)+dy,0,179);
                int pixel=ty*320+tx;
                if(pixel>=first && pixel<end) changed++;
            }
            // Mixed samples straddle the update edge; require exact results elsewhere.
            if(changed!=0 && changed!=4) continue;
            if(changed==4) {
                if(data[p]!=(blue?255:0) || data[p+1]!=0 || data[p+2]!=(blue?0:255) || data[p+3]!=255)
                    throw new Exception("Updated span mismatch at "+x+","+y);
            } else {
                for(int c=0;c<4;c++) if(data[p+c]!=baseline[p+c])
                    throw new Exception("Untouched GPU region changed at "+x+","+y);
            }
        }
        return colored;
    }
}
"@
    }
    if($DmaImageDraw -and -not ('DmaDrawVerifierV1' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class DmaDrawVerifierV1 {
    public static int Check(byte[] data,int width,int height,bool second) {
        if(data.Length!=checked(width*height*4)) throw new Exception("Invalid draw capture size");
        int colored=0;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int p=(y*width+x)*4;
            if(data[p+1]!=0 || Math.Abs(data[p]+data[p+2]-data[p+3])>2)
                throw new Exception("Invalid dependent-draw channels");
            if(data[p+3]==255) colored++;
            double sx=(x+0.5)*320/width, sy=(y+0.5)*180/height;
            double d=(sx-40)/240+(sy-22.5)/135;
            bool inside=sx>42 && sy>24.5 && d<0.98;
            bool outside=sx<38 || sy<20.5 || d>1.02;
            if(Math.Abs(sy-90)<2 || (!inside && !outside)) continue;
            int r=0,b=0,a=0;
            if(sy<90) {
                a=255;
                if(inside) { r=128;b=128; }
                else if(second) b=255; else r=255;
            } else if(inside) {
                a=255;
                if(second) r=255; else { r=128;b=128; }
            }
            if(Math.Abs(data[p]-b)>1 || Math.Abs(data[p+2]-r)>1 || data[p+3]!=a)
                throw new Exception("Dependent draw mismatch at "+x+","+y);
        }
        return colored;
    }
}
"@
    }
    $verifiedFrames = @()
    if($TextureUv -and -not ('TextureUvVerifierV3' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
public static class TextureUvVerifierV3 {
    public static int Check(byte[] data,int width,int height,bool swap,bool perspective,bool linear) {
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
            if(!inside || (!linear && (Math.Abs(u-0.5)<0.02 || Math.Abs(v-0.5)<0.02))) continue;
            int channel=v>0.5 ? 1 : u>0.5 ? 0 : 2;
            if(swap && channel!=1) channel=2-channel;
            if(linear) {
                double tx=Math.Max(0,Math.Min(1,2*u-0.5)),ty=Math.Max(0,Math.Min(1,2*v-0.5));
                double r=255*((1-tx)*(1-ty)+tx*ty),g=255*ty,b=255*tx;
                if(swap) { double tmp=r; r=b; b=tmp; }
                if(data[p+3]!=255 || Math.Abs(data[p]-b)>3 || Math.Abs(data[p+1]-g)>3 || Math.Abs(data[p+2]-r)>3)
                    throw new Exception("Linear sample mismatch at "+x+","+y);
            } else if(data[p+3]!=255 || data[p+channel]!=255 || data[p+(channel+1)%3]!=0 || data[p+(channel+2)%3]!=0)
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
    if($Mip) { $expectBlue=($frameIndex -eq 2) }
    if($MipBaseOnly) { $expectBlue=$false }
    if($MipLowerView) { $expectBlue=($frameIndex -lt 3) }
    if($DmaImage) { $expectBlue=($frameIndex -eq 2) }
    $capture = $captures[$frameIndex].Groups[1].Value.Trim()
    $dimensions = [regex]::Match([IO.Path]::GetFileName($capture), '^present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm\.bgra$')
    if(-not $dimensions.Success) { throw 'Unsupported triangle capture format.' }
    $width=[int]$dimensions.Groups[1].Value; $height=[int]$dimensions.Groups[2].Value
    $gpuBytes=[IO.File]::ReadAllBytes($capture)
    if($DmaImageSample) {
        $coloredPixels=[TrianglePixelVerifierV5]::Check($gpuBytes,$width,$height,$expectBlue,$false,$false,$false)
        $rejectedBlank=$false; $rejectedWrongColor=$false
        try { [TrianglePixelVerifierV5]::Check([byte[]]::new($gpuBytes.Length),$width,$height,$expectBlue,$false,$false,$false) | Out-Null } catch { $rejectedBlank=$true }
        try { [TrianglePixelVerifierV5]::Check($gpuBytes,$width,$height,(-not $expectBlue),$false,$false,$false) | Out-Null } catch { $rejectedWrongColor=$true }
        if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'Texture consumer oracle accepted blank/stale colors.' }
    } elseif($DmaImageDraw -and $frameIndex -gt 1) {
        $blankRejected=$false
        try { [DmaDrawVerifierV1]::Check([byte[]]::new($gpuBytes.Length),$width,$height,$expectBlue) | Out-Null } catch { $blankRejected=$true }
        if(-not $blankRejected) { throw 'Dependent draw oracle accepted a blank frame.' }
        $baselineBytes=[IO.File]::ReadAllBytes($captures[1].Groups[1].Value.Trim())
        $dmaOnly=[DmaPrefixVerifierV4]::MakeSample($baselineBytes,$width,$height,$expectBlue,0,28800)
        $drawMissingRejected=$false; $wrongFrameRejected=$false
        try { [DmaDrawVerifierV1]::Check($dmaOnly,$width,$height,$expectBlue) | Out-Null } catch { $drawMissingRejected=$true }
        try { [DmaDrawVerifierV1]::Check($gpuBytes,$width,$height,(-not $expectBlue)) | Out-Null } catch { $wrongFrameRejected=$true }
        if(-not $drawMissingRejected -or -not $wrongFrameRejected) { throw 'Dependent draw oracle accepted a missing draw or wrong frame.' }
        $coloredPixels=[DmaDrawVerifierV1]::Check($gpuBytes,$width,$height,$expectBlue)
    } elseif($DmaImagePrefix -and $frameIndex -gt 1) {
        $firstPixel=if($DmaImageSpan) { 14480 } else { 0 }
        $endPixel=if($DmaImageSpan) { 43440 } else { 28800 }
        $baselineBytes=[IO.File]::ReadAllBytes($captures[1].Groups[1].Value.Trim())
        $rejectedBlank=$false; $rejectedFull=$false
        try { [DmaPrefixVerifierV4]::Check([byte[]]::new($gpuBytes.Length),$baselineBytes,$width,$height,$expectBlue,$firstPixel,$endPixel) | Out-Null } catch { $rejectedBlank=$true }
        $wrong=[DmaPrefixVerifierV4]::MakeSample($baselineBytes,$width,$height,$expectBlue,0,57600)
        try { [DmaPrefixVerifierV4]::Check($wrong,$baselineBytes,$width,$height,$expectBlue,$firstPixel,$endPixel) | Out-Null } catch { $rejectedFull=$true }
        if(-not $rejectedBlank -or -not $rejectedFull) { throw 'Prefix oracle accepted blank/full-image overwrite.' }
        $positive=[DmaPrefixVerifierV4]::MakeSample($baselineBytes,$width,$height,$expectBlue,$firstPixel,$endPixel)
        [DmaPrefixVerifierV4]::Check($positive,$baselineBytes,$width,$height,$expectBlue,$firstPixel,$endPixel) | Out-Null
        $coloredPixels=[DmaPrefixVerifierV4]::Check($gpuBytes,$baselineBytes,$width,$height,$expectBlue,$firstPixel,$endPixel)
    } elseif($DmaImage -and $frameIndex -gt 1) {
        $coloredPixels=[DmaImageVerifierV1]::Check($gpuBytes,$width,$height,$expectBlue)
        $rejectedBlank=$false; $rejectedWrongColor=$false
        try { [DmaImageVerifierV1]::Check([byte[]]::new(320*180*4),320,180,$expectBlue) | Out-Null } catch { $rejectedBlank=$true }
        try { [DmaImageVerifierV1]::Check($gpuBytes,$width,$height,(-not $expectBlue)) | Out-Null } catch { $rejectedWrongColor=$true }
        if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'DMA image verifier accepted a negative control.' }
    } elseif($MipSrgb -or $MipBc3) {
        $encoded=if($MipBc3) { 255 } elseif($frameIndex -eq 3) { 64 } else { 128 }
        $expectedAlpha=if($MipBc3 -and $frameIndex -eq 3) { 64 } else { 128 }
        $srgbOutput=$capture -match 'Srgb\.bgra$'
        $coloredPixels=[UniformMipVerifierV2]::Check($gpuBytes,$width,$height,$expectBlue,$encoded,$srgbOutput,[bool]$MipSrgb,$expectedAlpha)
        if($MipSrgb) {
            $rejectedNoDecode=$false
            try { [UniformMipVerifierV2]::Check($gpuBytes,$width,$height,$expectBlue,$encoded,$srgbOutput,$false,$expectedAlpha) | Out-Null } catch { $rejectedNoDecode=$true }
            if(-not $rejectedNoDecode) { throw 'sRGB mip output passed the incorrect non-decoding oracle.' }
        }
        if($MipBc3) {
            $rejectedOpaque=$false
            try { [UniformMipVerifierV2]::Check($gpuBytes,$width,$height,$expectBlue,$encoded,$srgbOutput,$false,255) | Out-Null } catch { $rejectedOpaque=$true }
            if(-not $rejectedOpaque) { throw 'BC3 mip output passed the incorrect opaque-alpha oracle.' }
        }
        if($frameIndex -eq 1) {
            $rejectedBlank=$false; $rejectedWrongColor=$false
            try { [UniformMipVerifierV2]::Check([byte[]]::new(320*180*4),320,180,$false,$encoded,$srgbOutput,[bool]$MipSrgb,$expectedAlpha) | Out-Null } catch { $rejectedBlank=$true }
            try { [UniformMipVerifierV2]::Check($gpuBytes,$width,$height,(-not $expectBlue),$encoded,$srgbOutput,[bool]$MipSrgb,$expectedAlpha) | Out-Null } catch { $rejectedWrongColor=$true }
            if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'Mip verifier accepted a negative control.' }
        }
    } elseif($TextureUv) {
        $coloredPixels=[TextureUvVerifierV3]::Check($gpuBytes,$width,$height,$false,[bool]$Perspective,[bool]$LinearFilter)
        if($frameIndex -eq 1) {
            $rejectedBlank=$false; $rejectedWrongColor=$false
            try { [TextureUvVerifierV3]::Check([byte[]]::new(320*180*4),320,180,$false,[bool]$Perspective,[bool]$LinearFilter) | Out-Null } catch { $rejectedBlank=$true }
            try { [TextureUvVerifierV3]::Check($gpuBytes,$width,$height,$true,[bool]$Perspective,[bool]$LinearFilter) | Out-Null } catch { $rejectedWrongColor=$true }
            if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'UV verifier accepted a negative control.' }
            if($Perspective) {
                $rejectedAffine=$false
                try { [TextureUvVerifierV3]::Check($gpuBytes,$width,$height,$false,$false,$false) | Out-Null } catch { $rejectedAffine=$true }
                if(-not $rejectedAffine) { throw 'Perspective output also passed the incorrect affine oracle.' }
            }
            if($LinearFilter) {
                $rejectedNearest=$false
                try { [TextureUvVerifierV3]::Check($gpuBytes,$width,$height,$false,$false,$false) | Out-Null } catch { $rejectedNearest=$true }
                if(-not $rejectedNearest) { throw 'Linear output passed the incorrect nearest-neighbor oracle.' }
            }
        }
    } else {
        $coloredPixels=[TrianglePixelVerifierV5]::Check($gpuBytes,$width,$height,$expectBlue,[bool]$Scissor,($IndexBits -ne 0),[bool]$Blend)
    }
    if($frameIndex -eq 1 -and -not $TextureUv -and -not $MipSrgb -and -not $MipBc3) {
        $rejectedBlank=$false; $rejectedWrongColor=$false
        try { [TrianglePixelVerifierV5]::Check([byte[]]::new(320*180*4),320,180,$false,[bool]$Scissor,($IndexBits -ne 0),[bool]$Blend) | Out-Null } catch { $rejectedBlank=$true }
        try { [TrianglePixelVerifierV5]::Check($gpuBytes,$width,$height,(-not $expectBlue),[bool]$Scissor,($IndexBits -ne 0),[bool]$Blend) | Out-Null } catch { $rejectedWrongColor=$true }
        if(-not $rejectedBlank -or -not $rejectedWrongColor) { throw 'Triangle verifier accepted a negative control.' }
    }
    $color=if($TextureUv) { 'textured' } elseif($expectBlue -and $Blend) { 'purple' } elseif($expectBlue) { 'blue' } else { 'red' }
    $shape=if($DmaImage -and $frameIndex -gt 1) { 'DMA image' } else { 'GPU triangle' }
    Write-Output "PASS: $color $shape verified across $($width*$height) pixels ($coloredPixels colored)."
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
    $mipCacheHits=0; $mipCacheMisses=0; $mipUploads=0; $mipCacheTrims=0
    if($Mip) {
        $mipTrace=Get-Content -LiteralPath $log -Raw
        $mipCacheHits=[regex]::Matches($mipTrace,'vk\.cpu_mip_cache hit').Count
        $mipCacheMisses=[regex]::Matches($mipTrace,'vk\.cpu_mip_cache miss').Count
        $mipUploads=[regex]::Matches($mipTrace,'vk\.cpu_mip_upload levels=2').Count
        $mipCacheTrims=[regex]::Matches($mipTrace,'vk\.cpu_mip_cache trim entries=32').Count
        $expectedMisses=if($MipCachePressure) { 42 } else { 2 }
        if($mipCacheHits -ne 1 -or $mipCacheMisses -ne $expectedMisses -or $mipUploads -ne $expectedMisses) {
            throw "Mip cache expected one unchanged reuse and $expectedMisses uploads; got hits=$mipCacheHits misses=$mipCacheMisses uploads=$mipUploads."
        }
        if($MipCachePressure -and $mipCacheTrims -lt 1) { throw 'Mip pressure did not exercise deferred eviction.' }
    }
    $report = [ordered]@{
        Status='Passed'; CompletedUtc=[DateTime]::UtcNow.ToString('o'); Frames=$verifiedFrames; Scissor=[bool]$Scissor; IndexBits=$IndexBits; Blend=[bool]$Blend; Depth=[bool]$Depth
        Indirect=[bool]$Indirect
        IndirectMulti=[bool]$IndirectMulti
        IndirectGpuCount=[bool]$IndirectGpuCount
        IndirectGpuArgs=[bool]$IndirectGpuArgs
        CrossQueue=[bool]$CrossQueue
        CrossQueueReuse=[bool]$CrossQueueReuse
        CrossQueueWriteData=[bool]$CrossQueueWriteData
        CrossQueueDma=[bool]$CrossQueueDma
        DmaImageComputeStore=[bool]$DmaImageComputeStore
        DmaImageComputeRead=[bool]$DmaImageComputeRead
        ComputeImageReadDispatches=$(if($DmaImageComputeRead) { 3 } else { 0 })
        ComputeImageReadValueChecks=$(if($DmaImageComputeStore) { 36 } elseif($DmaImageComputeRead) { 24 } else { 0 })
        ComputeImageReadGuardChecks=$(if($DmaImageComputeStore) { 12 } elseif($DmaImageComputeRead) { 24 } else { 0 })
        DmaImageSamplePreserved=[bool]$DmaImageSamplePreserved
        DmaImageSample=[bool]$DmaImageSample
        DmaImageDraw=[bool]$DmaImageDraw
        DmaImageSpan=[bool]$DmaImageSpan
        DmaImagePrefix=[bool]$DmaImagePrefix
        DmaImage=[bool]$DmaImage
        DmaImageCrossQueue=[bool]$DmaImageCrossQueue
        WindowRecovery=[bool]$WindowRecovery
        Texture=[bool]$Texture
        TextureSelect=[bool]$TextureSelect
        TextureUv=[bool]$TextureUv
        Perspective=[bool]$Perspective
        LinearFilter=[bool]$LinearFilter
        Mip=[bool]$Mip
        MipBaseOnly=[bool]$MipBaseOnly
        MipLowerView=[bool]$MipLowerView
        MipCacheHits=$mipCacheHits
        MipCacheMisses=$mipCacheMisses
        MipUploads=$mipUploads
        MipCacheTrims=$mipCacheTrims
        MipCachePressure=[bool]$MipCachePressure
        Mip2D=[bool]$Mip2D
        MipOdd=[bool]$MipOdd
        MipSrgb=[bool]$MipSrgb
        MipBc1=[bool]$MipBc1
        MipBc3=[bool]$MipBc3
        OpaqueAlphaOracleRejected=[bool]$MipBc3
        NonDecodingOracleRejected=[bool]$MipSrgb
        ExecutableSha256=(Get-FileHash -LiteralPath 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe').Hash
        GuestSha256=(Get-FileHash -LiteralPath $guest).Hash
        SourceSha256=(Get-FileHash -LiteralPath (Join-Path $fixture 'triangle.c')).Hash
        RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
        NegativeControls=$(if($MipSrgb) { 'Blank frame and incorrect non-decoding oracle rejected' } elseif($MipBc3) { 'Blank frame, incorrect expected color and opaque alpha rejected' } else { 'Blank frame and incorrect expected color rejected' })
        AffineOracleRejected=[bool]$Perspective
        NearestOracleRejected=[bool]$LinearFilter
        Log=$log
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'verification.json') -Encoding utf8
}
finally {
    foreach($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
}
