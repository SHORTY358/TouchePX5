# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$CorrectInitAbi)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=Join-Path $root 'artifacts/external-homebrew/ps5link-sdk'
$output=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$pin='ea771e535378740b6a058b8e5419eb8a0e0e0ec8'
if ((& git -C $source rev-parse HEAD) -ne $pin) { throw 'Unexpected ps5link-sdk revision' }
if (& git -C $source status --porcelain) { throw 'Upstream source checkout must be unchanged' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$clang='C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe'
foreach($stage in @('vs','ps')) {
    & py -3 "$source/shaders/tools/agcpack.py" array "$output/mesh_${stage}_sb.h" "mesh_${stage}_sb" "$source/shaders/third_party/sharpprospero/mesh_${stage}.sb"
    if($LASTEXITCODE) { throw 'Shader header generation failed' }
}
$sources=@('link_real','elf_object','linker','dynwriter','catalog','catalog_extra','catalog_lookup','nid','sha1') | ForEach-Object { "$source/linker/$_.c" }
& $clang -O2 -Wno-deprecated-declarations @sources -o "$output/link_real.exe"
if($LASTEXITCODE) { throw 'Host linker build failed' }
$cubeSource="$source/examples/gpu_cube/main.c"
$suffix=''
if($CorrectInitAbi) {
    $suffix='-init-abi'
    $text=[IO.File]::ReadAllText($cubeSource)
    $declaration='extern int sceAgcInit(void *state, unsigned int defaultsRevision);'
    if(-not $text.Contains($declaration) -or -not $text.Contains('sceAgcInit(&agc_state, 8)')) { throw 'Unexpected upstream init call shape' }
    $text=$text.Replace($declaration,'extern int sceAgcInit(unsigned int version);').Replace('sceAgcInit(&agc_state, 8)','sceAgcInit(8)')
    $cubeSource=Join-Path $output 'main-init-abi.c'
    [IO.File]::WriteAllText($cubeSource,$text)
}
& $clang --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $output -c $cubeSource -o "$output/main$suffix.o"
if($LASTEXITCODE) { throw 'Unchanged cube compilation failed' }
& $clang --target=x86_64-sie-ps5 -c "$source/linker/crt1.S" -o "$output/crt1.o"
if($LASTEXITCODE) { throw 'Unchanged native startup compilation failed' }
& "$output/link_real.exe" "$output/app$suffix.elf" "$output/crt1.o" "$output/main$suffix.o"
if($LASTEXITCODE) { throw 'Native SCE ELF link failed' }
@{
    Repository='https://github.com/Rufidj/ps5link-sdk'; Commit=$pin
    ElfSha256=(Get-FileHash "$output/app$suffix.elf").Hash
    SourceModified=[bool]$CorrectInitAbi; StartupModified=$false
    InitAbiCorrection=[bool]$CorrectInitAbi
    ShaderOrigin='Upstream third_party/sharpprospero; GPL-3.0; see retained LICENSE and PSSL'
    Signing='Unsigned SCE ELF for local emulator; not a console installation package'
    GameplayVerified=$false
} | ConvertTo-Json | Set-Content -LiteralPath "$output/build-report$suffix.json" -Encoding utf8
