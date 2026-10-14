# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deps = Join-Path $repo 'artifacts/external-homebrew/gears-toolchain'
$gears = Join-Path $repo 'artifacts/external-homebrew/ps5-agc-gears'
if ((& git -C $gears rev-parse HEAD) -ne '1ae1f9182abd2770c131b97419034fb85173c2dc' -or (& git -C $gears status --porcelain)) {
    throw 'Clean pinned Gears source required'
}
if ((& git -C "$deps/llpc" rev-parse HEAD) -ne '23a0757d922da7eb06c6a626d48553e0fb99fde0') { throw 'Pinned LLPC required' }
$compiler = Join-Path $deps 'build-windows/llpc/llpc/amdllpc.exe'
$llvm = Join-Path $deps 'build-windows/llpc/llvm/bin'
$originalRoot = Join-Path $deps 'gears-native-windows'
$original = Get-Content "$originalRoot/shaders/gears_lit.manifest.json" -Raw | ConvertFrom-Json
$buildReport = Get-Content "$originalRoot/shader-report.json" -Raw | ConvertFrom-Json
$pipe = Join-Path $gears 'shaders/gears_lit.pipe'
if ((Get-FileHash -LiteralPath $pipe).Hash -ne $original.source_sha256 -or
    (Get-FileHash -LiteralPath $compiler).Hash -ne $buildReport.CompilerSha256) { throw 'Original shader provenance mismatch' }
$run = Join-Path $repo ('artifacts/gears-ngg-mode/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
& $compiler '-gfxip=10.1.3' '-enable-outs' "-log-file-outs=$run/compiler-outs.log" "-o=$run/gears.pal.elf" $pipe > "$run/process.log" 2>&1
if ($LASTEXITCODE -ne 0) { throw "Audit compilation failed: $run" }
$outs = Get-Content "$run/compiler-outs.log" -Raw
$settings = [ordered]@{}
foreach ($name in @('EnableNgg', 'EnableGsUse', 'PassthroughMode', 'CompactVertex',
    'EnableBackfaceCulling', 'EnableFrustumCulling', 'EnableBoxFilterCulling',
    'EnableSphereCulling', 'EnableSmallPrimFilter', 'EnableCullDistanceCulling')) {
    $match = [regex]::Match($outs, "(?m)^$name\s*=\s*(\d+)\s*$")
    if (!$match.Success) { throw "Missing NGG setting: $name" }
    $settings[$name] = [int]$match.Groups[1].Value
}
if ($settings.EnableNgg -ne 1 -or $settings.EnableGsUse -ne 0 -or $settings.PassthroughMode -ne 1 -or $settings.CompactVertex -ne 0) {
    throw "Unexpected Gears NGG mode: $run"
}
& "$llvm/llvm-objcopy.exe" "--dump-section=.text=$run/text.bin" "$run/gears.pal.elf"
if ($LASTEXITCODE -ne 0) { throw 'Shader text extraction failed' }
& "$llvm/llvm-readelf.exe" --symbols --elf-output-style=GNU "$run/gears.pal.elf" > "$run/symbols.log"
if ($LASTEXITCODE -ne 0) { throw 'Shader symbol extraction failed' }
$symbols = Get-Content "$run/symbols.log" -Raw
$text = [IO.File]::ReadAllBytes("$run/text.bin")
$stages = @()
foreach ($name in @('pre_raster_gs', 'pixel')) {
    $stage = $original.stages.$name
    $symbol = [regex]::Escape($stage.symbol)
    $match = [regex]::Match($symbols, "(?m)^\s*\d+:\s+([0-9a-fA-F]+)\s+(\d+)\s+FUNC.*\s$symbol\s*$")
    if (!$match.Success) { throw "Missing symbol: $symbol" }
    $offset = [Convert]::ToInt32($match.Groups[1].Value, 16)
    $size = [int]$match.Groups[2].Value
    if ($size -le 0 -or [long]$offset + $size -gt $text.Length) { throw 'Shader symbol exceeds text' }
    $bytes = [byte[]]::new($size)
    [Array]::Copy($text, $offset, $bytes, 0, $size)
    [IO.File]::WriteAllBytes("$run/$name.bin", $bytes)
    $hash = (Get-FileHash -LiteralPath "$run/$name.bin").Hash
    if ($hash -ne $stage.sha256 -or $size -ne $stage.bytes) { throw "Original stage mismatch: $name" }
    $stages += @{ Name = $name; Bytes = $size; Sha256 = $hash; OriginalStageMatches = $true }
}
[ordered]@{
    Scope = 'Isolated pinned original Gears compile with diagnostic logs; exact original stage bytes; no emulator execution'
    Settings = $settings; Stages = $stages; GearsExecuted = $false
    CompilerSha256 = (Get-FileHash -LiteralPath $compiler).Hash
    PipeSha256 = (Get-FileHash -LiteralPath $pipe).Hash
    LogSha256 = (Get-FileHash -LiteralPath "$run/compiler-outs.log").Hash
    PalElfSha256 = (Get-FileHash -LiteralPath "$run/gears.pal.elf").Hash
    OriginalPalElfSha256 = $original.pal_elf_sha256
    OriginalManifestSha256 = (Get-FileHash -LiteralPath "$originalRoot/shaders/gears_lit.manifest.json").Hash
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$run/mode-report.json" -Encoding UTF8
Write-Output "PASS exact Gears stages use NGG passthrough with no compaction: $run"
