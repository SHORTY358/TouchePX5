# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param([ValidateRange(1,32)][int]$SequenceLimit = 4, [ValidateRange(1,60)][int]$TimeoutSeconds = 12)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo ('artifacts/gears-frame-replay/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$variables = @('TOUCHEPX5_SHADER_REPLAY_DIR','TOUCHEPX5_SHADER_REPLAY_ADDRESS','TOUCHEPX5_SHADER_REPLAY_DRAW_COUNT','TOUCHEPX5_SHADER_REPLAY_SEQUENCE_LIMIT')
$saved = @{}
foreach ($variable in $variables) { $saved[$variable] = [Environment]::GetEnvironmentVariable($variable) }
try {
    $env:TOUCHEPX5_SHADER_REPLAY_DIR = $output
    $env:TOUCHEPX5_SHADER_REPLAY_ADDRESS = $null
    $env:TOUCHEPX5_SHADER_REPLAY_DRAW_COUNT = $null
    $env:TOUCHEPX5_SHADER_REPLAY_SEQUENCE_LIMIT = "$SequenceLimit"
    & "$PSScriptRoot/Invoke-GearsNative.ps1" -PublicInitAbi -BootDiagnostics -LegacyDefaultsAbi -ShaderAperture -TimeoutSeconds $TimeoutSeconds
    $draws = @()
    foreach ($directory in Get-ChildItem -LiteralPath $output -Directory | Sort-Object Name) {
        $gsFile = Join-Path $directory.FullName '0000000200001000.replay.json'
        $psFile = Join-Path $directory.FullName '0000000200001200.replay.json'
        if (!(Test-Path -LiteralPath $gsFile) -or !(Test-Path -LiteralPath $psFile)) { throw "Incomplete stage pair: $($directory.Name)" }
        $gs = Get-Content -LiteralPath $gsFile -Raw | ConvertFrom-Json
        $ps = Get-Content -LiteralPath $psFile -Raw | ConvertFrom-Json
        if (($gs.DrawContext | ConvertTo-Json -Depth 5 -Compress) -ne ($ps.DrawContext | ConvertTo-Json -Depth 5 -Compress)) { throw 'Stage draw contexts differ' }
        $draws += @{ Directory=$directory.FullName; Context=$gs.DrawContext; GS=(Get-FileHash -LiteralPath $gsFile).Hash; PS=(Get-FileHash -LiteralPath $psFile).Hash }
    }
    if ($draws.Count -eq 0) { throw 'No bounded paired draw capture' }
    @{ Scope='Bounded original native submitted command inputs only; no complete frame rendering'; Draws=$draws; RenderingVerified=$false } |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath "$output/frame-capture-report.json" -Encoding UTF8
    Write-Output "Captured $($draws.Count) original paired draw inputs: $output"
} finally {
    foreach ($variable in $variables) { [Environment]::SetEnvironmentVariable($variable,$saved[$variable]) }
}
