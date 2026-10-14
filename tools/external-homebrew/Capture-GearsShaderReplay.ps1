# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param([ValidateRange(1,60)][int]$TimeoutSeconds = 12)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo ('artifacts/gears-shader-replay/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$savedDirectory = $env:TOUCHEPX5_SHADER_REPLAY_DIR
$savedAddress = $env:TOUCHEPX5_SHADER_REPLAY_ADDRESS
try {
    $env:TOUCHEPX5_SHADER_REPLAY_DIR = $output
    # Native capture manifests label the pixel shader (0x1200). The original
    # pre-raster GS is at 0x1000; do not filter the vertex backend by the PS.
    $env:TOUCHEPX5_SHADER_REPLAY_ADDRESS = '0x200001000'
    & "$PSScriptRoot/Invoke-GearsNative.ps1" -PublicInitAbi -BootDiagnostics -LegacyDefaultsAbi -ShaderAperture -TimeoutSeconds $TimeoutSeconds
    $file = Join-Path $output '0000000200001000.replay.json'
    if (!(Test-Path -LiteralPath $file)) { throw 'Original GS replay inputs were not captured' }
    $capture = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($capture.Snapshot.ShaderAddress -ne 0x200001000 -or $capture.Snapshot.Schema -ne 1) { throw 'Wrong shader snapshot' }
    @{ Scope = 'Original GS backend input capture only; no successful rendering or GPU replay implied'
        ReplayFile = $file; ReplaySha256 = (Get-FileHash -LiteralPath $file).Hash
        GlobalBindings = $capture.Snapshot.GlobalMemoryBindings.Count; VertexViews = $capture.Snapshot.VertexInputs.Count
        RenderingVerified = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'capture-report.json') -Encoding UTF8
    Write-Output "Original GS input snapshot: $output"
} finally {
    $env:TOUCHEPX5_SHADER_REPLAY_DIR = $savedDirectory
    $env:TOUCHEPX5_SHADER_REPLAY_ADDRESS = $savedAddress
}
