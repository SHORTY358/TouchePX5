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
$out=Join-Path $repo ('artifacts/xash3d-native-inputs/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path "$out/shaders","$out/generated","$out/map" -Force | Out-Null
$assembly=[IO.File]::ReadAllText("$source/native/shader_assets.S")
$names=@([regex]::Matches($assembly,'build/shaders/([a-z_]+)\.(?:gs|ps)\.bin') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
if($names.Count -ne 8) { throw 'Review changed standalone native shader inventory' }
foreach($name in $names) {
    & $python "$source/tools/build_shader.py" --pipe "$source/shaders/$name.pipe" --name $name --amdllpc $compiler --readelf $readelf --objcopy $objcopy --output-dir "$out/shaders" > "$out/$name-build.log" 2>&1
    if($LASTEXITCODE -ne 0) { throw "Original native shader build failed: $name" }
    $prefix=if($name -eq 'gears_lit') { 'GEARS' } else { $name.ToUpperInvariant() }
    $symbol=if($name -eq 'gears_lit') { 'ps5_gears' } else { 'ps5_'+$name }
    $header=if($name -eq 'gears_lit') { 'gears_shader_metadata.h' } else { $name+'_shader_metadata.h' }
    & $python "$source/tools/generate_agc_metadata.py" --manifest "$out/shaders/$name.manifest.json" --output "$out/generated/$header" --prefix $prefix --symbol-prefix $symbol > "$out/$name-metadata.log" 2>&1
    if($LASTEXITCODE -ne 0) { throw "Original PAL metadata generation failed: $name" }
    foreach($stage in @('gs','ps')) { $assembly=$assembly.Replace("build/shaders/$name.$stage.bin",(Join-Path $out "shaders/$name.$stage.bin").Replace('\','/')) }
}
[IO.File]::WriteAllText("$out/shader_assets.S",$assembly,[Text.UTF8Encoding]::new($false))
& $python "$PSScriptRoot/Make-Xash3DFixture.py" $source "$out/map"
if($LASTEXITCODE -ne 0) { throw 'Synthetic upstream BSP preparation failed' }
& $python "$source/tools/generate_bsp_build_metadata.py" --bundle "$out/map/map.ps5bsp" --output "$out/generated/bsp_build_metadata.h" > "$out/map-metadata.log" 2>&1
if($LASTEXITCODE -ne 0) { throw 'Synthetic BSP metadata generation failed' }
@{SourcePin=$pin;GameAssetsUsed=$false;Shaders=$names;NativeBuilt=$false
    CompilerSha256=(Get-FileHash -LiteralPath $compiler).Hash
    Files=@(Get-ChildItem -LiteralPath $out -Recurse -File | ForEach-Object { @{File=[IO.Path]::GetRelativePath($out,$_.FullName);Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
} | ConvertTo-Json -Depth 6 | Set-Content "$out/report.json" -Encoding utf8
Write-Output "PASS original native shader/metadata and synthetic map inputs: $out"
