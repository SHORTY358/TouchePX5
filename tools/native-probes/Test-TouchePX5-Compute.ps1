[CmdletBinding()]
param([ValidateSet(0,1,2,3,4,5)][int]$ByteCase=0, [switch]$UnalignedByte, [switch]$PartialImage, [switch]$ImageAtomicsCrossQueue, [switch]$NggGroupFoundation, [switch]$NggForceAllActive, [switch]$LdsPaired, [switch]$LdsWide, [switch]$LdsAtomics, [switch]$BoundedAtomics, [switch]$BufferAtomics, [switch]$SignedImage, [switch]$ImageAtomics, [switch]$IndirectDimensions, [switch]$BaseRelative, [switch]$ComputeQueue)
$ErrorActionPreference = 'Stop'
if($NggForceAllActive -and -not $NggGroupFoundation) { throw 'Negative tail control requires group foundation mode' }
if($NggGroupFoundation) { $LdsPaired=$true }
if($ByteCase -ne 0) { $UnalignedByte=$true }
if($UnalignedByte) { $PartialImage=$true }
if($PartialImage) { $ImageAtomics=$true }
if($PartialImage -and $ImageAtomicsCrossQueue) { throw 'Choose either partial CPU image DMA or cross-queue image atomics.' }
if($ImageAtomicsCrossQueue) { $ImageAtomics=$true }
if($ImageAtomicsCrossQueue -and $ComputeQueue) { throw 'Cross-queue image mode uses an ACB producer and a graphics consumer.' }
if($LdsPaired) { $LdsAtomics=$true }
if($LdsWide) { $LdsAtomics=$true }
if($LdsPaired -and $LdsWide) { throw 'Choose either paired or wide LDS stores.' }
if($LdsAtomics) { $BoundedAtomics=$true }
if($LdsAtomics -and ($BufferAtomics -or $SignedImage)) { throw 'LDS mode has no input buffer/image descriptor.' }
if($BufferAtomics) { $BoundedAtomics=$true }
if($SignedImage -and -not $PartialImage) { $BoundedAtomics=$true }
if($SignedImage -and $BufferAtomics) { throw 'Signed-image mode requires an image descriptor.' }
if($ImageAtomics -and $BoundedAtomics) { throw 'Choose either atomic add or bounded increment/decrement.' }
if(($ImageAtomics -or $BoundedAtomics) -and ($IndirectDimensions -or $BaseRelative)) { throw 'Atomic image/buffer fixtures require their standalone layout.' }
if($BaseRelative -and $ComputeQueue) { throw 'The compute-queue fixture uses absolute MEC-format arguments.' }
if($BaseRelative -or ($ComputeQueue -and -not ($ImageAtomics -or $BoundedAtomics))) { $IndirectDimensions=$true }
$root = 'C:\Dev\TouchePX5\artifacts\smoke-compute'
if($ImageAtomics) { $root += '-image-atomics' }
if($ImageAtomicsCrossQueue) { $root += '-cross-queue' }
if($PartialImage) { $root += '-partial' }
if($UnalignedByte) { $root += '-byte-case'+$ByteCase }
if($BoundedAtomics) { $root += '-bounded-atomics' }
if($BufferAtomics) { $root += '-buffer' }
if($SignedImage) { $root += '-signed' }
if($LdsAtomics) { $root += '-lds' }
if($LdsWide) { $root += '-wide' }
if($LdsPaired) { $root += '-paired' }
if($NggGroupFoundation) { $root += '-ngg-foundation' }
if($NggForceAllActive) { $root += '-all-active-negative' }
if($IndirectDimensions) { $root += '-indirect-dimensions' }
if($BaseRelative) { $root += '-base-relative' }
if($ComputeQueue) { $root += '-compute-queue' }
New-Item -ItemType Directory -Path $root -Force | Out-Null
$fixture = Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$clang = 'C:\BuildTools\VC\Tools\Llvm\x64\bin\clang.exe'
$source=if($NggGroupFoundation) { 'compute-ngg-group-foundation.c' } elseif($PartialImage) { 'compute-image-partial.c' } elseif($ImageAtomicsCrossQueue) { 'compute-image-atomic-cross.c' } elseif($LdsPaired) { 'compute-lds-paired.c' } elseif($LdsWide) { 'compute-lds-wide.c' } elseif($LdsAtomics) { 'compute-lds-atomic.c' } elseif($BoundedAtomics) { 'compute-clamped-atomic.c' } elseif($ImageAtomics) { 'compute-image-atomic.c' } elseif($IndirectDimensions) { 'compute-indirect.c' } else { 'compute.c' }
$fixtureOptions=@(
    if($NggForceAllActive) { '-DNGG_FORCE_ALL_ACTIVE=1' }
    if($UnalignedByte) { '-DPARTIAL_UNALIGNED_BYTE=1'; '-DPARTIAL_BYTE_CASE='+$ByteCase }
    if($BufferAtomics) { '-DCLAMP_BUFFER=1' }
    if($SignedImage) { if($PartialImage) { '-DPARTIAL_SIGNED_IMAGE=1' } else { '-DCLAMP_SIGNED_IMAGE=1' } }
    if($BaseRelative) { '-DINDIRECT_BASE_RELATIVE=1' }
    if($ComputeQueue) { if($ImageAtomics -or $BoundedAtomics) { '-DIMAGE_ATOMIC_COMPUTE_QUEUE=1' } else { '-DINDIRECT_COMPUTE_QUEUE=1' } }
)
& $clang --target=x86_64-pc-linux-gnu -O1 -ffreestanding -fno-builtin -fno-stack-protector -fpie -mno-sse @fixtureOptions -c (Join-Path $fixture $source) -o (Join-Path $root 'compute.o')
if ($LASTEXITCODE -ne 0) { throw 'Compute guest compilation failed.' }
& $clang --target=x86_64-pc-linux-gnu -c (Join-Path $fixture 'imports.S') -o (Join-Path $root 'imports.o')
if ($LASTEXITCODE -ne 0) { throw 'Compute imports compilation failed.' }
$guest = Join-Path $root 'compute.elf'
& 'C:\BuildTools\VC\Tools\Llvm\x64\bin\ld.lld.exe' -T (Join-Path $PSScriptRoot 'fixtures\ps5-touche-input-demo\demo.ld') -o $guest (Join-Path $root 'compute.o') (Join-Path $root 'imports.o')
if ($LASTEXITCODE -ne 0) { throw 'Compute guest link failed.' }
$bytes = [IO.File]::ReadAllBytes($guest)
$bytes[7]=9; $bytes[8]=2; $bytes[16]=0x10; $bytes[17]=0xfe
[IO.File]::WriteAllBytes($guest,$bytes)
$log = Join-Path $root 'compute.log'
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Write-Output "Log: $log"
$oldAgc = $env:SHARPEMU_LOG_AGC
try {
    $env:SHARPEMU_LOG_AGC = '1'
    $guestProcess=Start-Process -FilePath 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe' -ArgumentList @('--cpu-engine=native','--fps-limit=60','--log-level=info',"--log-file=$log",$guest) -WindowStyle Hidden -PassThru
    try {
        if(-not $guestProcess.WaitForExit(60000)) {
            $guestProcess.Kill()
            $guestProcess.WaitForExit(5000) | Out-Null
            throw "Compute guest timed out: $log"
        }
        if($guestProcess.ExitCode -ne 0) { throw "Compute launch failed ($($guestProcess.ExitCode)): $log" }
    } finally { $guestProcess.Dispose() }
    $output = if (Test-Path -LiteralPath $log) { '' + (Get-Content -LiteralPath $log -Raw) } else { '' }
    if (-not $output.Contains('[RUNTIME] DispatchEntry returned: ORBIS_GEN2_OK')) { throw "Compute guest failed: $log" }
    if ($output -notmatch 'agc.compute_shader[^\r\n]*gpu=True[^\r\n]*global_writes=True') { throw "Missing translated GPU compute dispatch: $log" }
    if($PartialImage) {
        if($UnalignedByte) {
            $edges=if($ByteCase -le 2) { 1 } else { 2 }
            if([regex]::Matches($output,'vk.cpu_image_edge_readback ').Count -ne $edges) {
                throw "Expected exactly $edges edge-texel readbacks."
            }
        }
        $dmaBytes=if($UnalignedByte) { @(1,1,1,3,6,3)[$ByteCase] } else { 4 }
        $dispatches=if($UnalignedByte) { 4 } else { 3 }
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne $dispatches -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1 [^\r\n]*textures=1 ').Count -ne $dispatches -or
           [regex]::Matches($output,"agc.dma_packet [^\r\n]*bytes=$dmaBytes src_sel=2 fill=True copied=True").Count -ne 1 -or
           [regex]::Matches($output,'vk.cpu_image_partial_upload [^\r\n]*spans=1 [^\r\n]*success=True').Count -ne 1) { throw 'Expected GPU seed, a one-texel CPU DMA and two independent GPU readbacks preserving its GPU-only neighbor.' }
    } elseif($LdsPaired) {
        $pairedDispatches=if($NggGroupFoundation) { 12 } else { 8 }
        if($NggGroupFoundation -and [regex]::Matches($output,'agc.compute_shader [^\r\n]*wave=32 local=64x1x1 ').Count -ne 2) {
            throw 'Expected two translated wave32 shaders with 64 local participants'
        }
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne $pairedDispatches -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1 [^\r\n]*textures=0 ').Count -ne $pairedDispatches -or
           [regex]::Matches($output,'agc.compute_shader [^\r\n]*local=64x1x1 ').Count -ne 2 -or
           -not $output.Contains('DsWrite2B32') -or
           -not $output.Contains('DsWrite2St64B32')) { throw 'Expected paired and scaled-offset LDS stores under inactive/lane-0/32/63 masks.' }
    } elseif($LdsWide) {
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne 12 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1 [^\r\n]*textures=0 ').Count -ne 12 -or
           [regex]::Matches($output,'agc.compute_shader [^\r\n]*local=64x1x1 ').Count -ne 3 -or
           -not $output.Contains('DsWriteB64') -or
           -not $output.Contains('DsWriteB96') -or
           -not $output.Contains('DsWriteB128')) { throw 'Expected all three LDS store widths under inactive/lane-0/32/63 masks.' }
    } elseif($LdsAtomics) {
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne 19 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1 [^\r\n]*textures=0 ').Count -ne 19 -or
           [regex]::Matches($output,'agc.compute_shader [^\r\n]*local=64x1x1 ').Count -ne 5 -or
           -not $output.Contains('DsIncRtnU32') -or
           -not $output.Contains('DsDecRtnU32')) { throw 'Expected eleven scalar LDS boundaries, four shared cycles and four masked writes.' }
    } elseif($BoundedAtomics) {
        $textureCount=if($BufferAtomics) { 0 } else { 1 }
        $prefix=if($BufferAtomics) { 'Buffer' } else { 'Image' }
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne 39 -or
           [regex]::Matches($output,"vk.compute_dispatch groups=2x1x1 [^\r\n]*textures=$textureCount ").Count -ne 11 -or
           [regex]::Matches($output,"vk.compute_dispatch groups=1x1x1 [^\r\n]*textures=$textureCount ").Count -ne 26 -or
           [regex]::Matches($output,"vk.compute_dispatch groups=64x1x1 [^\r\n]*textures=$textureCount ").Count -ne 2 -or
           -not $output.Contains($prefix+'AtomicInc') -or
           -not $output.Contains($prefix+'AtomicDec')) { throw 'Expected eleven bounded INC/DEC cases and two 64-workgroup cycles, each with GPU seed, contended update and separate readback.' }
    } elseif($IndirectDimensions) {
        $expectedX=if($BaseRelative) { 1 } else { 2 }
        $expectedY=if($BaseRelative) { 1 } else { 0 }
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne 8 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1').Count -ne 6 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=2x1x1').Count -ne $expectedX -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x2x1').Count -ne $expectedY) { throw "Unexpected indirect dispatch sizes: $log" }
    } elseif($ImageAtomics) {
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne 5 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=2x1x1 [^\r\n]*textures=1 ').Count -ne 2 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1 [^\r\n]*textures=1 ').Count -ne 3) { throw 'Expected one GPU seed, two contended image atomic dispatches and two separate image readbacks.' }
    } elseif ([regex]::Matches($output,'vk.compute_dispatch groups=1x1x1').Count -ne 2) { throw "Expected two Vulkan executions: $log" }
    if ($output.Contains('agc.compute_semantic_fast_path')) { throw 'Probe unexpectedly used a CPU semantic replacement.' }
    $expectedSubmissions=if($UnalignedByte) { 4 } elseif($PartialImage) { 3 } elseif($LdsPaired) { if($NggGroupFoundation) { 12 } else { 8 } } elseif($LdsWide) { 12 } elseif($LdsAtomics) { 19 } elseif($BoundedAtomics) { 39 } else { 5 }
    if($ImageAtomicsCrossQueue -and
        ([regex]::Matches($output,'agc.driver_submit_acb owner=7 [^\r\n]*dwords=70 ').Count -ne 3 -or
         [regex]::Matches($output,'agc.driver_submit_dcb [^\r\n]*dwords=77 ').Count -ne 2 -or
         [regex]::Matches($output,'agc.wait_suspended [^\r\n]*queue=dcb.graphics ').Count -ne 2 -or
         [regex]::Matches($output,'agc.dcb.resumed ').Count -ne 2 -or
         [regex]::Matches($output,'agc.queue_resumed queue=dcb.graphics [^\r\n]*remaining_dwords=70 ').Count -ne 2 -or
         $output.Contains('agc.deadlock_break ') -or $output.Contains('forced=True'))) { throw 'Expected three ACB producers and two genuinely suspended/resumed graphics image consumers.' }
    if(($BoundedAtomics -or $ImageAtomics) -and -not ($ComputeQueue -or $ImageAtomicsCrossQueue) -and
        ([regex]::Matches($output,'agc.driver_submit_dcb ').Count -ne $expectedSubmissions -or
         $output.Contains('agc.driver_submit_acb '))) { throw "Expected $expectedSubmissions graphics-queue submissions only: $log" }
    if($ComputeQueue -and
        ([regex]::Matches($output,'agc.driver_submit_acb owner=7 ').Count -ne $expectedSubmissions -or
         $output.Contains('agc.driver_submit_dcb '))) { throw "Expected $expectedSubmissions compute-queue submissions only: $log" }
    if($UnalignedByte) { Write-Output "PASS: Byte-span R32 DMA case=$ByteCase preserved GPU-only bytes; separate GPU reads and CPU/output guards passed. SignedImage=$SignedImage ComputeQueue=$ComputeQueue" }
    elseif($PartialImage) { Write-Output "PASS: Partial R32 CPU DMA changed only texel 1 to 123; separate GPU reads preserved GPU-only texel 0 at 7 while CPU backing stayed 99, with unchanged output and neighboring CPU guards. SignedImage=$SignedImage ComputeQueue=$ComputeQueue" }
    elseif($ImageAtomicsCrossQueue) { Write-Output "PASS: ACB image atomics returned unique old values; two graphics consumers suspended before their producers, resumed through real labels, and read sums 13/23 with unchanged CPU image backing and output guards." }
    elseif($NggGroupFoundation) { Write-Output "PASS: independent wave32 group sharing, local indices 0..63, live tails 0/13/32/33/63/64, paired/scaled LDS and guards; not Gears rendering." }
    elseif($LdsPaired) { Write-Output "PASS: Paired LDS stores at ordinary and ST64-scaled offsets verified each component through independent dword reads on all 64 invocations, untouched LDS locations, per-record padding and trailing output guards. ComputeQueue=$ComputeQueue" }
    elseif($LdsWide) { Write-Output "PASS: Masked 64/96/128-bit LDS stores verified each component through independent dword reads on all 64 invocations, a neighboring LDS guard, per-record padding and trailing output guards. ComputeQueue=$ComputeQueue" }
    elseif($LdsAtomics) { Write-Output "PASS: LDS INC/DEC boundaries, 64-invocation shared cycles and inactive/lane-0/32/63 writes verified old values, final readback and output guards. ComputeQueue=$ComputeQueue" }
    elseif($BoundedAtomics) { Write-Output "PASS: Eleven GPU bounded INC/DEC cases and two 64-workgroup cycles verified old values, unsigned limit/wrap boundaries, separate readback and unchanged guards. BufferAtomics=$BufferAtomics SignedImage=$SignedImage ComputeQueue=$ComputeQueue" }
    elseif($ImageAtomics) { Write-Output 'PASS: R32_UINT GPU image atomics returned unique old values for both invocations, preserved output guards and produced sums 13/23 through separate GPU image readbacks. CPU image backing remained 99/100/101/102.' }
    elseif($IndirectDimensions) { Write-Output "PASS: Atomic baseline and GPU-produced indirect dimensions (1 -> 2, 2 -> 1, 0 -> 2, 2 -> 0), completion and buffer guards match. BaseRelative=$BaseRelative ComputeQueue=$ComputeQueue" }
    else { Write-Output 'PASS: Two translated compute dispatches with different inputs, EXEC masking, completion and guest-memory readback match.' }
}
finally { $env:SHARPEMU_LOG_AGC = $oldAgc }
