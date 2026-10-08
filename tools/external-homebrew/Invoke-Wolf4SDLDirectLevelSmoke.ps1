# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(10,120)][int]$TimeoutSeconds=50,
      [string]$KeyboardReplay='10000:87:5000;17000:87:3000;22000:32:800;25000:87:5000;34000:162:500',
      [switch]$InspectSurfaces,
      [ValidateRange(0,1000000)][int]$CaptureAfterFlip=50,
      [ValidateRange(0,105)][int]$SurfaceSampleStartSeconds=10)
$ErrorActionPreference='Stop'
$settings=@{
    SHARPEMU_DUMP_VIDEOOUT='1'
    SHARPEMU_DUMP_VIDEOOUT_AFTER_FLIP=[string]$CaptureAfterFlip
    # Odd spacing samples both alternating buffers, rather than only buffer 0.
    SHARPEMU_DUMP_VIDEOOUT_EVERY_FLIP='51'
    SHARPEMU_DUMP_VIDEOOUT_MAX_FRAMES='64'
    SHARPEMU_LOG_VIDEOOUT='1'
    SHARPEMU_OVERLAY='0'
}
$previous=@{}
try {
    foreach($key in $settings.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$settings[$key],'Process')
    }
    & (Join-Path $PSScriptRoot 'Invoke-Wolf4SDLUserland.ps1') -StartLevel 0 `
        -TimeoutSeconds $TimeoutSeconds -KeyboardReplay $KeyboardReplay -InspectSurfaces:$InspectSurfaces `
        -SurfaceSampleStartSeconds $SurfaceSampleStartSeconds `
        -ConfigSubdirectory ('direct-level-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    Write-Output 'Direct source-profile level scenario finished; inspect captured frames before claiming gameplay.'
} finally {
    foreach($key in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')
    }
}
