# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$ReverseObjects)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
& "$PSScriptRoot/Build-PS5LinkTextureBindings.ps1" -UpdateSecondTexture -FenceFrames -ReverseObjects:$ReverseObjects
$parent='texture-bindings'
if($ReverseObjects) { $parent+='-reverse' }
$parent+='-updates-fenced'
$text=[IO.File]::ReadAllText("$out/main-$parent.c")
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Generated source contract changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
Replace-Checked 'int main(void) {' @'
static int retire_frame_slot(volatile unsigned long long *completion) {
    unsigned long long start = sceKernelGetProcessTime();
    while (*completion != 0) {
        if (sceKernelGetProcessTime() - start >= 5000000ULL) return 0;
        sceKernelUsleep(1000);
    }
    return 1;
}

int main(void) {
'@
Replace-Checked @'
DirectMem frame_fence;
    ALLOC_OR_FAIL(frame_fence, 8, DIRECT_MEM_MIN_ALIGN, "frame-fence");
    volatile unsigned long long *completion = frame_fence.ptr;
'@ @'
DirectMem frame_fence[2], mutable_texture[2];
    mutable_texture[0] = texture_second;
    ALLOC_OR_FAIL(mutable_texture[1], 64 * 64 * 4, DIRECT_MEM_MIN_ALIGN, "texture-slot1");
    volatile unsigned long long *fences[2];
    for (int i = 0; i < 2; i++) {
        ALLOC_OR_FAIL(frame_fence[i], 8, DIRECT_MEM_MIN_ALIGN, "frame-fence-slot");
        fences[i] = frame_fence[i].ptr;
        *fences[i] = 0;
    }
    unsigned int occupied[2] = {0, 0};
    unsigned int submitted = 0, retired = 0, reuses = 0, max_pending = 0;
'@
Replace-Checked 'int slot = frame & 1;' @'
int slot = frame & 1;
        volatile unsigned long long *completion = fences[slot];
        if (occupied[slot]) {
            if (!retire_frame_slot(completion)) {
                notify("mesh_test: slot reuse GPU fence timeout"); return 4;
            }
            occupied[slot] = 0; retired++; reuses++;
        }
        /* Every mutable resource belongs to this slot, including its texture.
         * The other slot's queued draw can keep sampling its own bytes. */
        second_pixels = mutable_texture[slot].ptr;
        unsigned long long slot_texture_address = (unsigned long long)(unsigned long)mutable_texture[slot].ptr;
        second_texture_words[0] = (unsigned int)(slot_texture_address >> 8);
        second_texture_words[1] = (unsigned int)(slot_texture_address >> 40) | (56u << 20) | (3u << 30);
'@
Replace-Checked '(unsigned long)frame_fence.ptr' '(unsigned long)frame_fence[slot].ptr'
Replace-Checked @'
unsigned long long wait_start = sceKernelGetProcessTime();
        while (*completion != 0) {
            if (sceKernelGetProcessTime() - wait_start >= 5000000ULL) {
                notify("mesh_test: terminal GPU fence timeout"); return 4;
            }
            sceKernelUsleep(1000);
        }
'@ @'
occupied[slot] = 1; submitted++;
        unsigned int pending = (*fences[0] != 0) + (*fences[1] != 0);
        if (pending > max_pending) max_pending = pending;
'@
Replace-Checked 'notify("mesh_test: fin");' @'
for (int slot = 0; slot < 2; slot++) {
        if (occupied[slot]) {
            if (!retire_frame_slot(fences[slot])) {
                notify("mesh_test: final slot GPU fence timeout"); return 4;
            }
            occupied[slot] = 0; retired++;
        }
    }
    if (submitted != 600 || retired != 600 || reuses != 598 || max_pending > 2) {
        notify("mesh_test: frame slot accounting failed"); return 6;
    }
    notify_vals("frame_slots", "submitted", submitted, "retired", retired,
                "max_pending", max_pending, "reuses", reuses);
    notify("mesh_test: fin");
'@
Replace-Checked 'This diagnostic serializes frames;' 'This diagnostic retires each slot before reuse;'
$suffix='texture-frame-slots'
if($ReverseObjects) { $suffix+='-reverse' }
[IO.File]::WriteAllText("$out/main-$suffix.c",$text)
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-$suffix.c" -o "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Frame slots compilation failed' }
& "$out/link_real.exe" "$out/app-$suffix.elf" "$out/crt1.o" "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Frame slots link failed' }
$report=Get-Content "$out/build-report-$parent.json" -Raw | ConvertFrom-Json
$report.FrameOwnership='two slots; per-slot GPU retirement before CPU reuse; final drain'
$report | Add-Member FrameSlotCount 2
$report | Add-Member MutableTexturePerSlot $true
$report.GeneratedSourceSha256=(Get-FileHash "$out/main-$suffix.c").Hash
$report.ElfSha256=(Get-FileHash "$out/app-$suffix.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-$suffix.json" -Encoding utf8
