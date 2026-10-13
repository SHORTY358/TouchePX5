# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$ReverseObjects,[switch]$SharedTextureControl)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
& "$PSScriptRoot/Build-PS5LinkTexturePlane.ps1"
. "$PSScriptRoot/PS5LinkTexturePair.ps1"
$text=Add-PS5LinkTexturePair ([IO.File]::ReadAllText("$out/main-texture-static.c"))
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Generated source contract changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
Replace-Checked 'prim_m[2], cons_m[2];' 'prim_m[2], cons_m[2], cons_second[2];'
Replace-Checked 'ALLOC_OR_FAIL(cons_m[i], 256,     DIRECT_MEM_MIN_ALIGN, "cons");' @'
ALLOC_OR_FAIL(cons_m[i], 256,     DIRECT_MEM_MIN_ALIGN, "cons");
        ALLOC_OR_FAIL(cons_second[i], 256, DIRECT_MEM_MIN_ALIGN, "cons-second");
'@
Replace-Checked 'mat_identity(c);' 'mat_identity(c); c[0] = 0.5f; c[12] = -0.45f;'
Replace-Checked '/* Clear this frame' @'
float *second = cons_second[slot].ptr;
        mat_identity(second); second[0] = 0.5f; second[12] = 0.45f;
        mat_identity(second + 16);
        unsigned int second_cb[4];
        desc_constant(second_cb, (unsigned long long)(unsigned long)cons_second[slot].ptr, 128);

        /* Clear this frame
'@
$a='cb_words'; $b='second_cb'; $ta='texture_words'; $tb='second_texture_words'
if($SharedTextureControl) { $tb='texture_words' }
if($ReverseObjects) { $a='second_cb'; $b='cb_words'; $ta='second_texture_words'; $tb='texture_words' }
if($ReverseObjects -and $SharedTextureControl) { $ta='texture_words' }
Replace-Checked 'sceAgcDcbDrawIndex(dcb, CUBE_INDICES, ibuf.ptr, 0);' @"
sceAgcCbSetShRegisterRangeDirect(dcb, GS_USER_DATA_BASE + (unsigned int)cb_dwo, $a, 4);
        sceAgcCbSetShRegisterRangeDirect(dcb, PS_USER_DATA_BASE + (unsigned int)tex_dwo, $ta, 8);
        sceAgcDcbDrawIndex(dcb, CUBE_INDICES, ibuf.ptr, 0);
        sceAgcCbSetShRegisterRangeDirect(dcb, GS_USER_DATA_BASE + (unsigned int)cb_dwo, $b, 4);
        sceAgcCbSetShRegisterRangeDirect(dcb, PS_USER_DATA_BASE + (unsigned int)tex_dwo, $tb, 8);
        sceAgcDcbDrawIndex(dcb, CUBE_INDICES, ibuf.ptr, 0);
"@
$suffix='texture-bindings'
if($SharedTextureControl) { $suffix+='-shared' }
if($ReverseObjects) { $suffix+='-reverse' }
$suffix+='-static'
[IO.File]::WriteAllText("$out/main-$suffix.c",$text)
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-$suffix.c" -o "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Texture bindings compilation failed' }
& "$out/link_real.exe" "$out/app-$suffix.elf" "$out/crt1.o" "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Texture bindings link failed' }
$report=Get-Content "$out/build-report-texture-static.json" -Raw | ConvertFrom-Json
$report | Add-Member ObjectCount 2
$report | Add-Member DistinctObjectTextures (-not $SharedTextureControl)
$report | Add-Member ReversedObjectOrder ([bool]$ReverseObjects)
$report | Add-Member GeneratedSourceSha256 ((Get-FileHash "$out/main-$suffix.c").Hash)
$report.ElfSha256=(Get-FileHash "$out/app-$suffix.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-$suffix.json" -Encoding utf8
