# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
# The native outputs/assembly overlay retain the upstream GPL-3.0 license.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deps=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
$gears=Join-Path $root 'artifacts/external-homebrew/ps5-agc-gears'
$foundation=Join-Path $deps 'native-foundation'
$out=Join-Path $deps 'gears-native-windows'
$shaderReport=Get-Content "$out/shader-report.json" -Raw | ConvertFrom-Json
if(-not $shaderReport.ShadersBuilt -or $shaderReport.MetadataHeaderSha256 -ne (Get-FileHash "$out/generated/gears_shader_metadata.h").Hash -or $shaderReport.ManifestSha256 -ne (Get-FileHash "$out/shaders/gears_lit.manifest.json").Hash) { throw 'Verified real Gears shader outputs required' }
$manifest=Get-Content "$out/shaders/gears_lit.manifest.json" -Raw | ConvertFrom-Json
foreach($stage in @(@{Name='pre_raster_gs';File='gears_lit.gs.bin'},@{Name='pixel';File='gears_lit.ps.bin'})) {
    if((Get-FileHash "$out/shaders/$($stage.File)").Hash -ne $manifest.stages.($stage.Name).sha256) { throw 'Shader payload changed after validation' }
}
& "$PSScriptRoot/Test-GearsWindowsCrossCompile.ps1"
$cross=Join-Path $deps 'cross-compiler-clang18'
$bin=Join-Path $deps 'clang18/llvm-mingw-20240619-ucrt-x86_64/bin'
$sdk=Join-Path $deps 'sdk-v0.42/ps5-payload-sdk'
$converter=Join-Path $deps 'foundation-demo-windows/ps5-native-tool.exe'
$converterReport=Get-Content "$deps/foundation-demo-windows/report.json" -Raw | ConvertFrom-Json
if((Get-FileHash $converter).Hash -ne $converterReport.ConverterSha256 -or -not $converterReport.OriginalConverterFailureVerified) { throw 'Verified repaired foundation converter required' }
$flags=@('-target','x86_64-sie-ps5','-fvisibility-nodllstorageclass=default','-isysroot',$sdk,'-isystem',"$sdk/target/include/c++/v1",'-isystem',"$sdk/target/include",'-fno-stack-protector','-fno-plt','-femulated-tls','-O2','-Wall','-Wextra','-Werror','-ffunction-sections','-fdata-sections',"-I$gears/include","-I$gears/src","-I$gears/native","-I$gears/native/ps5log","-I$out/generated")
& "$bin/clang.exe" @flags -std=c11 -c "$gears/native/main.c" -o "$out/main.o" > "$out/main-build.log" 2>&1
if($LASTEXITCODE) { throw 'Gears native main compilation failed' }
$assembly=[IO.File]::ReadAllText("$gears/native/shader_assets.S")
foreach($file in @('gears_lit.gs.bin','gears_lit.ps.bin')) {
    $original="build/shaders/$file"
    if(-not $assembly.Contains($original)) { throw 'Review changed upstream shader assembly' }
    $assembly=$assembly.Replace($original,(Join-Path $out "shaders/$file").Replace('\','/'))
}
[IO.File]::WriteAllText("$out/shader_assets.S",$assembly)
& "$bin/clang.exe" @flags -c "$out/shader_assets.S" -o "$out/shader_assets.o" > "$out/assets-build.log" 2>&1
if($LASTEXITCODE) { throw 'Actual Gears shader assembly failed' }
$buildScript=[IO.File]::ReadAllText("$gears/tools/build_native.sh")
$sources=[regex]::Match($buildScript,'(?s)sources=\((.*?)\)').Groups[1].Value.Trim() -split '\s+'
if($sources.Count -ne 25) { throw 'Review changed native source list' }
$objects=@("$cross/foundation_app_crt.cpp.o")
foreach($source in $sources) {
    if($source -eq 'native/main.c') { $objects+="$out/main.o" }
    else { $objects+=Join-Path $cross ($source.Replace('/','_')+'.o') }
}
$objects+=@("$cross/native_ps5log_ps5log.c.o","$cross/native_ps5log_ps5log_ps5_net.c.o","$out/shader_assets.o")
$libraries=@(Get-ChildItem "$sdk/target/lib" -Filter '*.so' | Sort-Object Name | ForEach-Object FullName)
& "$bin/ld.lld.exe" -m elf_x86_64 -pie -z max-page-size=0x4000 -mllvm -emulated-tls --hash-style=gnu -T "$foundation/tooling/native/ps5-pie.ld" --eh-frame-hdr --version-script "$foundation/tooling/native/app-symbols.map" -e _start -o "$out/llvm-pie.elf" @objects --as-needed @libraries "$cross/libSceAgc.so" "$cross/libSceAgcDriver.so" > "$out/native-link.log" 2>&1
if($LASTEXITCODE) { throw 'Gears native link failed' }
& $converter link --in "$out/llvm-pie.elf" --out "$out/eboot.elf" --stub-dir "$sdk/target/lib" --module-sdk 0x02000009 --stub "$cross/libSceAgc.so" --stub "$cross/libSceAgcDriver.so" --companion-sdk 0x08050001 --file-name eboot.elf > "$out/native-convert.log" 2>&1
if($LASTEXITCODE) { throw 'Gears native conversion failed' }
$app=Join-Path $out 'app/PPSA99997'
New-Item -ItemType Directory -Path "$app/sce_sys","$app/sce_module" -Force | Out-Null
& $converter self --sign --in "$out/eboot.elf" --out "$app/eboot.bin" --magic 0x1D3D154F > "$out/native-self.log" 2>&1
if($LASTEXITCODE) { throw 'Gears container packaging failed' }
& $converter self --inspect --file "$app/eboot.bin" > "$out/native-inspect.log" 2>&1
if($LASTEXITCODE -or [IO.File]::ReadAllText("$out/native-inspect.log") -notmatch 'integrity: valid') { throw 'Gears container integrity failed' }
Copy-Item -LiteralPath "$gears/sce_sys/param.json","$gears/sce_sys/icon0.png" -Destination "$app/sce_sys" -Force
Copy-Item -LiteralPath "$deps/runtime-windows/libc-a.prx" -Destination "$app/sce_module/libc.prx" -Force
if(Test-Path "$gears/dev.conf") { Copy-Item -LiteralPath "$gears/dev.conf" -Destination "$app/dev.conf" -Force }
@{NativeTitleBuilt=$true; Executed=$false; App=$app; ElfSha256=(Get-FileHash "$out/eboot.elf").Hash; SelfSha256=(Get-FileHash "$app/eboot.bin").Hash;
    ShaderManifestSha256=$shaderReport.ManifestSha256; MetadataHeaderSha256=$shaderReport.MetadataHeaderSha256;
    OriginalAssemblySha256=(Get-FileHash "$gears/native/shader_assets.S").Hash; AssemblyOverlaySha256=(Get-FileHash "$out/shader_assets.S").Hash;
    ConverterSha256=$converterReport.ConverterSha256; PosixHostNetworkTestCompleted=$false;
    Scope='native pinned Gears title built using actual LLPC stages; no emulator execution or full upstream host test claim'
} | ConvertTo-Json -Depth 4 | Set-Content "$out/native-report.json" -Encoding utf8
Write-Output 'PASS: native Gears app built and packaged; not executed'
