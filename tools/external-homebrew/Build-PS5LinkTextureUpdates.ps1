# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
if(-not (Test-Path "$out/main-texture-static.c")) { & "$PSScriptRoot/Build-PS5LinkTexturePlane.ps1" }
$text=[IO.File]::ReadAllText("$out/main-texture-static.c")
$needle='int slot = frame & 1;'
if(-not $text.Contains($needle)) { throw 'Generated texture plane frame contract changed' }
$text=$text.Replace($needle,@'
int slot = frame & 1;
        /* Reuse the same texture allocation and descriptor. Only CPU contents
         * change, after the previous frame's suspend/vblank boundary. */
        unsigned int phase = (unsigned int)(frame / 150) % 3;
        for (unsigned int y = 0; y < 64; y++) for (unsigned int x = 0; x < 64; x++) {
            unsigned int i = (y * 64 + x) * 4;
            unsigned char value = ((x / 8 + y / 8) & 1) ? 48 : 255;
            unsigned char rgb[3] = {
                (y < 32 ? (x < 32) : (x >= 32)) ? value : 0,
                x >= 32 ? value : 0,
                y >= 32 && x < 32 ? value : 0
            };
            for (unsigned int c = 0; c < 3; c++) pixels[i+c] = rgb[(c+phase)%3];
            pixels[i+3] = 255;
        }
'@)
[IO.File]::WriteAllText("$out/main-texture-updates.c",$text)
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-texture-updates.c" -o "$out/main-texture-updates.o"
if($LASTEXITCODE) { throw 'Texture updates compilation failed' }
& "$out/link_real.exe" "$out/app-texture-updates.elf" "$out/crt1.o" "$out/main-texture-updates.o"
if($LASTEXITCODE) { throw 'Texture updates link failed' }
$report=Get-Content "$out/build-report-texture-static.json" -Raw | ConvertFrom-Json
$report | Add-Member TextureCpuUpdates $true
$report | Add-Member TextureUpdatePeriodFrames 150
$report | Add-Member GeneratedSourceSha256 ((Get-FileHash "$out/main-texture-updates.c").Hash)
$report.FrozenView=$false
$report.ElfSha256=(Get-FileHash "$out/app-texture-updates.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-texture-updates.json" -Encoding utf8
