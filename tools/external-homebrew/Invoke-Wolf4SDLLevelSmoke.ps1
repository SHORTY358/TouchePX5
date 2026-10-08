# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$captureRoot=Join-Path $root ('artifacts\external-homebrew\wolf4sdl-userland\level-smoke\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$settings=@{
    SHARPEMU_DUMP_VIDEOOUT='1'
    SHARPEMU_DUMP_VIDEOOUT_AFTER_FLIP='500'
    SHARPEMU_DUMP_VIDEOOUT_EVERY_FLIP='10'
    SHARPEMU_DUMP_VIDEOOUT_MAX_FRAMES='32'
    SHARPEMU_LOG_VIDEOOUT='1'
    SHARPEMU_TRACE_GUEST_IMAGES='present'
    SHARPEMU_GUEST_IMAGE_DUMP_DIR=$captureRoot
    SHARPEMU_GUEST_IMAGE_DUMP_CONTINUOUS='0'
    SHARPEMU_SWAPCHAIN_DUMP_EVERY='100'
    SHARPEMU_OVERLAY='0'
}
$previous=@{}
try {
    foreach($key in $settings.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$settings[$key],'Process')
    }
    & (Join-Path $PSScriptRoot 'Invoke-Wolf4SDLUserland.ps1') -TimeoutSeconds 60 -KeyboardReplay `
        '6000:13:300;14000:13:300;22000:27:150;26000:78:200;29000:78:200;32000:13:300;37000:13:300;42000:13:300;48000:38:1000;51000:162:1500;55000:39:1000;58000:162:800'
    Write-Output "Actual swapchain samples: $captureRoot"
    Write-Output 'Bounded diagnostic scenario finished; inspect its captured level and ammunition before claiming gameplay.'
} finally {
    foreach($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
