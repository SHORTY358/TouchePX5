# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$FreezeView,[switch]$ReverseObjects,[switch]$DisableDepth,[switch]$SkipBackgroundWrites,[switch]$PatternedBackground,[switch]$UseRedZoneControl)
$ErrorActionPreference='Stop'
if($PatternedBackground -and $SkipBackgroundWrites) { throw 'Patterned background requires CPU writes' }
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
if($PatternedBackground) {
    if(-not $text.Contains('const unsigned int W = 1920, H = 1080;')) {
        throw 'Pattern writer requires the pinned 1920x1080 fixture contract'
    }
    # Direct address-bit evaluation, independent of the runtime detiler.
    # AMD GFX10 RB+ 16-pipe/8-packer 64KiB R_X, 4 bytes per element:
    # nibble01=39, nibble2=307, nibble3=381.
    Replace-Checked 'int main(void) {' @'
static void fill_pattern(unsigned int *pixels, unsigned int phase) {
    static unsigned int xt[1920], yt[1080];
    static int ready;
    const unsigned int xm[16] = {0,0,1,2,0,0,4,0,128,16,64,32,8,0,64,128};
    const unsigned int ym[16] = {0,0,0,0,1,2,0,4,144,16,32,64,0,8,128,64};
    if (!ready) {
        for (unsigned int x=0; x<1920; x++) for (unsigned int bit=0; bit<16; bit++) {
            unsigned int v=x&xm[bit], parity=0;
            while(v) { parity^=v&1; v>>=1; }
            xt[x]|=parity<<bit;
        }
        for (unsigned int y=0; y<1080; y++) for (unsigned int bit=0; bit<16; bit++) {
            unsigned int v=y&ym[bit], parity=0;
            while(v) { parity^=v&1; v>>=1; }
            yt[y]|=parity<<bit;
        }
        ready=1;
    }
    for (unsigned int y=0; y<1080; y++) for (unsigned int x=0; x<1920; x++) {
        unsigned int b=32+((x>>6)&7)*24;
        unsigned int g=24+((y>>6)&7)*24;
        unsigned int r=40+(((x>>7)+(y>>7)*3)&7)*24;
        if(phase==1) { unsigned int t=b; b=g; g=r; r=t; }
        if(phase==2) { unsigned int t=b; b=r; r=g; g=t; }
        unsigned int offset=(((y>>7)*15+(x>>7))<<16)+(xt[x]^yt[y]);
        pixels[offset>>2]=0xff000000u|(r<<16)|(g<<8)|b;
    }
}
int main(void) {
'@
    $backgroundPhase=if($FreezeView) { '0u' } else { '(unsigned int)(frame / 150) % 3u' }
    Replace-Checked 'for (unsigned long i = 0; i < n; i++) p[i] = 0xFF201828u;' @"
(void)n; fill_pattern(p, $backgroundPhase);
          if(frame==0 || frame==149 || frame==299 || frame==449 || frame==599) {
              volatile unsigned int *probe=p;
              notify_vals("pattern_source", "frame", (unsigned int)frame,
                          "x32y32", probe[768], "x96y32", probe[4608], "phase", $backgroundPhase);
          }
"@
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
if($PatternedBackground) { $suffix+='-pattern' }
if($UseRedZoneControl) { $suffix+='-red-zone' }
if($FreezeView) { $suffix+='-static' }
[IO.File]::WriteAllText("$out/main-$suffix.c",$text)
[string[]]$stackOptions=@()
if(-not $UseRedZoneControl) { $stackOptions=@('-mno-red-zone') }
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector @stackOptions -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-$suffix.c" -o "$out/main-$suffix.o"
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
$report | Add-Member PatternedBackground ([bool]$PatternedBackground)
$report | Add-Member RedZoneEnabled ([bool]$UseRedZoneControl)
$report.GeneratedSourceSha256=(Get-FileHash "$out/main-$suffix.c").Hash
$report.ElfSha256=(Get-FileHash "$out/app-$suffix.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-$suffix.json" -Encoding utf8
