# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([string]$LlvmBin='C:/Dev/tools/llvm-amdgpu-18.1.8/build/bin')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=Join-Path $root 'artifacts/external-homebrew/ps5link-sdk'
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-texture-shaders'
if((& git -C $source rev-parse HEAD) -ne 'ea771e535378740b6a058b8e5419eb8a0e0e0ec8' -or
    (& git -C $source status --porcelain)) { throw 'Expected unchanged pinned ps5link-sdk checkout' }
$mc=Join-Path $LlvmBin 'llvm-mc.exe'
$objcopy=Join-Path $LlvmBin 'llvm-objcopy.exe'
foreach($tool in @($mc,$objcopy)) {
    if(-not (Test-Path -LiteralPath $tool)) { throw "Missing AMDGPU tool: $tool" }
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$packer="$source/shaders/tools/agcpack.py"
& py -3 $packer texture-container "$source/shaders/third_party/sharpprospero/mesh_ps.sb" "$out/texture_container.sb"
if($LASTEXITCODE) { throw 'Texture container generation failed' }
# Start with the reviewed one-texture contract. The two-texture programs need
# a different resource container and must not silently use the one-slot one.
$programs=@('textured_p')
$records=@()
foreach($name in $programs) {
    & $mc -triple=amdgcn-amd-amdhsa -mcpu=gfx1030 -filetype=obj -o "$out/$name.o" "$source/shaders/src/$name.s"
    if($LASTEXITCODE) { throw "AMDGPU assembly failed: $name" }
    & $objcopy --dump-section ".text=$out/$name.bin" "$out/$name.o" "$out/$name.stripped.o"
    if($LASTEXITCODE) { throw "Shader code extraction failed: $name" }
    & py -3 $packer pack "$out/texture_container.sb" "$out/$name.bin" "$out/$name.sb" --vgprs 4
    if($LASTEXITCODE) { throw "Shader packing failed: $name" }
    & py -3 $packer array "$out/${name}_sb.h" "${name}_sb" "$out/$name.sb"
    if($LASTEXITCODE) { throw "Shader header generation failed: $name" }
    $records+=@{Name=$name; CodeBytes=(Get-Item "$out/$name.bin").Length; Sha256=(Get-FileHash "$out/$name.sb").Hash}
}
@{
    Repository='https://github.com/Rufidj/ps5link-sdk'
    Commit='ea771e535378740b6a058b8e5419eb8a0e0ec8'
    AssemblyTarget='gfx1030 (upstream build.sh target)'
    Compiler=$mc; CompilerSha256=(Get-FileHash $mc).Hash
    ShaderContainerOrigin='SharpProspero mesh_ps.sb; retained GPL-3.0 source and license'
    Programs=$records; RuntimeRenderingVerified=$false
} | ConvertTo-Json -Depth 4 | Set-Content "$out/build-report.json" -Encoding utf8
Write-Output "Texture shaders built: $out"
