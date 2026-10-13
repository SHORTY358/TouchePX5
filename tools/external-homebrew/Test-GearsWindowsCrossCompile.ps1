# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
# Compile real upstream translation units only; no substitute shader metadata.
[CmdletBinding()]
param([switch]$AlternativeClang23, [switch]$VerifySdkOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$gears=Join-Path $root 'artifacts/external-homebrew/ps5-agc-gears'
$deps=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
$foundation=Join-Path $deps 'native-foundation'
$pins=@{ $gears='1ae1f9182abd2770c131b97419034fb85173c2dc'; $foundation='37dd53602bdead63936f718004555ba10154be48' }
foreach($checkout in $pins.Keys) {
    $actual=& git -C $checkout rev-parse HEAD
    if($LASTEXITCODE -or $actual -ne $pins[$checkout]) { throw "Unexpected pin: $checkout" }
    $dirty=& git -C $checkout status --porcelain
    if($LASTEXITCODE -or $dirty) { throw "Clean pinned checkout required: $checkout" }
}
$sdkArchive=Join-Path $deps 'ps5-payload-sdk-v0.42.zip'
$sdk=Join-Path $deps 'sdk-v0.42/ps5-payload-sdk'
$sdkHash='8CFBC7CD5811E719EB4F0C47EEA668D3DC7B40BC8AB11C4A5031D40C23EC02DA'
if((Get-FileHash $sdkArchive).Hash -ne $sdkHash -or (Get-Item $sdkArchive).Length -ne 8816075) { throw 'SDK archive differs from foundation v0.42 contract' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($sdkArchive)
$verifiedSdkFiles=0
try {
    foreach($entry in $zip.Entries) {
        if($entry.FullName.EndsWith('/')) { continue }
        $path=[IO.Path]::GetFullPath((Join-Path (Split-Path $sdk) $entry.FullName))
        if(-not $path.StartsWith($sdk+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'SDK archive path outside isolated extraction' }
        $stream=$entry.Open()
        $hasher=[Security.Cryptography.SHA256]::Create()
        try { $hash=[BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','') }
        finally { $stream.Dispose(); $hasher.Dispose() }
        if((Get-FileHash $path).Hash -ne $hash) { throw "Extracted SDK file differs from verified archive: $($entry.FullName)" }
        $verifiedSdkFiles++
    }
} finally { $zip.Dispose() }
if($VerifySdkOnly) {
    Write-Output "PASS: $verifiedSdkFiles extracted SDK files match the pinned archive"
    return
}
$bin=Join-Path $deps 'clang18/llvm-mingw-20240619-ucrt-x86_64/bin'
if($AlternativeClang23) { $bin='C:/Dev/tools/llvm-mingw/llvm-mingw-20260826-ucrt-x86_64/bin' }
else { & "$PSScriptRoot/Get-GearsClang18.ps1" }
$cc=Join-Path $bin 'clang.exe'
$out=Join-Path $deps 'cross-compiler-clang18'
if($AlternativeClang23) { $out=Join-Path $deps 'cross-compiler-probe' }
New-Item -ItemType Directory -Path $out -Force | Out-Null
$version=(& $cc --version | Out-String).Trim()
$expectedVersion='clang version 18\.1\.8'
if($AlternativeClang23) { $expectedVersion='clang version 23\.1\.0' }
if($LASTEXITCODE -or $version -notmatch $expectedVersion) { throw 'Unexpected cross compiler version' }
$flags=@('-target','x86_64-sie-ps5','-fvisibility-nodllstorageclass=default','-isysroot',$sdk,
    '-isystem',"$sdk/target/include/c++/v1",'-isystem',"$sdk/target/include",
    '-fno-stack-protector','-fno-plt','-femulated-tls','-O2','-Wall','-Wextra','-Werror',
    '-ffunction-sections','-fdata-sections',"-I$gears/include","-I$gears/src","-I$gears/native","-I$gears/native/ps5log")
$buildScript=[IO.File]::ReadAllText("$gears/tools/build_native.sh")
$sourceBlock=[regex]::Match($buildScript,'(?s)sources=\((.*?)\)').Groups[1].Value
$sources=@($sourceBlock.Trim() -split '\s+' | Where-Object { $_ -ne 'native/main.c' })
if($sources.Count -ne 24) { throw 'Review changed upstream native source list before continuing' }
$entries=@()
foreach($source in $sources + @('native/ps5log/ps5log.c','native/ps5log/ps5log_ps5_net.c','native/stubs/libSceAgc.c','native/stubs/libSceAgcDriver.c','foundation/app_crt.cpp')) {
    $name=$source.Replace('/','_')
    $inputPath=Join-Path $gears $source
    $extra=@('-std=c11')
    if($source -eq 'native/ps5log/ps5log.c') { $extra+=@('-include',"$gears/native/ps5log/ps5log_ps5_net.h") }
    if($source -like 'native/stubs/*') { $extra+='-fPIC' }
    if($source -eq 'foundation/app_crt.cpp') {
        $inputPath=Join-Path $foundation 'tooling/native/app_crt.cpp'
        $extra=@('-std=c++20','-fno-exceptions','-fno-rtti')
    }
    & $cc @flags @extra -c $inputPath -o "$out/$name.o" > "$out/$name.build.log" 2>&1
    if($LASTEXITCODE) { throw "Cross compile failed: $source; see $out/$name.build.log" }
    & "$bin/llvm-readelf.exe" -h "$out/$name.o" > "$out/$name.header.log" 2>&1
    if($LASTEXITCODE) { throw "ELF inspection failed: $source" }
    $header=[IO.File]::ReadAllText("$out/$name.header.log")
    if($header -notmatch 'ELF64' -or $header -notmatch 'REL \(Relocatable file\)' -or $header -notmatch 'Advanced Micro Devices X86-64') { throw "Unexpected target object: $source" }
    $entries+=@{Source=$source; SourceSha256=(Get-FileHash $inputPath).Hash; ObjectSha256=(Get-FileHash "$out/$name.o").Hash}
}
$stubs=@()
foreach($library in @('libSceAgc','libSceAgcDriver')) {
    & "$bin/ld.lld.exe" -m elf_x86_64 --shared -T "$sdk/ldscripts/elf_x86_64.x" --eh-frame-hdr -z max-page-size=0x4000 -mllvm -emulated-tls --hash-style=gnu -soname "$library.prx" -o "$out/$library.so" "$out/native_stubs_$library.c.o" > "$out/$library.link.log" 2>&1
    if($LASTEXITCODE) { throw "Import stub link failed: $library" }
    & "$bin/llvm-readelf.exe" -h -d "$out/$library.so" > "$out/$library.elf.log" 2>&1
    if($LASTEXITCODE) { throw "Import stub inspection failed: $library" }
    $elf=[IO.File]::ReadAllText("$out/$library.elf.log")
    if($elf -notmatch 'DYN \(Shared object file\)' -or $elf -notmatch ([regex]::Escape("[$library.prx]"))) { throw "Import stub type/SONAME differs: $library" }
    $stubs+=@{Name=$library; Sha256=(Get-FileHash "$out/$library.so").Hash}
}
@{Passed=$true; ObjectCount=$entries.Count; Objects=$entries; Pins=$pins; CompilerVersion=$version;
    CompilerSha256=(Get-FileHash $cc).Hash; SdkArchiveSha256=$sdkHash; SdkVersion='v0.42'; VerifiedSdkFiles=$verifiedSdkFiles;
    CanonicalClang18=(-not $AlternativeClang23); ImportStubs=$stubs; ImportStubsLinked=$true; Linked=$false; GearsBuilt=$false; Executed=$false;
    NotCompiled=@('native/main.c (requires actual generated shader metadata)','native/shader_assets.S (requires actual generated shaders)');
    Scope='Windows cross-compile probe of genuine upstream PS5 source and import stubs; not a complete native title or execution'
} | ConvertTo-Json -Depth 6 | Set-Content "$out/report.json" -Encoding utf8
Write-Output "PASS: $($entries.Count) genuine upstream objects cross-compiled; no native link or launch"
