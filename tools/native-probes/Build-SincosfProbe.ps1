# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deps=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
& "$root/tools/external-homebrew/Test-GearsWindowsCrossCompile.ps1"
$bin="$deps/clang18/llvm-mingw-20240619-ucrt-x86_64/bin"
$sdk="$deps/sdk-v0.42/ps5-payload-sdk"
$out="$root/artifacts/native-sincosf"
New-Item -ItemType Directory -Path "$out/app/sce_module" -Force | Out-Null
$source="$PSScriptRoot/fixtures/sincosf.c"
$converter="$deps/foundation-demo-windows/ps5-native-tool.exe"
$converterReport=Get-Content "$deps/foundation-demo-windows/report.json" -Raw | ConvertFrom-Json
if((Get-FileHash $converter).Hash -ne $converterReport.ConverterSha256) { throw 'Converter provenance changed' }
& "$bin/clang.exe" -target x86_64-sie-ps5 -fvisibility-nodllstorageclass=default -isysroot $sdk -isystem "$sdk/target/include" -fno-stack-protector -fno-plt -femulated-tls -fno-builtin -O2 -Wall -Wextra -Werror -c $source -o "$out/probe.o"
if($LASTEXITCODE) { throw 'Native numerical probe compilation failed' }
$libraries=@(Get-ChildItem "$sdk/target/lib" -Filter '*.so' | Sort-Object Name | ForEach-Object FullName)
& "$bin/ld.lld.exe" -m elf_x86_64 -pie -z max-page-size=0x4000 -mllvm -emulated-tls --hash-style=gnu -T "$deps/native-foundation/tooling/native/ps5-pie.ld" --eh-frame-hdr --version-script "$deps/native-foundation/tooling/native/app-symbols.map" -e _start -o "$out/probe-pie.elf" "$deps/cross-compiler-clang18/foundation_app_crt.cpp.o" "$out/probe.o" --as-needed @libraries
if($LASTEXITCODE) { throw 'Native numerical probe link failed' }
& $converter link --in "$out/probe-pie.elf" --out "$out/probe.elf" --stub-dir "$sdk/target/lib" --module-sdk 0x02000009 --companion-sdk 0x08050001 --file-name eboot.elf > "$out/convert.log"
if($LASTEXITCODE) { throw 'Probe native conversion failed' }
& $converter self --sign --in "$out/probe.elf" --out "$out/app/eboot.bin" --magic 0x1D3D154F > "$out/self.log"
if($LASTEXITCODE) { throw 'Probe packaging failed' }
Copy-Item -LiteralPath "$deps/runtime-windows/libc-a.prx" -Destination "$out/app/sce_module/libc.prx" -Force
@{Scope='Standalone native sincosf numerical assertions with same companion libc as Gears; no graphics';
 SourceSha256=(Get-FileHash $source).Hash; SelfSha256=(Get-FileHash "$out/app/eboot.bin").Hash;
 CompanionSha256=(Get-FileHash "$out/app/sce_module/libc.prx").Hash; ConverterSha256=$converterReport.ConverterSha256} |
 ConvertTo-Json | Set-Content "$out/build-report.json" -Encoding utf8
Write-Output "Native sincosf probe built: $out"
