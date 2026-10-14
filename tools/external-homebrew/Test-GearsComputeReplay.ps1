# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param(
    [Parameter(Mandatory)][string]$ReplayFile,
    [string]$Dotnet = 'C:\Dev\dotnet10\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$replay = (Resolve-Path -LiteralPath $ReplayFile).Path
$output = Join-Path $repo ('artifacts/gears-compute-replay/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$stage = Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/gears-native-windows/shaders/gears_lit.gs.bin'
Push-Location $repo
try {
    & $Dotnet run --project tools/SharpEmu.Tools.GearsComputeAudit -c Release -- $replay $stage $output --nonempty
    if ($LASTEXITCODE -ne 0) { throw 'Original clear-draw fixture generation failed' }
    & $Dotnet run --project tools/SharpEmu.Tools.GpuConformance -c Release -- "$output/original-gears-compute.spv" "$output/expected.json" "$output/actual.json" "$output/initial.json" "$output/limits.json" "$output/storage-fixture.json" > "$output/gpu.log" 2>&1
    $gpuCode = $LASTEXITCODE
    $log = Get-Content -LiteralPath "$output/gpu.log" -Raw
    if ($gpuCode -ne 0 -or !$log.Contains('RESULT: all values match')) {
        throw "Original clear-draw numerical GPU audit failed; see $output/gpu.log"
    }
    @{
        Scope = 'Original Gears GS first native clear triangle only; separate captured inputs, exact position/packed parameters/connectivity and idle guards; no scene rendering'
        GpuExecuted = $true; NumericalOutputsVerified = $true; RenderingVerified = $false
        ReplayFile = $replay; ReplaySha256 = (Get-FileHash -LiteralPath $replay).Hash
        Device = ($log -split '\r?\n' | Where-Object { $_ -like 'executing on:*' } | Select-Object -First 1)
        Hashes = @('original-gears-compute.spv','audit-report.json','storage-fixture.json','expected.json','actual.json','initial.json','limits.json','gpu.log') | ForEach-Object {
            @{ File = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $output $_)).Hash }
        }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/execution-report.json" -Encoding UTF8
    Write-Output "PASS original first clear triangle compute outputs; rendering unverified: $output"
} finally { Pop-Location }
