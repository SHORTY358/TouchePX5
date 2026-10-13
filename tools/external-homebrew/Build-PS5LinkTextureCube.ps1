# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
& "$PSScriptRoot/Build-PS5LinkTexturePlane.ps1"
& "$PSScriptRoot/Build-PS5LinkCubeDepth.ps1"
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$shaderOut=Join-Path $root 'artifacts/external-homebrew/ps5link-texture-shaders'
$plane=[IO.File]::ReadAllText("$out/main-texture-static.c")
$begin=$plane.IndexOf('DirectMem texture;')
$end=$plane.IndexOf('/* Where the vertex program wants its two resources. */',$begin)
if($begin -lt 0 -or $end -le $begin) { throw 'Generated texture source contract changed' }
$textureSource=$plane.Substring($begin,$end-$begin)
$text=[IO.File]::ReadAllText("$out/main-depth.c").Replace('mesh_ps_sb','textured_p_sb')
$text="/* LOCAL SOURCE ADAPTATION: animated textured cube with D32 GPU reset. */`n"+$text
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Generated source contract changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
# The unchanged mesh vertex shader transports normal.xy as TEXCOORD0.
# Each face's corners carry UV there; only MVP rotates, while the model
# constants that transform TEXCOORD0 stay identity. Pixel lighting is replaced
# by the explicitly assembled texture program, so real normals are not needed.
Replace-Checked 'N[f][0], N[f][1], N[f][2], COL[f]);' '(c >= 2 ? 1.0f : 0.0f), (c == 1 || c == 2 ? 1.0f : 0.0f), 1.0f, 0xFFFFFFFFu);'
Replace-Checked 'for (int i = 0; i < 16; i++) c[16 + i] = model[i];' 'mat_identity(c + 16); /* preserve UV during rotation */'
Replace-Checked '/* Where the vertex program wants its two resources. */' ($textureSource+"`n/* Where the vertex program wants its two resources. */")
Replace-Checked '/* Fullscreen depth reset, followed by the actual cube draw. */' @'
sceAgcCbSetShRegisterRangeDirect(dcb, PS_USER_DATA_BASE + (unsigned int)tex_dwo, texture_words, 8);
        sceAgcCbSetShRegisterRangeDirect(dcb, PS_USER_DATA_BASE + (unsigned int)samp_dwo, sampler_words, 4);
        /* Fullscreen depth reset, followed by the actual cube draw. */
'@
[IO.File]::WriteAllText("$out/main-texture-depth.c",$text)
$clang='C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe'
& $clang --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -I $shaderOut -c "$out/main-texture-depth.c" -o "$out/main-texture-depth.o"
if($LASTEXITCODE) { throw 'Texture cube compilation failed' }
& "$out/link_real.exe" "$out/app-texture-depth.elf" "$out/crt1.o" "$out/main-texture-depth.o"
if($LASTEXITCODE) { throw 'Texture cube link failed' }
$shaderReport=Get-Content "$shaderOut/build-report.json" -Raw | ConvertFrom-Json
@{
    BaseRepository='https://github.com/Rufidj/ps5link-sdk'
    BaseCommit='ea771e535378740b6a058b8e5419eb8a0e0ec8'
    SourceModified=$true; StartupModified=$false; InitAbiCorrection=$true
    DepthEnabled=$true; DepthClearMethod='fullscreen-gpu-draw'; BackFaceCulling=$false
    FrozenView=$false; TextureDimensions='64x64 RGBA8 UNORM'; TextureLayout='linear'
    TextureShaderSha256=$shaderReport.Programs[0].Sha256
    ElfSha256=(Get-FileHash "$out/app-texture-depth.elf").Hash
    VisualCorrectnessVerified=$false
} | ConvertTo-Json | Set-Content "$out/build-report-texture-depth.json" -Encoding utf8
