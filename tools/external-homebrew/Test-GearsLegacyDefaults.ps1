# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deps=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain'
$gears=Join-Path $repo 'artifacts/external-homebrew/ps5-agc-gears'
$out=Join-Path $deps 'gears-legacy-defaults-windows'
$compiler=Join-Path $deps 'clang18/llvm-mingw-20240619-ucrt-x86_64/bin/clang.exe'
$source=[IO.File]::ReadAllText("$gears/tests/test_ps5_color_target.c")
$source=$source.Replace('../src/ps5_color_target.h',"$out/src/ps5_color_target.h".Replace('\','/')).Replace('PS5_FW_1202_DEFAULT_COUNT','PS5_HLE_LEGACY_DEFAULT_COUNT').Replace('PS5_FW_1202_CX_DEFAULT_COUNT','PS5_HLE_LEGACY_CX_DEFAULT_COUNT')
$source=$source.Replace('{UINT32_C(0x38e92c91), 72u << 2}','{UINT32_C(0x38e92c91), 72u << 2, 0u}').Replace('(struct ps5_agc_type_index){0u, 0u}','(struct ps5_agc_type_index){0u, 0u, 0u}')
$source=$source.Replace('int main(void)','_Static_assert(sizeof(struct ps5_agc_type_index) == 12, "legacy type stride");'+"`n"+'int main(void)')
# Preserve every original key, bank, duplicate, register-order, count and address
# assertion. Only the explicit consumer ABI/profile differs from FW12.02.
[IO.File]::WriteAllText("$out/test_color_legacy.c",$source)
& $compiler -std=c11 -O2 -Wall -Wextra -Werror -static "$out/test_color_legacy.c" "$out/src/ps5_color_target.c" -o "$out/test_color_legacy.exe" > "$out/color-host-build.log" 2>&1
if($LASTEXITCODE) { throw 'Legacy selector host compilation failed' }
& "$out/test_color_legacy.exe" > "$out/color-host-test.log" 2>&1
if($LASTEXITCODE) { throw 'Legacy selector assertion failed' }
@{Passed=$true; Firmware1202Test=$false; Scope='same strict color selector assertions under explicit legacy profile';
  OriginalTestSha256=(Get-FileHash "$gears/tests/test_ps5_color_target.c").Hash;
  AdaptedTestSha256=(Get-FileHash "$out/test_color_legacy.c").Hash; ExecutableSha256=(Get-FileHash "$out/test_color_legacy.exe").Hash} |
  ConvertTo-Json | Set-Content "$out/color-host-report.json" -Encoding UTF8
