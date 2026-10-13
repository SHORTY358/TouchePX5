# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$ExitSecondWorker)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sdk=Join-Path $root 'artifacts/external-homebrew/ps5link-sdk'
$pin='ea771e535378740b6a058b8e5419eb8a0e0e0ec8'
if((& git -C $sdk rev-parse HEAD) -ne $pin) { throw 'Unexpected SDK revision' }
if(& git -C $sdk status --porcelain) { throw 'Upstream SDK must remain unchanged' }
$out=Join-Path $root 'artifacts/native-guest-threads'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$source=Join-Path $PSScriptRoot 'fixtures/ps5-touche-guest-threads/threads.c'
$clang='C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe'
$link=Join-Path $root 'artifacts/external-homebrew/ps5link-cube/link_real.exe'
if(-not (Test-Path -LiteralPath $link)) { throw 'Build the SDK linker first' }
$extra=@()
if($ExitSecondWorker) { $extra+= '-DEXIT_SECOND_WORKER=1' }
& $clang --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector @extra -c $source -o "$out/threads.o"
if($LASTEXITCODE) { throw 'Native thread source compilation failed' }
& $clang --target=x86_64-sie-ps5 -c "$sdk/linker/crt1.S" -o "$out/crt1.o"
if($LASTEXITCODE) { throw 'Startup compilation failed' }
& $link "$out/threads.elf" "$out/crt1.o" "$out/threads.o"
if($LASTEXITCODE) { throw 'Native thread ELF link failed' }
@{
    Scope='original native thread ABI probe; no graphics'
    StartupRepository='https://github.com/Rufidj/ps5link-sdk'; StartupCommit=$pin
    StartupModified=$false; SourceSha256=(Get-FileHash $source).Hash
    ExitSecondWorker=[bool]$ExitSecondWorker
    ElfSha256=(Get-FileHash "$out/threads.elf").Hash
    LinkerSha256=(Get-FileHash $link).Hash
} | ConvertTo-Json | Set-Content "$out/build-report.json" -Encoding utf8
