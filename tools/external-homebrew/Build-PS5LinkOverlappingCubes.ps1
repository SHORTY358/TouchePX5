# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$FreezeView,[switch]$ReverseObjects,[switch]$DisableDepth)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
if(-not (Test-Path "$out/main-texture-depth.c")) { & "$PSScriptRoot/Build-PS5LinkTextureCube.ps1" }
$text=[IO.File]::ReadAllText("$out/main-texture-depth.c")
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Generated source contract changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
Replace-Checked 'prim_m[2], cons_m[2];' 'prim_m[2], cons_m[2], cons_second[2];'
Replace-Checked 'ALLOC_OR_FAIL(cons_m[i], 256,     DIRECT_MEM_MIN_ALIGN, "cons");' @'
ALLOC_OR_FAIL(cons_m[i], 256,     DIRECT_MEM_MIN_ALIGN, "cons");
        ALLOC_OR_FAIL(cons_second[i], 256, DIRECT_MEM_MIN_ALIGN, "cons-second");
'@
if($FreezeView) { Replace-Checked 'float angle = (float)frame * 0.02f;' 'float angle = 0.75f;' }
Replace-Checked 'mat_mul(mv, model, view);' @'
float offset[16], placed[16];
        mat_translate(offset, -0.45f, 0.0f, 0.0f);
        mat_mul(placed, model, offset);
        mat_mul(mv, placed, view);
'@
Replace-Checked '/* Clear this frame' @'
/* A second object owns separate constants for each frame in flight.
         * Neither draw can observe the other object's later matrix write. */
        float *second = (float*)cons_second[slot].ptr;
        mat_rot_y(rot_y, -angle * 0.7f);
        mat_rot_x(rot_x, angle * 0.3f);
        mat_mul(model, rot_x, rot_y);
        mat_translate(offset, 0.45f, 0.12f, 0.65f);
        mat_mul(placed, model, offset);
        mat_mul(mv, placed, view);
        mat_mul(mvp, mv, proj);
        for (int i = 0; i < 16; i++) second[i] = mvp[i];
        mat_identity(second + 16);
        unsigned int second_cb[4];
        desc_constant(second_cb, (unsigned long long)(unsigned long)cons_second[slot].ptr, 128);

        /* Clear this frame
'@
$first='cb_words'; $last='second_cb'
if($ReverseObjects) { $first='second_cb'; $last='cb_words' }
Replace-Checked 'sceAgcDcbDrawIndex(dcb, CUBE_INDICES, ibuf.ptr, 0);' @"
sceAgcCbSetShRegisterRangeDirect(dcb, GS_USER_DATA_BASE + (unsigned int)cb_dwo, $first, 4);
        sceAgcDcbDrawIndex(dcb, CUBE_INDICES, ibuf.ptr, 0);
        sceAgcCbSetShRegisterRangeDirect(dcb, GS_USER_DATA_BASE + (unsigned int)cb_dwo, $last, 4);
        sceAgcDcbDrawIndex(dcb, CUBE_INDICES, ibuf.ptr, 0);
"@
if($DisableDepth) {
    Replace-Checked '0xB6u' '0x0u'
    Replace-Checked '(0xB6ULL << 32)' '(0x0ULL << 32)'
}
$suffix='texture-overlap'
if($DisableDepth) { $suffix+='-disabled' }
if($ReverseObjects) { $suffix+='-reverse' }
if($FreezeView) { $suffix+='-static' }
[IO.File]::WriteAllText("$out/main-$suffix.c",$text)
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-$suffix.c" -o "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Overlapping cubes compilation failed' }
& "$out/link_real.exe" "$out/app-$suffix.elf" "$out/crt1.o" "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Overlapping cubes link failed' }
$report=Get-Content "$out/build-report-texture-depth.json" -Raw | ConvertFrom-Json
$report | Add-Member ObjectCount 2
$report | Add-Member ReversedObjectOrder ([bool]$ReverseObjects)
$report | Add-Member GeneratedSourceSha256 ((Get-FileHash "$out/main-$suffix.c").Hash)
$report.FrozenView=[bool]$FreezeView
$report.DepthEnabled=-not $DisableDepth
$report.ElfSha256=(Get-FileHash "$out/app-$suffix.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-$suffix.json" -Encoding utf8
