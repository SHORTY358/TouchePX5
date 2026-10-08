# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
# Interactive explicit source profile; closes when the game window is closed.
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$inputRoot=Join-Path $root 'artifacts\external-homebrew\wolf4sdl-userland'
$elf=Join-Path $inputRoot 'wolf3d-shareware-userland-mouse.elf'
$exe=Join-Path $root 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe'
foreach($path in @($elf,$exe,(Join-Path $inputRoot 'VSWAP.WL1'))) {
    if(-not (Test-Path -LiteralPath $path)) { throw "Missing game input: $path" }
}
$config=Join-Path $inputRoot 'mouse-play'
New-Item -ItemType Directory -Path $config -Force | Out-Null
$settings=@{
    SHARPEMU_SOURCE_PROFILE_ARGV='1'
    # Default fullscreen SDL surface fills the physical framebuffer. Windowed
    # mouse mode creates a centered 640x400 surface on the PS5 SDL backend.
    SHARPEMU_GUEST_ARGS='--configdir /temp0 --tedlevel 0 --nowait --normal'
    SHARPEMU_TEMP0_DIR=$config
    SHARPEMU_MOUSE_RELATIVE='1'
    SHARPEMU_TEST_KEYBOARD_REPLAY=$null
}
$previous=@{}
$process=$null
try {
    foreach($key in $settings.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$settings[$key],'Process')
    }
    $process=Start-Process -FilePath $exe -WorkingDirectory $inputRoot -WindowStyle Hidden -PassThru `
        -ArgumentList @('--cpu-engine=native','--generation=ps5',('--log-file="'+(Join-Path $config 'runtime.log')+'"'),('"'+$elf+'"')) `
        -RedirectStandardOutput (Join-Path $config 'stdout.log') -RedirectStandardError (Join-Path $config 'stderr.log')
    $process.WaitForExit()
} finally {
    if($null -ne $process) { $process.Dispose() }
    foreach($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
