# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$EnableMouseBridge)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$source = Join-Path $root 'artifacts\external-homebrew\wolf4sdl-engine-source'
$sdk = Join-Path $root 'artifacts\external-homebrew\pacbrew-v0.40.2\opt\ps5-payload-sdk'
$target = Join-Path $sdk 'target'
$homebrew = Join-Path $target 'user\homebrew'
$output = Join-Path $root 'artifacts\external-homebrew\wolf4sdl-userland'
$work = Join-Path $output 'source'
$objects = Join-Path $output 'objects'
$llvm = 'C:\BuildTools\VC\Tools\Llvm\x64\bin'
if ((& git -C $source rev-parse HEAD) -ne '5e639973a665443053c58a0498b68f487537ec80') { throw 'Unexpected engine revision' }
if ((& git -C (Join-Path $source 'SDL_GameControllerDB') rev-parse HEAD) -ne '01275a5bbefbab17f274e60393cd75405f74feb3') { throw 'Unexpected controller database revision' }
foreach ($path in @($work,$objects)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
# Copy only build inputs; the pinned upstream checkout is preserved.
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.h','.cpp','.inc' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $work -Force }
Copy-Item -LiteralPath (Join-Path $source 'mame') -Destination $work -Recurse -Force
$version = Join-Path $work 'version.h'
$text = [IO.File]::ReadAllText($version).Replace('//#define UPLOAD','#define UPLOAD').Replace('#define GOODTIMES','//#define GOODTIMES')
[IO.File]::WriteAllText($version,$text)
$mappings = Get-Content (Join-Path $source 'SDL_GameControllerDB\gamecontrollerdb.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#') }
$mappingText = ($mappings -join "`n").Replace('\','\\').Replace('"','\"').Replace("`n",'\n')
$mappingHeader = Join-Path $output 'controller-mappings.h'
[IO.File]::WriteAllText($mappingHeader, '#define SDLMAPPINGS "' + $mappingText + '"')
$flags = @('--target=x86_64-sie-ps5',('-isysroot' + $sdk),'-isystem',(Join-Path $target 'include\c++\v1'),
    '-isystem',(Join-Path $target 'include'),
    '-I',(Join-Path $homebrew 'include'),'-I',(Join-Path $homebrew 'include\SDL2'),
    '-include',$mappingHeader,'-O2','-fPIC','-fno-plt','-fno-stack-protector','-femulated-tls',
    '-Wno-c++11-narrowing','-DDATADIR="/app0/"')
$sources = @('mame/fmopl.cpp','id_ca.cpp','id_in.cpp','id_pm.cpp','id_sd.cpp','id_us_1.cpp',
    'id_vh.cpp','id_vl.cpp','signon.cpp','wl_act1.cpp','wl_act2.cpp','wl_agent.cpp','wl_atmos.cpp',
    'wl_cloudsky.cpp','wl_debug.cpp','wl_draw.cpp','wl_floorceiling.cpp','wl_game.cpp','wl_inter.cpp',
    'wl_main.cpp','wl_menu.cpp','wl_parallax.cpp','wl_play.cpp','wl_state.cpp','wl_text.cpp')
$objectPaths = @()
foreach ($file in $sources) {
    $object = Join-Path $objects (($file -replace '/','-') + '.o')
    & (Join-Path $llvm 'clang++.exe') @flags -c (Join-Path $work $file) -o $object
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $file" }
    $objectPaths += $object
    Write-Output "Compiled $file"
}
$mouseLinkFlags=@()
if($EnableMouseBridge) {
    $mouseObject=Join-Path $objects 'mouse-bridge.o'
    & (Join-Path $llvm 'clang++.exe') @flags -c (Join-Path $PSScriptRoot 'wolf4sdl-mouse.cpp') -o $mouseObject
    if($LASTEXITCODE -ne 0) { throw 'Mouse source adapter compilation failed' }
    $objectPaths+=$mouseObject
    $stubObject=Join-Path $objects 'mouse-import-stubs.o'
    & (Join-Path $llvm 'clang.exe') --target=x86_64-sie-ps5 -fPIC -c (Join-Path $PSScriptRoot 'mouse-import-stubs.c') -o $stubObject
    if($LASTEXITCODE -ne 0) { throw 'Mouse import symbols compilation failed' }
    & (Join-Path $llvm 'ld.lld.exe') -shared -soname libSceMouse.so -o (Join-Path $objects 'libSceMouse.so') $stubObject
    if($LASTEXITCODE -ne 0) { throw 'Mouse link-time provider failed' }
    $mouseLinkFlags=@('--wrap=SDL_GetMouseState','--wrap=SDL_WarpMouseInWindow',('-L'+$objects),'-lSceMouse')
}
$start = Join-Path $objects 'userland-start.o'
& (Join-Path $llvm 'clang.exe') --target=x86_64-sie-ps5 -c (Join-Path $PSScriptRoot 'wolf4sdl-start.S') -o $start
if ($LASTEXITCODE -ne 0) { throw 'Userland entry compilation failed' }
$elf = Join-Path $output 'wolf3d-shareware-userland.elf'
if($EnableMouseBridge) { $elf=Join-Path $output 'wolf3d-shareware-userland-mouse.elf' }
$linkerScript = Join-Path $root 'artifacts\external-homebrew\sdk-v0.43\ps5-payload-sdk\ldscripts\elf_x86_64.x'
& (Join-Path $llvm 'ld.lld.exe') -m elf_x86_64 -pie --eh-frame-hdr -z max-page-size=0x4000 `
    -T $linkerScript --entry=_start -o $elf $start @objectPaths `
    ('-L' + (Join-Path $homebrew 'lib')) ('-L' + (Join-Path $target 'lib')) `
    --start-group -lSDL2_mixer -lSDL2 -liconv -lc -lc++ -lc++abi -lunwind --end-group `
    -lkernel_web -lSceLibcInternal -lSceNet -lSceAudioOut -lScePad -lSceUserService -lSceVideoOut -lSceSystemService -lSceImeDialog -lSceKeyboard @mouseLinkFlags
if ($LASTEXITCODE -ne 0) { throw 'Source build link failed; inspect unresolved dependencies' }
$report = [ordered]@{
    Profile='Explicit emulator userland startup, not the unchanged upstream release'
    MouseBridge=[bool]$EnableMouseBridge
    EngineCommit=(& git -C $source rev-parse HEAD)
    EngineChanges=@(& git -C $source status --porcelain)
    ControllerDbCommit=(& git -C (Join-Path $source 'SDL_GameControllerDB') rev-parse HEAD)
    BundleVersion='v0.40.2'
    BundleSha256=(Get-FileHash (Join-Path $root 'artifacts\external-homebrew\ps5-payload-dev-v0.40.2.tar.gz')).Hash
    LinkerScriptSha256=(Get-FileHash -LiteralPath $linkerScript).Hash
    StartupSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'wolf4sdl-start.S')).Hash
    CompilerVersion=(& (Join-Path $llvm 'clang++.exe') --version | Select-Object -First 1)
    ElfSha256=(Get-FileHash -LiteralPath $elf).Hash
    Elf=$elf
    GameplayVerified=$false
}
$reportFile=if($EnableMouseBridge) {'build-report-mouse.json'} else {'build-report.json'}
$report | ConvertTo-Json | Set-Content (Join-Path $output $reportFile) -Encoding utf8
Write-Output "Built PS5 source profile: $elf"
