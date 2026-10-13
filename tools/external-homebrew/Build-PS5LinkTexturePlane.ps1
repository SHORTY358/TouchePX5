# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
& "$PSScriptRoot/Build-PS5LinkCube.ps1" -CorrectInitAbi
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$shaderOut=Join-Path $root 'artifacts/external-homebrew/ps5link-texture-shaders'
$shaderReport=Get-Content "$shaderOut/build-report.json" -Raw | ConvertFrom-Json
if((Get-FileHash "$shaderOut/textured_p.sb").Hash -ne $shaderReport.Programs[0].Sha256) {
    throw 'Texture shader does not match its build provenance'
}
$text=[IO.File]::ReadAllText("$out/main-init-abi.c").Replace('mesh_ps_sb','textured_p_sb')
$text="/* LOCAL SOURCE ADAPTATION: textured plane; upstream cube/startup retained separately. */`n"+$text
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Upstream source shape changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
Replace-Checked '#define CUBE_VERTS 24' '#define CUBE_VERTS 4'
Replace-Checked '#define CUBE_INDICES 36' '#define CUBE_INDICES 6'
Replace-Checked 'build_cube((unsigned char*)vbuf.ptr, (unsigned int*)ibuf.ptr);' @'
/* Local texture plane. mesh_vs exports transformed normal as TEXCOORD0;
     * feed UV in normal.xy and keep its model matrix identity. */
    put_vertex(vbuf.ptr, 0, -0.8f,-0.8f,0.5f, 0,0,1, 0xFFFFFFFFu);
    put_vertex(vbuf.ptr, 1, -0.8f, 0.8f,0.5f, 0,1,1, 0xFFFFFFFFu);
    put_vertex(vbuf.ptr, 2,  0.8f, 0.8f,0.5f, 1,1,1, 0xFFFFFFFFu);
    put_vertex(vbuf.ptr, 3,  0.8f,-0.8f,0.5f, 1,0,1, 0xFFFFFFFFu);
    unsigned int plane_indices[6] = {0,1,2,0,2,3};
    for (int i = 0; i < 6; i++) ((unsigned int*)ibuf.ptr)[i] = plane_indices[i];
    DirectMem texture;
    ALLOC_OR_FAIL(texture, 64 * 64 * 4, DIRECT_MEM_MIN_ALIGN, "texture");
    unsigned char *pixels = texture.ptr;
    for (unsigned int y = 0; y < 64; y++) for (unsigned int x = 0; x < 64; x++) {
        unsigned int i = (y * 64 + x) * 4;
        unsigned char value = ((x / 8 + y / 8) & 1) ? 48 : 255;
        pixels[i] = (y < 32 ? (x < 32) : (x >= 32)) ? value : 0;
        pixels[i+1] = x >= 32 ? value : 0;
        pixels[i+2] = y >= 32 && x < 32 ? value : 0;
        pixels[i+3] = 255;
    }
    /* RDNA2 image descriptor: 64x64, linear, RGBA8 UNORM (FORMAT=56),
     * identity RGBA destination selects, 2D, one mip, 256-byte rows. */
    unsigned long long ta = (unsigned long long)(unsigned long)texture.ptr;
    unsigned int texture_words[8] = {
        (unsigned int)(ta >> 8),
        (unsigned int)(ta >> 40) | (56u << 20) | (3u << 30),
        (15u) | (63u << 14), 0x90000FACu, 63u, 0, 0, 0
    };
    unsigned int sampler_words[4] = {0,0,0,0}; /* normalized, repeat, nearest */
    int tex_dwo = resource_dword_offset(ps_handle, KIND_READONLY, 0);
    int samp_dwo = resource_dword_offset(ps_handle, KIND_SAMPLER, 0);
    if (tex_dwo != 0 || samp_dwo != 8) {
        notify("mesh_test: unexpected texture resource layout"); return 1;
    }
'@
Replace-Checked 'for (int i = 0; i < 16; i++) c[i] = mvp[i];' 'mat_identity(c);'
Replace-Checked 'for (int i = 0; i < 16; i++) c[16 + i] = model[i];' 'mat_identity(c + 16);'
Replace-Checked '/* Both resources belong to the vertex stage. */' @'
sceAgcCbSetShRegisterRangeDirect(dcb, PS_USER_DATA_BASE + (unsigned int)tex_dwo, texture_words, 8);
        sceAgcCbSetShRegisterRangeDirect(dcb, PS_USER_DATA_BASE + (unsigned int)samp_dwo, sampler_words, 4);
        /* Both resources below belong to the vertex stage. */
'@
[IO.File]::WriteAllText("$out/main-texture-static.c",$text)
$clang='C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe'
& $clang --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -I $shaderOut -c "$out/main-texture-static.c" -o "$out/main-texture-static.o"
if($LASTEXITCODE) { throw 'Texture plane compilation failed' }
& "$out/link_real.exe" "$out/app-texture-static.elf" "$out/crt1.o" "$out/main-texture-static.o"
if($LASTEXITCODE) { throw 'Texture plane link failed' }
@{
    BaseRepository='https://github.com/Rufidj/ps5link-sdk'
    BaseCommit='ea771e535378740b6a058b8e5419eb8a0e0ec8'
    SourceModified=$true; StartupModified=$false; InitAbiCorrection=$true
    TextureShaderSha256=$shaderReport.Programs[0].Sha256
    FrozenView=$true; TextureDimensions='64x64 RGBA8 UNORM'; TextureLayout='linear'
    UvTransport='mesh vertex normal.xy with identity model matrix (local adaptation)'
    ElfSha256=(Get-FileHash "$out/app-texture-static.elf").Hash
    VisualCorrectnessVerified=$false
} | ConvertTo-Json | Set-Content "$out/build-report-texture-static.json" -Encoding utf8
