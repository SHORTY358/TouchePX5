# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([string]$ToolRoot='C:/Dev/tools/llvm-amdgpu-18.1.8',[ValidateRange(1,8)][int]$Jobs=3)
$ErrorActionPreference='Stop'
$ToolRoot=[IO.Path]::GetFullPath($ToolRoot)
$cmake='C:/Dev/tools/cmake/cmake-4.4.3-windows-x86_64/bin/cmake.exe'
$ninja='C:/Dev/tools/ninja/ninja.exe'
$vcvars='C:/BuildTools/VC/Auxiliary/Build/vcvars64.bat'
foreach($tool in @($cmake,$ninja,$vcvars)) {
    if(-not (Test-Path -LiteralPath $tool)) { throw "Missing build tool: $tool" }
}
# Pinned official release archives. Partial downloads must not be extracted.
$archives=@{'llvm-18.1.8.src.tar.xz'=61986468;'cmake-18.1.8.src.tar.xz'=8708}
$hashes=@{
    'llvm-18.1.8.src.tar.xz'='F68CF90F369BC7D0158BA70D860B0CB34DBC163D6FF0EBC6CFA5E515B9B2E28D'
    'cmake-18.1.8.src.tar.xz'='59BADEF592DD34893CD319D42B323AAA990B452D05C7180FF20F23AB1B41E837'
}
foreach($name in $archives.Keys) {
    $path=Join-Path $ToolRoot $name
    if(-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -ne $archives[$name]) {
        throw "Missing or incomplete official LLVM archive: $path"
    }
    if((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$name]) { throw "LLVM archive hash mismatch: $name" }
}
$llvmSource=Join-Path $ToolRoot 'llvm-18.1.8.src'
$cmakeSource=Join-Path $ToolRoot 'cmake'
function Expand-Source([string]$Archive,[string]$Destination,[int]$Strip=0) {
    # Windows' bundled tar may lack xz; Python's standard library supports it.
    @'
import sys, tarfile
archive, destination, strip = sys.argv[1], sys.argv[2], int(sys.argv[3])
with tarfile.open(archive, 'r:xz') as source:
    members = source.getmembers()
    if strip:
        for member in members:
            member.name = '/'.join(member.name.split('/')[strip:])
        members = [member for member in members if member.name]
    source.extractall(destination, members=members, filter='data')
'@ | py -3 - $Archive $Destination $Strip
    if($LASTEXITCODE) { throw "Source extraction failed: $Archive" }
}
if(-not (Test-Path -LiteralPath "$ToolRoot/llvm-source-extracted.ok")) {
    Expand-Source "$ToolRoot/llvm-18.1.8.src.tar.xz" $ToolRoot
    Set-Content -LiteralPath "$ToolRoot/llvm-source-extracted.ok" -Value $hashes['llvm-18.1.8.src.tar.xz']
}
if(-not (Test-Path -LiteralPath "$ToolRoot/cmake-source-extracted.ok")) {
    New-Item -ItemType Directory -Path $cmakeSource -Force | Out-Null
    Expand-Source "$ToolRoot/cmake-18.1.8.src.tar.xz" $cmakeSource 1
    Set-Content -LiteralPath "$ToolRoot/cmake-source-extracted.ok" -Value $hashes['cmake-18.1.8.src.tar.xz']
}
$build=Join-Path $ToolRoot 'build'
$batch=Join-Path $ToolRoot 'build-tools.cmd'
# cmd is used only for the supplied MSVC environment and compiler invocation.
# No cross-shell filesystem deletion or moving is performed.
@"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
"$cmake" -S "$llvmSource" -B "$build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_MAKE_PROGRAM="$ninja" -DLLVM_TARGETS_TO_BUILD=AMDGPU -DLLVM_INCLUDE_TESTS=OFF -DLLVM_INCLUDE_BENCHMARKS=OFF -DLLVM_INCLUDE_EXAMPLES=OFF -DLLVM_ENABLE_ZLIB=OFF -DLLVM_ENABLE_ZSTD=OFF -DLLVM_ENABLE_LIBXML2=OFF -DLLVM_ENABLE_TERMINFO=OFF -DLLVM_ENABLE_DIA_SDK=OFF
if errorlevel 1 exit /b 1
"$ninja" -C "$build" -j $Jobs llvm-mc llvm-objcopy
if errorlevel 1 exit /b 1
"$build/bin/llvm-mc.exe" --version
"@ | Set-Content -LiteralPath $batch -Encoding ascii
& cmd.exe /d /c $batch
if($LASTEXITCODE) { throw "LLVM tool build failed; retrying reuses $build" }
if(-not (Test-Path -LiteralPath "$build/bin/llvm-mc.exe") -or
   -not (Test-Path -LiteralPath "$build/bin/llvm-objcopy.exe")) {
    throw 'LLVM build did not produce both required tools'
}
Write-Output "LLVM AMDGPU tools ready: $build/bin"
