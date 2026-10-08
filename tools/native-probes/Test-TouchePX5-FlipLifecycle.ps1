# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later

[CmdletBinding()]
param(
    [ValidateSet('cpu','burst','direct','ordered')][string]$Mode='ordered',
    [ValidateSet('none','acquire','present')][string]$Retry='none',
    [switch]$WindowRecovery,
    [switch]$TiledCpu
)
$ErrorActionPreference='Stop'
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if($TiledCpu -and ($Mode -ne 'cpu' -or $WindowRecovery)) { throw 'Tiled CPU fixture requires cpu mode without window recovery.' }
if($WindowRecovery -and ($Mode -eq 'burst' -or $Retry -ne 'none')) {
    throw 'Window recovery requires a sequential mode and no injected retry.'
}
$suffix=if($WindowRecovery) { '-window' } else { '' }
if($TiledCpu) { $suffix+='-tiled' }
$root=Join-Path $sourceRoot "artifacts\smoke-flip-$Mode-$Retry$suffix"
New-Item -ItemType Directory -Path $root -Force | Out-Null
Remove-Item -LiteralPath (Join-Path $root 'verification.json') -Force -ErrorAction SilentlyContinue
$fixture=Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$llvm='C:\BuildTools\VC\Tools\Llvm\x64\bin'
$defines=@()
if($WindowRecovery) { $defines+='-DWINDOW_RECOVERY=1' }
if($Mode -eq 'cpu') { $defines+='-DCPU_FLIP=1' }
if($TiledCpu) { $defines+='-DTILED_CPU_FLIP=1' }
if($Mode -eq 'burst') { $defines+=@('-DCPU_FLIP=1','-DBURST_FLIP=1') }
if($Mode -eq 'ordered') { $defines+='-DORDERED_FLIP=1' }
& "$llvm\clang.exe" --target=x86_64-pc-linux-gnu -O1 -ffreestanding -fno-builtin -fno-stack-protector -fpie -mno-sse @defines -c (Join-Path $fixture 'flip-lifecycle.c') -o (Join-Path $root 'guest.o')
if($LASTEXITCODE -ne 0) { throw 'Flip guest compilation failed.' }
& "$llvm\clang.exe" --target=x86_64-pc-linux-gnu -DFLIP_LIFECYCLE=1 -c (Join-Path $fixture 'imports.S') -o (Join-Path $root 'imports.o')
if($LASTEXITCODE -ne 0) { throw 'Flip imports compilation failed.' }
$guest=Join-Path $root 'flip.elf'
& "$llvm\ld.lld.exe" -T (Join-Path $PSScriptRoot 'fixtures\ps5-touche-input-demo\demo.ld') -o $guest (Join-Path $root 'guest.o') (Join-Path $root 'imports.o')
if($LASTEXITCODE -ne 0) { throw 'Flip guest link failed.' }
$bytes=[IO.File]::ReadAllBytes($guest)
$bytes[7]=9; $bytes[8]=2; $bytes[16]=0x10; $bytes[17]=0xfe
[IO.File]::WriteAllBytes($guest,$bytes)
$log=Join-Path $root 'flip.log'
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$settings=@{
    SHARPEMU_DIAGNOSTIC_FLIP_DELAY_MS='300'
    SHARPEMU_DIAGNOSTIC_FLIP_RETRY=$Retry
    SHARPEMU_DIAGNOSTIC_FLIP_RETRY_FRAME='2'
    SHARPEMU_TRACE_GUEST_IMAGES='present'
    SHARPEMU_GUEST_IMAGE_DUMP_DIR=(Join-Path $root ('gpu-'+[DateTime]::UtcNow.Ticks))
    SHARPEMU_GUEST_IMAGE_DUMP_CONTINUOUS='1'
    SHARPEMU_OVERLAY='0'
    SHARPEMU_LOG_AGC='1'
}
$previous=@{}
try {
    if($WindowRecovery) {
        Write-Output 'INTERACTIVE: minimize the native VideoOut window after frame 1; restore at least 36 seconds after frame 1. This test does not control the window itself.'
    }
    foreach($key in $settings.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$settings[$key],'Process')
    }
    $arguments=@('--cpu-engine=native','--fps-limit=60','--log-level=info',"--log-file=$log",$guest)
    if($TiledCpu) { $arguments=@('--generation=ps5')+$arguments }
    $process=Start-Process -FilePath (Join-Path $sourceRoot 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        $timeoutMs=if($WindowRecovery) { 120000 } else { 45000 }
        if(-not $process.WaitForExit($timeoutMs)) {
            $process.Kill()
            $process.WaitForExit(5000) | Out-Null
            throw "Flip lifecycle guest timed out: $log"
        }
        if($process.ExitCode -ne 0) { throw "Flip lifecycle process failed ($($process.ExitCode)): $log" }
    } finally { $process.Dispose() }
    $output=Get-Content -LiteralPath $log -Raw
    if(-not $output.Contains('[RUNTIME] DispatchEntry returned: ORBIS_GEN2_OK')) { throw "Guest lifecycle assertions failed: $log" }
    if($WindowRecovery) {
        $deferred=$output.IndexOf('vk.present_deferred window-unavailable')
        $resumed=$output.IndexOf('vk.present_resumed window-restored')
        if($deferred -lt 0 -or $resumed -le $deferred) { throw 'Real window defer/resume was not observed.' }
        if(-not $output.Contains('Vulkan VideoOut recreated swapchain:')) { throw 'Restored window did not recreate its swapchain.' }
    }
    $expectedFrames=if($Mode -eq 'burst') { 6 } else { 3 }
    if([regex]::Matches($output,'vk.flip_deferred ').Count -ne $expectedFrames) { throw "Expected exactly $expectedFrames deferred flips." }
    if($Retry -ne 'none') {
        if([regex]::Matches($output,"vk.flip_retry_injected phase=$Retry ").Count -ne 1) { throw 'Retry path did not execute exactly once.' }
        if(-not $output.Contains('Vulkan VideoOut recreated swapchain:')) { throw 'Swapchain was not recreated.' }
        if($Mode -eq 'ordered' -and -not $output.Contains("vk.flip_retry_injected phase=$Retry version=1")) { throw 'Retry did not retain an owned capture.' }
    }
    if($Mode -eq 'ordered' -and [regex]::Matches($output,'vk.flip_capture version=').Count -ne 2) { throw 'Expected two ordered GPU captures.' }
    if($Mode -eq 'ordered') {
        $capture=$output.IndexOf('vk.flip_capture version=1 ')
        $overwrite=[regex]::Match($output,"vk.ordered_action [^\r\n]*name='dma_data dst=0x[0-9A-F]+ bytes=230400'")
        $present=[regex]::Match($output,'vk.present_taken [^\r\n]*version=1 ')
        if(-not $overwrite.Success -or -not $present.Success -or
            $overwrite.Index -le $capture -or $overwrite.Index -ge $present.Index) {
            throw 'The live image overwrite did not execute between capture and presentation.'
        }
    }
    $pixelMode=if($TiledCpu) { 'tiled' } else { $Mode }
    & py -3 (Join-Path $PSScriptRoot 'verify-flip-lifecycle.py') $log $pixelMode (Join-Path $root 'verification.json')
    if($LASTEXITCODE -ne 0) { throw 'Presented pixel oracle failed.' }
    Write-Output "PASS flip lifecycle mode=$Mode retry=$Retry windowRecovery=$WindowRecovery; status, pending, events and pixels: $log"
} finally {
    foreach($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
