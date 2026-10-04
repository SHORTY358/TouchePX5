# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later

[CmdletBinding()]
param(
    [ValidateSet('cpu','burst','direct','ordered')][string]$Mode='ordered',
    [ValidateSet('none','acquire','present')][string]$Retry='none'
)
$ErrorActionPreference='Stop'
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$root=Join-Path $sourceRoot "artifacts\smoke-flip-$Mode-$Retry"
New-Item -ItemType Directory -Path $root -Force | Out-Null
Remove-Item -LiteralPath (Join-Path $root 'verification.json') -Force -ErrorAction SilentlyContinue
$fixture=Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$llvm='C:\BuildTools\VC\Tools\Llvm\x64\bin'
$defines=@()
if($Mode -eq 'cpu') { $defines+='-DCPU_FLIP=1' }
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
    foreach($key in $settings.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$settings[$key],'Process')
    }
    $process=Start-Process -FilePath (Join-Path $sourceRoot 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe') -ArgumentList @('--cpu-engine=native','--fps-limit=60','--log-level=info',"--log-file=$log",$guest) -WindowStyle Hidden -PassThru
    try {
        if(-not $process.WaitForExit(45000)) {
            $process.Kill()
            $process.WaitForExit(5000) | Out-Null
            throw "Flip lifecycle guest timed out: $log"
        }
        if($process.ExitCode -ne 0) { throw "Flip lifecycle process failed ($($process.ExitCode)): $log" }
    } finally { $process.Dispose() }
    $output=Get-Content -LiteralPath $log -Raw
    if(-not $output.Contains('[RUNTIME] DispatchEntry returned: ORBIS_GEN2_OK')) { throw "Guest lifecycle assertions failed: $log" }
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
    & py -3 (Join-Path $PSScriptRoot 'verify-flip-lifecycle.py') $log $Mode (Join-Path $root 'verification.json')
    if($LASTEXITCODE -ne 0) { throw 'Presented pixel oracle failed.' }
    Write-Output "PASS flip lifecycle mode=$Mode retry=$Retry; status, pending, events and pixels: $log"
} finally {
    foreach($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
