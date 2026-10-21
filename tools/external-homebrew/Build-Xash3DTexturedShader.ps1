# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=Join-Path $repo 'artifacts/external-homebrew/ps5-xash3d-halflife'
$pin='be0731b4fcc4233349bba5e0d3ff33c79428f695'
if((& git -C $source rev-parse HEAD) -ne $pin -or (& git -C $source status --porcelain)) { throw 'Clean pinned Xash3D source required' }
$deps=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain'
$compiler=Join-Path $deps 'build-windows/llpc/llpc/amdllpc.exe'
$readelf=Join-Path $deps 'build-windows/llpc/llvm/bin/llvm-readelf.exe'
$objcopy=Join-Path $deps 'build-windows/llpc/llvm/bin/llvm-objcopy.exe'
$python=Join-Path $deps 'python/Scripts/python.exe'
foreach($file in @($compiler,$readelf,$objcopy,$python)) { if(-not(Test-Path -LiteralPath $file)) { throw "Missing existing tool: $file" } }
$out=Join-Path $repo ('artifacts/xash3d-shaders/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
& $python "$source/tools/build_shader.py" --pipe "$source/shaders/bsp_textured.pipe" --name bsp_textured --amdllpc $compiler --readelf $readelf --objcopy $objcopy --output-dir $out > "$out/build.log" 2>&1
if($LASTEXITCODE -ne 0) { throw "Original Xash3D shader compilation failed: $out" }
$manifest=Get-Content "$out/bsp_textured.manifest.json" -Raw | ConvertFrom-Json
if($manifest.target -ne 'gfx1013' -or $manifest.pipeline_type -ne 'Ngg' -or -not $manifest.no_relocations) { throw 'Unexpected native shader pipeline' }
foreach($stage in @(@{Name='pre_raster_gs';Suffix='gs'},@{Name='pixel';Suffix='ps'})) {
 $file=Join-Path $out ('bsp_textured.'+$stage.Suffix+'.bin')
 $entry=$manifest.stages.($stage.Name)
 if((Get-Item $file).Length -ne $entry.bytes -or (Get-FileHash $file).Hash -ne $entry.sha256) { throw 'Shader extent/hash mismatch' }
}
[ordered]@{SourcePin=$pin;PipeSha256=(Get-FileHash "$source/shaders/bsp_textured.pipe").Hash
 CompilerSha256=(Get-FileHash $compiler).Hash;ReadelfSha256=(Get-FileHash $readelf).Hash;ObjcopySha256=(Get-FileHash $objcopy).Hash
 ManifestSha256=(Get-FileHash "$out/bsp_textured.manifest.json").Hash;ShaderStagesBuilt=$true
 HostContractsCheckedByThisInvocation=$false;GuestBuilt=$false;GuestExecuted=$false;RenderingVerified=$false;GameAssetsUsed=$false
 Scope='Unchanged project-authored textured BSP GLSL compiled to original GFX1013 PAL stages; not title execution or emulator compatibility'
} | ConvertTo-Json -Depth 5 | Set-Content "$out/report.json" -Encoding utf8
Write-Output "PASS original Xash3D textured GFX1013 stages: $out"
