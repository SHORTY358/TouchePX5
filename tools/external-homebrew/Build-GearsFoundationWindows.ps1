# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$foundation=Join-Path $root 'artifacts/external-homebrew/gears-toolchain/native-foundation'
$pin='37dd53602bdead63936f718004555ba10154be48'
if((& git -C $foundation rev-parse HEAD) -ne $pin -or (& git -C $foundation status --porcelain)) { throw 'Clean pinned foundation required' }
$source=Join-Path $foundation 'tooling/native'
$out=Join-Path $root 'artifacts/external-homebrew/gears-toolchain/runtime-windows'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$cmake='C:/Dev/tools/cmake/cmake-4.4.3-windows-x86_64/bin/cmake.exe'
$compilerDirectory='C:/Dev/tools/llvm-mingw/llvm-mingw-20260826-ucrt-x86_64/bin'
$compiler=Join-Path $compilerDirectory 'clang++.exe'
$archive=Join-Path $out 'zlib-1.3.2.tar.gz'
$zlibHash='BB329A0A2CD0274D05519D61C667C062E06990D72E125EE2DFA8DE64F0119D16'
if(-not (Test-Path $archive)) { Invoke-WebRequest https://zlib.net/fossils/zlib-1.3.2.tar.gz -OutFile $archive }
if((Get-FileHash $archive).Hash -ne $zlibHash) { throw 'Zlib digest differs from pinned foundation contract' }
if(-not (Test-Path "$out/zlib-1.3.2")) {
    & tar -xf $archive -C $out
    if($LASTEXITCODE) { throw 'Zlib extraction failed' }
}
& $cmake -S "$out/zlib-1.3.2" -B "$out/zlib-build" -G Ninja -DCMAKE_MAKE_PROGRAM=C:/Dev/tools/ninja/ninja.exe "-DCMAKE_C_COMPILER=$compilerDirectory/clang.exe" -DCMAKE_BUILD_TYPE=Release -DZLIB_BUILD_EXAMPLES=OFF > "$out/zlib-configure.log" 2>&1
if($LASTEXITCODE) { throw 'Windows zlib configure failed' }
& $cmake --build "$out/zlib-build" --target zlibstatic --parallel 1 > "$out/zlib-build.log" 2>&1
if($LASTEXITCODE) { throw 'Windows zlib build failed' }
$flags=@('-std=c++20','-O2','-Wall','-Wextra','-Werror','-static')
& $compiler @flags "$source/libc_builder.cpp" -o "$out/libc-builder.exe" > "$out/build.log" 2>&1
if($LASTEXITCODE) { throw 'Runtime generator build failed' }
& $compiler @flags -I "$out/zlib-build" -I "$out/zlib-1.3.2" "$source/native_app_builder.cpp" "$source/self_container.cpp" "$source/elf_object.cpp" "$source/sce_module_writer.cpp" "$out/zlib-build/libzs.a" -o "$out/ps5-native-tool.exe" > "$out/tool-build.log" 2>&1
if($LASTEXITCODE) { throw 'Native packaging tool build failed' }
$expectedRaw='8EE6E124993E1AF26420CB455890FD002F5D6C7E78883C860CE45734E7D002BB'
$expectedSigned='E6FF45D16ADF687855CC3B33B0C8A4132B6504360B221E0A34C7E99FB3BA0036'
if(-not ([IO.File]::ReadAllText("$foundation/runtime/libc.prx.sha256").Contains($expectedSigned.ToLowerInvariant()+' *libc.prx'))) { throw 'Foundation runtime digest contract changed' }
foreach($copy in @('a','b')) {
    & "$out/libc-builder.exe" "$source/runtime/api-surface.txt" "$source/runtime/imports.txt" "$out/libc-$copy.raw.elf" > "$out/generate-$copy.log" 2>&1
    if($LASTEXITCODE -or (Get-FileHash "$out/libc-$copy.raw.elf").Hash -ne $expectedRaw) { throw 'Raw runtime does not match upstream digest' }
    & "$out/ps5-native-tool.exe" self --sign --in "$out/libc-$copy.raw.elf" --out "$out/libc-$copy.prx" > "$out/sign-$copy.log" 2>&1
    if($LASTEXITCODE -or (Get-FileHash "$out/libc-$copy.prx").Hash -ne $expectedSigned -or (Get-Item "$out/libc-$copy.prx").Length -ne 1284674) { throw 'Runtime container does not match upstream digest/size' }
}
@{
    Passed=$true; FoundationPin=$pin; FoundationModified=$false; GeneratedRuntimeLicense='GPL-3.0-or-later'
    Scope='two Windows-built raw ELF and runtime containers match the exact pinned upstream hashes; not native Gears or runtime execution'
    RawSha256=$expectedRaw; SignedSha256=$expectedSigned; SignedBytes=1284674
    CompilerPath=$compiler; CompilerOptions=$flags; CompilerSha256=(Get-FileHash $compiler).Hash
    GeneratorSha256=(Get-FileHash "$out/libc-builder.exe").Hash
    NativeToolSha256=(Get-FileHash "$out/ps5-native-tool.exe").Hash
    ZlibArchiveSha256=$zlibHash; ZlibLibrarySha256=(Get-FileHash "$out/zlib-build/libzs.a").Hash
    ApiManifestSha256=(Get-FileHash "$source/runtime/api-surface.txt").Hash
    ImportsManifestSha256=(Get-FileHash "$source/runtime/imports.txt").Hash
    SignedRuntimeBuilt=$true; RuntimeExecuted=$false; GearsBuilt=$false
} | ConvertTo-Json -Depth 4 | Set-Content "$out/report.json" -Encoding utf8
Write-Output 'PASS: two raw ELF and runtime containers match pinned upstream hashes on Windows'
