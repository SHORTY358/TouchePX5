# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(30,1800)][int]$TimeoutSeconds=600)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/cygwin-host'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$setup=Join-Path $out 'setup-x86_64.exe'
$expected='6acea47c59781c9e7f544a18d53935d59df6e44d5d52ac95ee165671b8e388820455eebf30ccc7d254b00c3d0eb694269a0f8dc84b17944c1f355dac9c5aafcc'
if(-not(Test-Path $setup)) { Invoke-WebRequest 'https://cygwin.com/setup-x86_64.exe' -OutFile $setup }
if((Get-FileHash $setup -Algorithm SHA512).Hash -ne $expected) { throw 'Installer differs from the official 2026-10-13 SHA512; review upstream before updating the pin' }
$install=Join-Path $out 'root'
$cache=Join-Path $out 'packages'
# Normal repository signature and package digest verification remain enabled.
# No Windows features, global PATH, shortcuts, registry root or elevation.
$arguments=@('--no-admin','--no-write-registry','--no-shortcuts','--no-replaceonreboot',
 '--quiet-mode=hidden','--only-site','--site=https://mirrors.kernel.org/sourceware/cygwin/',
 '--root',('"'+$install+'"'),'--local-package-dir',('"'+$cache+'"'),
 '--packages=gcc-core,make,python3')
$process=Start-Process -FilePath $setup -ArgumentList $arguments -WorkingDirectory $out -WindowStyle Hidden -PassThru
$timer=[Diagnostics.Stopwatch]::StartNew()
try {
 while(-not $process.WaitForExit(1000)) {
  if($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Owned Cygwin installation reached its bounded timeout; cache is retained' }
 }
 if($process.ExitCode) { throw "Cygwin setup failed with exit $($process.ExitCode); inspect root/var/log/setup.log.full" }
 foreach($tool in @('bash.exe','gcc.exe','make.exe','python3.12.exe')) { if(-not(Test-Path "$install/bin/$tool")) { throw "Missing installed tool $tool" } }
 & "$install/bin/bash.exe" --noprofile --norc -c 'export PATH=/usr/bin:/bin; python3 --version'
 if($LASTEXITCODE) { throw 'Cygwin Python verification failed' }
 @{InstallerSha512=$expected; InstallerSource='https://cygwin.com/setup-x86_64.exe'; HashSource='https://cygwin.com/sha512.sum';
   Root=$install; SignatureVerificationEnabled=$true; Packages=@('gcc-core','make','python3');
   ExitCode=$process.ExitCode; Installed=$true; TestsExecuted=$false} | ConvertTo-Json -Depth 4 | Set-Content "$out/setup-report.json" -Encoding UTF8
} finally {
 if(-not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
}
