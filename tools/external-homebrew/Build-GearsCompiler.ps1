# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(0,1800)][int]$BuildSeconds=0)
$ErrorActionPreference='Stop'
$mutex=[Threading.Mutex]::new($false,'Local\TouchePX5GearsCompiler')
if(-not $mutex.WaitOne(0)) { $mutex.Dispose(); throw 'Another owned Gears compiler build is active' }
try {
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
New-Item -ItemType Directory -Path $out -Force | Out-Null
& "$PSScriptRoot/Get-GearsToolchainSources.ps1" > "$out/source-verification.log"
$cmake='C:/Dev/tools/cmake/cmake-4.4.3-windows-x86_64/bin/cmake.exe'
$vcvars='C:/BuildTools/VC/Auxiliary/Build/vcvars64.bat'
$ninja='C:/Dev/tools/ninja/ninja.exe'
foreach($file in @($cmake,$vcvars,$ninja)) { if(-not (Test-Path $file)) { throw "Required host tool missing: $file" } }
if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($out))).AvailableFreeSpace -lt 25GB) { throw 'At least 25 GiB free required before configuring/building LLVM' }
$zip=Join-Path $out 'dxc_2025_05_24.zip'
$url='https://github.com/microsoft/DirectXShaderCompiler/releases/download/v1.8.2505/dxc_2025_05_24.zip'
$sha='81380F3ECA156D902D6404FD6DF9F4B0886F576FF3E18B2CC10D3075FFC9D119'
if(-not (Test-Path $zip)) { Invoke-WebRequest $url -OutFile $zip }
if((Get-Item $zip).Length -ne 26599715 -or (Get-FileHash $zip).Hash -ne $sha) { throw 'DXC archive hash/size mismatch' }
$dxc=Join-Path $out 'dxc-v1.8.2505'
if(-not (Test-Path $dxc)) { Expand-Archive -LiteralPath $zip -DestinationPath $dxc }
if((Get-FileHash "$dxc/bin/x64/dxc.exe").Hash -ne '52A3AA8150BDD49071FEF302F6556C99A697F9A85770940C7EDD92F8C7FD7E1F') { throw 'Extracted DXC executable differs from the recorded release' }
$python=(& py -3 -c 'import sys; print(sys.executable)').Trim()
if($LASTEXITCODE) { throw 'Python discovery failed' }
if(Test-Path "$out/python/Scripts/python.exe") { $python="$out/python/Scripts/python.exe" }
$source=(Join-Path $PSScriptRoot 'gears-llpc').Replace('\','/')
$out=$out.Replace('\','/')
$command=@('@echo off','set VSLANG=1033',"call `"$vcvars`" >nul",'if errorlevel 1 exit /b 1',"`"$cmake`" -S `"$source`" -B `"$out/build-windows`" -G Ninja -DCMAKE_MAKE_PROGRAM=`"$ninja`" -DGEARS_SOURCES=`"$out`" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DPython3_EXECUTABLE=`"$python`" -DPYTHON_EXECUTABLE=`"$python`" -DDXC_PATH=`"$dxc/bin/x64`"")
[IO.File]::WriteAllLines("$out/configure-windows.cmd",$command)
& "$out/configure-windows.cmd" > "$out/configure-windows.log" 2>&1
if($LASTEXITCODE) { throw "Compiler configure failed: $out/configure-windows.log" }
@{Configured=$true;Built=$false;SourceLockSha256=(Get-FileHash "$PSScriptRoot/GearsToolchainDependencies.json").Hash;WrapperSha256=(Get-FileHash "$source/CMakeLists.txt").Hash;DxcArchiveSha256=$sha;DxcExecutableSha256=(Get-FileHash "$dxc/bin/x64/dxc.exe").Hash;CmakeSha256=(Get-FileHash $cmake).Hash;NinjaSha256=(Get-FileHash $ninja).Hash} | ConvertTo-Json | Set-Content "$out/configure-report.json" -Encoding utf8
if(-not $BuildSeconds) { Write-Output 'PASS: compiler build configured; compiler is not built'; return }
$command=@('@echo off','set VSLANG=1033',"call `"$vcvars`" >nul",'if errorlevel 1 exit /b 1',"`"$cmake`" --build `"$out/build-windows`" --target amdllpc llvm-readelf llvm-objcopy --parallel 2")
[IO.File]::WriteAllLines("$out/build-windows.cmd",$command)
& $python "$PSScriptRoot/Run-GearsCompilerBuild.py" $out $BuildSeconds
if($LASTEXITCODE) { throw "Compiler build failed: $out/build-windows.log" }
} finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
