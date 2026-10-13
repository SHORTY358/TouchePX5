# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sdk=Join-Path $root 'artifacts/external-homebrew/ps5link-sdk'
$pin='ea771e535378740b6a058b8e5419eb8a0e0e0ec8'
if((& git -C $sdk rev-parse HEAD) -ne $pin -or (& git -C $sdk status --porcelain)) { throw 'Unchanged pinned SDK required' }
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-deterministic-linker'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$original=Join-Path $sdk 'linker/dynwriter.c'
$patched=[IO.File]::ReadAllText($original)
foreach($region in @('code','rodata','data')) {
    $needle="$region.len = align_up($region.len, align);"
    if(([regex]::Matches($patched,[regex]::Escape($needle))).Count -ne 1) { throw "Alignment source contract changed: $region" }
    $patched=$patched.Replace($needle,"buf_pad_to(&$region, align_up($region.len, align));")
}
$patchedSource=Join-Path $out 'dynwriter.c'
[IO.File]::WriteAllText($patchedSource,$patched)
$reportPath=Join-Path $out 'build-report.json'
$exePath=Join-Path $out 'link_real.exe'
if((Test-Path $reportPath) -and (Test-Path $exePath)) {
    $cached=Get-Content $reportPath -Raw | ConvertFrom-Json
    if($cached.BaseCommit -eq $pin -and
       $cached.OriginalSourceSha256 -eq (Get-FileHash $original).Hash -and
       $cached.PatchedSourceSha256 -eq (Get-FileHash $patchedSource).Hash -and
       $cached.LinkerSha256 -eq (Get-FileHash $exePath).Hash) { return }
}
# This local source overlay remains GPL-3.0 with the upstream SDK; do not modify
# its checkout or replace the legacy linker used by other historical fixtures.
$sources=@('link_real','elf_object','linker','catalog','catalog_extra','catalog_lookup','nid','sha1') | ForEach-Object { "$sdk/linker/$_.c" }
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' -O2 -Wno-deprecated-declarations -I "$sdk/linker" @sources $patchedSource -o "$out/link_real.exe"
if($LASTEXITCODE) { throw 'Deterministic linker compilation failed' }
@{
    BaseRepository='https://github.com/Rufidj/ps5link-sdk'; BaseCommit=$pin
    UpstreamModified=$false; LocalSourceLicense='GPL-3.0'
    Change='zero-fill code/rodata/data alignment holes using existing buf_pad_to'
    OriginalSourceSha256=(Get-FileHash $original).Hash
    PatchedSourceSha256=(Get-FileHash $patchedSource).Hash
    LinkerSha256=(Get-FileHash "$out/link_real.exe").Hash
} | ConvertTo-Json | Set-Content "$out/build-report.json" -Encoding utf8
