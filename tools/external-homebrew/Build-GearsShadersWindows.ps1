# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deps=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
$gears=Join-Path $root 'artifacts/external-homebrew/ps5-agc-gears'
if(Test-Path "$deps/active-build.json") { throw 'Wait for the owned compiler build to finish' }
$build=Get-Content "$deps/build-report.json" -Raw | ConvertFrom-Json
if(-not $build.BuildComplete) { throw 'A completed pinned compiler build is required' }
if((& git -C $gears rev-parse HEAD) -ne '1ae1f9182abd2770c131b97419034fb85173c2dc' -or (& git -C $gears status --porcelain)) { throw 'Clean pinned Gears source required' }
& "$PSScriptRoot/Get-GearsToolchainSources.ps1"
$compiler=Join-Path $deps 'build-windows/llpc/llpc/amdllpc.exe'
$readelf=Join-Path $deps 'build-windows/llpc/llvm/bin/llvm-readelf.exe'
$objcopy=Join-Path $deps 'build-windows/llpc/llvm/bin/llvm-objcopy.exe'
$python=Join-Path $deps 'python/Scripts/python.exe'
foreach($file in @($compiler,$readelf,$objcopy,$python)) { if(-not(Test-Path $file)) { throw "Required tool is missing: $file" } }
$out=Join-Path $deps 'gears-native-windows'
New-Item -ItemType Directory -Path "$out/shaders","$out/generated" -Force | Out-Null
& $python "$gears/tools/build_shader.py" --amdllpc $compiler --readelf $readelf --objcopy $objcopy --output-dir "$out/shaders" > "$out/shader-build.log" 2>&1
if($LASTEXITCODE) { throw "Real Gears shader build failed; see $out/shader-build.log" }
$manifest=Get-Content "$out/shaders/gears_lit.manifest.json" -Raw | ConvertFrom-Json
if($manifest.target -ne 'gfx1013' -or $manifest.pipeline_type -ne 'Ngg' -or -not $manifest.no_relocations) { throw 'Unexpected Gears PAL pipeline' }
foreach($stage in @(@{Name='pre_raster_gs';File='gears_lit.gs.bin'},@{Name='pixel';File='gears_lit.ps.bin'})) {
    $entry=$manifest.stages.($stage.Name)
    $file=Join-Path $out ('shaders/'+$stage.File)
    if((Get-Item $file).Length -ne $entry.bytes -or (Get-FileHash $file).Hash -ne $entry.sha256) { throw "Shader extent/digest mismatch: $($stage.Name)" }
}
& $python "$gears/tools/generate_agc_metadata.py" --manifest "$out/shaders/gears_lit.manifest.json" --output "$out/generated/gears_shader_metadata.h" > "$out/metadata-build.log" 2>&1
if($LASTEXITCODE) { throw 'Gears metadata derivation failed' }
@{ShadersBuilt=$true; NativeTitleBuilt=$false; Executed=$false; GearsPin='1ae1f9182abd2770c131b97419034fb85173c2dc';
    CompilerSha256=(Get-FileHash $compiler).Hash; ReadelfSha256=(Get-FileHash $readelf).Hash; ObjcopySha256=(Get-FileHash $objcopy).Hash;
    PipeSha256=(Get-FileHash "$gears/shaders/gears_lit.pipe").Hash; ManifestSha256=(Get-FileHash "$out/shaders/gears_lit.manifest.json").Hash;
    MetadataHeaderSha256=(Get-FileHash "$out/generated/gears_shader_metadata.h").Hash;
    Scope='actual pinned Gears GLSL to GFX1013 PAL stages and derived AGC metadata; no native title or emulator execution'
} | ConvertTo-Json -Depth 4 | Set-Content "$out/shader-report.json" -Encoding utf8
Write-Output 'PASS: real Gears GFX1013 PAL stages and AGC metadata built'
