# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$FreezeView,[switch]$ReverseObjects,[switch]$DisableDepth,[switch]$SkipBackgroundWrites)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
& "$PSScriptRoot/Build-PS5LinkOverlappingCubes.ps1" -DistinctTextures -FreezeView:$FreezeView -ReverseObjects:$ReverseObjects -DisableDepth:$DisableDepth
$parent='texture-overlap-pair'
if($DisableDepth) { $parent+='-disabled' }
if($ReverseObjects) { $parent+='-reverse' }
if($FreezeView) { $parent+='-static' }
$text=[IO.File]::ReadAllText("$out/main-$parent.c")
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Generated source contract changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
if($SkipBackgroundWrites) {
    Replace-Checked 'for (unsigned long i = 0; i < n; i++) p[i] = 0xFF201828u;' '(void)p; (void)n; /* negative control: no CPU background stores */'
}
Replace-Checked 'extern int sceAgcSuspendPoint(void);' @'
extern int sceAgcSuspendPoint(void);
extern int sceKernelUsleep(unsigned int micros);
extern unsigned long long sceKernelGetProcessTime(void);
'@
Replace-Checked 'int main(void) {' @'
static int retire_cube_slot(volatile unsigned long long *completion) {
    unsigned long long start = sceKernelGetProcessTime();
    while (*completion != 0) {
        if (sceKernelGetProcessTime() - start >= 5000000ULL) return 0;
        sceKernelUsleep(1000);
    }
    return 1;
}
int main(void) {
'@
Replace-Checked 'const int FRAMES = 600;' @'
DirectMem frame_fence[2], mutable_texture[2];
    mutable_texture[0] = texture_second;
    ALLOC_OR_FAIL(mutable_texture[1], 64 * 64 * 4, DIRECT_MEM_MIN_ALIGN, "texture-slot1");
    volatile unsigned long long *fences[2];
    for (int i = 0; i < 2; i++) {
        ALLOC_OR_FAIL(frame_fence[i], 8, DIRECT_MEM_MIN_ALIGN, "frame-fence-slot");
        fences[i] = frame_fence[i].ptr; *fences[i] = 0;
    }
    unsigned int occupied[2] = {0,0};
    unsigned int submitted = 0, retired = 0, reuses = 0, max_pending = 0;
    const int FRAMES = 600;
'@
$phase=if($FreezeView) { '1u' } else { '(1u + (unsigned int)(frame / 150)) % 3' }
Replace-Checked 'int slot = frame & 1;' @"
int slot = frame & 1;
        volatile unsigned long long *completion = fences[slot];
        if (occupied[slot]) {
            if (!retire_cube_slot(completion)) { notify("mesh_test: cube slot reuse timeout"); return 4; }
            occupied[slot] = 0; retired++; reuses++;
        }
        /* Depth, commands, matrices and mutable texture all belong to this slot. */
        second_pixels = mutable_texture[slot].ptr;
        unsigned long long slot_texture_address = (unsigned long long)(unsigned long)mutable_texture[slot].ptr;
        second_texture_words[0] = (unsigned int)(slot_texture_address >> 8);
        second_texture_words[1] = (unsigned int)(slot_texture_address >> 40) | (56u << 20) | (3u << 30);
        unsigned int phase = $phase;
        for (unsigned int i = 0; i < 64 * 64 * 4; i += 4) {
            for (unsigned int c = 0; c < 3; c++) second_pixels[i+c] = pixels[i+(c+phase)%3];
            second_pixels[i+3] = 255;
        }
"@
Replace-Checked 'struct { void *words; unsigned int wordCount; unsigned char flag; } submit;' @'
/* Terminal RELEASE_MEM follows the immutable ordered-flip copy. */
        if (dcb->down_cursor - dcb->up_cursor < 8) { notify("mesh_test: cube fence capacity"); return 5; }
        *completion = 1;
        unsigned long long fence_address = (unsigned long long)(unsigned long)frame_fence[slot].ptr;
        unsigned int *release = dcb->up_cursor;
        release[0] = 0xc0064900u; release[1] = 0x06000528u; release[2] = 0x42010000u;
        release[3] = (unsigned int)fence_address; release[4] = (unsigned int)(fence_address >> 32);
        release[5] = 0; release[6] = 0; release[7] = 0; dcb->up_cursor += 8;
        struct { void *words; unsigned int wordCount; unsigned char flag; } submit;
'@
Replace-Checked 'sceVideoOutWaitVblank(vhandle);' @'
occupied[slot] = 1; submitted++;
        unsigned int pending = (*fences[0] != 0) + (*fences[1] != 0);
        if (pending > max_pending) max_pending = pending;
        sceVideoOutWaitVblank(vhandle);
'@
Replace-Checked 'notify("mesh_test: fin");' @'
for (int slot = 0; slot < 2; slot++) if (occupied[slot]) {
        if (!retire_cube_slot(fences[slot])) { notify("mesh_test: cube final drain timeout"); return 4; }
        occupied[slot] = 0; retired++;
    }
    if (submitted != 600 || retired != 600 || reuses != 598 || max_pending > 2) {
        notify("mesh_test: cube frame accounting failed"); return 6;
    }
    notify_vals("frame_slots", "submitted", submitted, "retired", retired,
                "max_pending", max_pending, "reuses", reuses);
    notify("mesh_test: fin");
'@
$suffix='texture-cube-slots'
if($DisableDepth) { $suffix+='-disabled' }
if($ReverseObjects) { $suffix+='-reverse' }
if($SkipBackgroundWrites) { $suffix+='-no-background' }
if($FreezeView) { $suffix+='-static' }
[IO.File]::WriteAllText("$out/main-$suffix.c",$text)
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-$suffix.c" -o "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Cube frame slots compilation failed' }
& "$out/link_real.exe" "$out/app-$suffix.elf" "$out/crt1.o" "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Cube frame slots link failed' }
$report=Get-Content "$out/build-report-$parent.json" -Raw | ConvertFrom-Json
$report | Add-Member FrameOwnership 'two cube slots; retire before CPU reuse; final drain'
$report | Add-Member FrameSlotCount 2
$report | Add-Member MutableTexturePerSlot $true
$report | Add-Member TerminalGpuFence $true
$report | Add-Member TextureCpuUpdates (-not $FreezeView)
$report | Add-Member CpuBackgroundWrites (-not $SkipBackgroundWrites)
$report.GeneratedSourceSha256=(Get-FileHash "$out/main-$suffix.c").Hash
$report.ElfSha256=(Get-FileHash "$out/app-$suffix.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-$suffix.json" -Encoding utf8
