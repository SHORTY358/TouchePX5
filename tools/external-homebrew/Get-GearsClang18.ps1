# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$url='https://github.com/mstorsjo/llvm-mingw/releases/download/20240619/llvm-mingw-20240619-ucrt-x86_64.zip'
$archive=Join-Path $out 'llvm-mingw-20240619-ucrt-x86_64.zip'
# Observed digest of the official release asset; not an upstream signature.
$sha='810703594A7E3EEA03385B5329C7EA3BD65F5E496B44CF1B68C17FF436D265E7'
$exeSha='2E1C3555B5C733D619B62F1F35D6DEFB5358CF94A01B2263B81F9ECFC0E9E08A'
if(-not(Test-Path $archive)) { Invoke-WebRequest $url -OutFile $archive }
if((Get-Item $archive).Length -ne 142797010 -or (Get-FileHash $archive).Hash -ne $sha) { throw 'LLVM-Mingw archive hash/size differs from recorded official asset' }
$extract=Join-Path $out 'clang18'
$compiler=Join-Path $extract 'llvm-mingw-20240619-ucrt-x86_64/bin/clang.exe'
if(-not(Test-Path $compiler)) { Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force }
if((Get-FileHash $compiler).Hash -ne $exeSha) { throw 'Extracted compiler differs from recorded official asset' }
$version=(& $compiler --version | Out-String).Trim()
if($LASTEXITCODE -or $version -notmatch 'clang version 18\.1\.8') { throw 'Clang 18.1.8 required' }
@{Url=$url; ArchiveSha256=$sha; ArchiveBytes=142797010; CompilerSha256=$exeSha; Version=$version;
    UpstreamPublishedDigest=$false; SystemInstallation=$false; PathChanged=$false
} | ConvertTo-Json | Set-Content "$out/clang18-release-report.json" -Encoding utf8
Write-Output 'PASS: isolated official LLVM-Mingw Clang 18.1.8 verified; no system PATH change'
