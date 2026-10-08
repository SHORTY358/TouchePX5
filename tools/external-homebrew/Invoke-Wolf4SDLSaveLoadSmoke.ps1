# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
# Every invocation owns a new save directory and never overwrites player saves.
$configuration='save-roundtrip-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$configurationRoot=Join-Path $root ('artifacts\external-homebrew\wolf4sdl-userland\'+$configuration)
$settings=@{
    SHARPEMU_LOG_STDIO='1'
    SHARPEMU_DUMP_VIDEOOUT='1'
    SHARPEMU_DUMP_VIDEOOUT_AFTER_FLIP='650'
    SHARPEMU_DUMP_VIDEOOUT_EVERY_FLIP='15'
    SHARPEMU_DUMP_VIDEOOUT_MAX_FRAMES='32'
}
$previous=@{}
$prefix='6000:13:300;14000:13:300;22000:27:150;26000:78:200;29000:78:200;32000:13:300;37000:13:300;42000:13:300;'
try {
    foreach($key in $settings.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$settings[$key],'Process')
    }
    Write-Output 'Phase 1/2: start a level, fire, turn, save SOL2 (60 seconds).'
    & (Join-Path $PSScriptRoot 'Invoke-Wolf4SDLUserland.ps1') -TimeoutSeconds 60 -ConfigSubdirectory $configuration -KeyboardReplay `
        ($prefix+'48000:162:1500;50000:39:500;52000:113:300;54000:13:300;55000:83:150;55300:79:150;55600:76:150;55900:50:150;57000:13:300')
    $savePath=Join-Path $configurationRoot 'savegam0.wl1'
    if(-not (Test-Path -LiteralPath $savePath)) { throw 'The game did not create its save; inspect phase 1 captures.' }
    $bytes=[IO.File]::ReadAllBytes($savePath)
    if($bytes.Length -lt 54) { throw 'Save is shorter than its name and gamestate header.' }
    $name=[Text.Encoding]::ASCII.GetString($bytes,0,32).Split([char]0)[0]
    # Public Wolf4SDL save layout: 32-byte name, then gametype. Ammo is a
    # short at gametype offset 20 in this x86-64 source build.
    $ammo=[BitConverter]::ToInt16($bytes,52)
    if($name -ne 'sol2' -or $ammo -ne 7) { throw "Changed-state save not confirmed: slot='$name', ammo=$ammo. Inspect phase 1 captures." }
    Write-Output "Save header verified: '$name', ammo=$ammo, bytes=$($bytes.Length)."
    Write-Output 'Phase 2/2: restart, load the slot with F3, then move (60 seconds).'
    & (Join-Path $PSScriptRoot 'Invoke-Wolf4SDLUserland.ps1') -TimeoutSeconds 60 -ConfigSubdirectory $configuration -KeyboardReplay `
        ($prefix+'46000:114:300;49000:13:300;55000:38:1000')
    Write-Output "Save retained at: $savePath"
    Write-Output 'Inspect phase 2 fread trace and captured ammo/view before claiming a successful game-state roundtrip.'
} finally {
    foreach($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
