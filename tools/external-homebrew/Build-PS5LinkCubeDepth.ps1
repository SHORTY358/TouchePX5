# Copyright (C) 2026 Touché PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$DisableDepth,[switch]$FreezeView,[switch]$CpuDepthClear,[switch]$ReverseFaces)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
& "$PSScriptRoot/Build-PS5LinkCube.ps1" -CorrectInitAbi
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$text=[IO.File]::ReadAllText("$out/main-init-abi.c")
$suffix=if($DisableDepth) {'-depth-disabled'} else {'-depth'}
if($CpuDepthClear) { $suffix+='-cpu-clear' }
if($ReverseFaces) { $suffix+='-reverse' }
if($FreezeView) { $suffix+='-static' }
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Upstream source shape changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
Replace-Checked 'SceVideoOutBuffers addresses[2];' @'
DirectMem depth[2];
    for (int i = 0; i < BUF_COUNT; i++)
        ALLOC_OR_FAIL(depth[i], frame_bytes, 65536, "depth");
    SceVideoOutBuffers addresses[2];
'@
Replace-Checked 'CxRegister context[16 + 14 + 1 + 34 + 32 + 32];' 'CxRegister context[16 + 14 + 1 + 34 + 32 + 32 + 22];'
# D32/no-HTILE register layout is cross-checked against the pinned public
# AGC Gears depth-target contracts. This generated scene is a local adaptation,
# not an unchanged upstream/hardware-validated Gears executable.
$depthControl=if($DisableDepth) {'0u'} else {'0xB6u'}
$depth=@'
        {
            unsigned int *z = (unsigned int *)depth[slot].ptr;
            for (unsigned long i = 0; i < depth[slot].size / 4; i++) z[i] = 0x3F800000u;
            unsigned long long a = (unsigned long long)(unsigned long)depth[slot].ptr;
            unsigned int lo = (unsigned int)(a >> 8), hi = (unsigned int)(a >> 40) & 255;
            CxRegister zr[22] = {
                {0x000,0,0x60}, {0x01F,0,0}, {0x002,0,0}, {0x004,0,0}, {0x005,0,0},
                {0x007,0,((H-1u)<<16)|(W-1u)}, {0x011,0,0x20000180}, {0x012,0,lo},
                {0x013,0,0}, {0x014,0,lo}, {0x015,0,0}, {0x2AF,0,0}, {0x2DE,0,0x1E9},
                {0x092,0,0}, {0x01A,0,hi}, {0x01C,0,hi}, {0x01B,0,0}, {0x01D,0,0},
                {0x01E,0,0}, {0x003,0,0x2A}, {0x010,0,0x183}, {0x200,0,DEPTH_CONTROL}
            };
            for (int i = 0; i < 22; i++) context[cx++] = zr[i];
        }
'@.Replace('DEPTH_CONTROL',$depthControl)
Replace-Checked 'for (int i = 0; i < 14; i++) context[cx++] = vp[i];' ("for (int i = 0; i < 14; i++) context[cx++] = vp[i];`n"+$depth)
Replace-Checked 'mode = (mode & ~0x7u) | 0x2u /* CULL_BACK */ | 0x4u /* front = CW */;' 'mode = (mode & ~0x7u) | 0x4u; /* depth probe: both sides submitted */'
if(-not $CpuDepthClear) {
    # A real draw writes depth=1 over the viewport. No DB fast-clear bit or
    # emulator-only unconditional clear: use the same mesh vertex shader with
    # identity transforms, color writes masked, and ALWAYS depth comparison.
    Replace-Checked '/* Where the vertex program wants its two resources. */' @'
DirectMem clear_vbuf, clear_ibuf, clear_cons;
    ALLOC_OR_FAIL(clear_vbuf, 3 * VERTEX_STRIDE, DIRECT_MEM_MIN_ALIGN, "clear_vbuf");
    ALLOC_OR_FAIL(clear_ibuf, 3 * 4, DIRECT_MEM_MIN_ALIGN, "clear_ibuf");
    ALLOC_OR_FAIL(clear_cons, 128, DIRECT_MEM_MIN_ALIGN, "clear_cons");
    put_vertex(clear_vbuf.ptr, 0, -1, -1, 1, 0, 0, 1, 0xFFFFFFFFu);
    put_vertex(clear_vbuf.ptr, 1,  3, -1, 1, 0, 0, 1, 0xFFFFFFFFu);
    put_vertex(clear_vbuf.ptr, 2, -1,  3, 1, 0, 0, 1, 0xFFFFFFFFu);
    for (int i = 0; i < 3; i++) ((unsigned int*)clear_ibuf.ptr)[i] = i;
    mat_identity((float*)clear_cons.ptr);
    mat_identity((float*)clear_cons.ptr + 16);
    unsigned int clear_cb[4], clear_vb[4];
    desc_constant(clear_cb, (unsigned long long)(unsigned long)clear_cons.ptr, 128);
    desc_structured(clear_vb, (unsigned long long)(unsigned long)clear_vbuf.ptr, VERTEX_STRIDE, 3);

    /* Where the vertex program wants its two resources. */
'@
    $clearDraw=@'
        /* Fullscreen depth reset, followed by the actual cube draw. */
        sceAgcDcbSetCxRegisterDirect(dcb, (0xF6ULL << 32) | 0x200ULL);
        sceAgcDcbSetCxRegisterDirect(dcb, 0x8EULL); /* color write mask = 0 */
        sceAgcCbSetShRegisterRangeDirect(dcb, GS_USER_DATA_BASE + (unsigned int)cb_dwo, clear_cb, 4);
        sceAgcCbSetShRegisterRangeDirect(dcb, GS_USER_DATA_BASE + (unsigned int)vb_dwo, clear_vb, 4);
        sceAgcDcbSetIndexSize(dcb, 1, 0);
        sceAgcDcbSetIndexBuffer(dcb, clear_ibuf.ptr);
        sceAgcDcbSetIndexCount(dcb, 3);
        sceAgcDcbDrawIndex(dcb, 3, clear_ibuf.ptr, 0);
        sceAgcDcbSetCxRegisterDirect(dcb, (DEPTH_CONTROL << 32) | 0x200ULL);
        sceAgcDcbSetCxRegisterDirect(dcb, (0xFULL << 32) | 0x8EULL);

        /* Both resources belong to the vertex stage. */
'@.Replace('DEPTH_CONTROL', $(if($DisableDepth) {'0ULL'} else {'0xB6ULL'}))
    Replace-Checked '/* Both resources belong to the vertex stage. */' $clearDraw
    Replace-Checked 'for (unsigned long i = 0; i < depth[slot].size / 4; i++) z[i] = 0x3F800000u;' '/* Depth is reset by a fullscreen GPU draw below. */'
}
if($FreezeView) { Replace-Checked 'float angle = (float)frame * 0.02f;' 'float angle = 0.75f; /* static visibility control */' }
if($ReverseFaces) {
    Replace-Checked 'build_cube((unsigned char*)vbuf.ptr, (unsigned int*)ibuf.ptr);' @'
build_cube((unsigned char*)vbuf.ptr, (unsigned int*)ibuf.ptr);
    /* Reverse face submission order without changing any triangle winding. */
    for (int f = 0; f < 3; f++) {
        unsigned int *indices = (unsigned int*)ibuf.ptr;
        for (int j = 0; j < 6; j++) {
            unsigned int temp = indices[f * 6 + j];
            indices[f * 6 + j] = indices[(5 - f) * 6 + j];
            indices[(5 - f) * 6 + j] = temp;
        }
    }
'@
}
[IO.File]::WriteAllText("$out/main$suffix.c",$text)
$clang='C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe'
& $clang --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -c "$out/main$suffix.c" -o "$out/main$suffix.o"
if($LASTEXITCODE) { throw 'Depth scene compilation failed' }
& "$out/link_real.exe" "$out/app$suffix.elf" "$out/crt1.o" "$out/main$suffix.o"
if($LASTEXITCODE) { throw 'Depth scene link failed' }
@{
    BaseRepository='https://github.com/Rufidj/ps5link-sdk'
    BaseCommit='ea771e535378740b6a058b8e5419eb8a0e0e0ec8'
    DepthLayoutReference='mpereiraesaa/ps5-agc-gears@1ae1f9182abd2770c131b97419034fb85173c2dc'
    SourceModified=$true; StartupModified=$false; InitAbiCorrection=$true
    DepthEnabled=(-not $DisableDepth); BackFaceCulling=$false
    FrozenView=[bool]$FreezeView
    ReversedFaceOrder=[bool]$ReverseFaces
    DepthClearMethod=$(if($CpuDepthClear) {'cpu-memory-writes'} else {'fullscreen-gpu-draw'})
    ElfSha256=(Get-FileHash "$out/app$suffix.elf").Hash
    VisualCorrectnessVerified=$false
} | ConvertTo-Json | Set-Content -LiteralPath "$out/build-report$suffix.json" -Encoding utf8
