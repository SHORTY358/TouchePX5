# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param(
    [Parameter(Mandatory)][string]$ReplayFile,
    [string]$Dotnet = 'C:\Dev\dotnet10\dotnet.exe',
    [switch]$Mesh
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$replay = (Resolve-Path -LiteralPath $ReplayFile).Path
$output = Join-Path $repo ('artifacts/gears-compute-replay/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$stage = Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/gears-native-windows/shaders/gears_lit.gs.bin'
Push-Location $repo
try {
    $mode = if ($Mesh) { '--mesh' } else { '--nonempty' }
    & $Dotnet run --project tools/SharpEmu.Tools.GearsComputeAudit -c Release -- $replay $stage $output $mode
    if ($LASTEXITCODE -ne 0) { throw 'Original clear-draw fixture generation failed' }
    & $Dotnet run --project tools/SharpEmu.Tools.GpuConformance -c Release -- "$output/original-gears-compute.spv" "$output/expected.json" "$output/actual.json" "$output/initial.json" "$output/limits.json" "$output/storage-fixture.json" > "$output/gpu.log" 2>&1
    $gpuCode = $LASTEXITCODE
    $log = Get-Content -LiteralPath "$output/gpu.log" -Raw
    if ($gpuCode -ne 0 -or !$log.Contains('RESULT: all values match')) {
        throw "Original clear-draw numerical GPU audit failed; see $output/gpu.log"
    }
    $audit = Get-Content -LiteralPath "$output/audit-report.json" -Raw | ConvertFrom-Json
    if ($Mesh) {
        & $Dotnet run --project tools/SharpEmu.Tools.GearsComputeAudit -c Release -- --consume-mesh $replay $output
        if ($LASTEXITCODE -ne 0) { throw 'Completed mesh connectivity consumption failed' }
    }
    $controlRejected = $null
    if ($Mesh -and $audit.GroupCount -gt 1) {
        # Wrong per-group source IDs must affect actual geometry, not merely the
        # input-table comparison. Match the expected table to the changed input.
        $fixture = Get-Content -LiteralPath "$output/storage-fixture.json" -Raw | ConvertFrom-Json
        $controlExpected = Get-Content -LiteralPath "$output/expected.json" -Raw | ConvertFrom-Json
        $sourceFirst = [int]$audit.OutputSlots * 17 + [int]$audit.GroupCount * 4
        for ($vertex = 0; $vertex -lt 126; $vertex++) {
            $word = $sourceFirst + 128 * 9 + $vertex * 9 + 5
            $fixture.Buffers[$fixture.OutputBinding][$word] = [uint32]$vertex
            $controlExpected[$word] = [uint32]$vertex
        }
        $fixture | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath "$output/wrong-source-fixture.json" -Encoding UTF8
        ConvertTo-Json -InputObject $controlExpected -Compress | Set-Content -LiteralPath "$output/wrong-source-expected.json" -Encoding UTF8
        & $Dotnet run --project tools/SharpEmu.Tools.GpuConformance -c Release -- "$output/original-gears-compute.spv" "$output/wrong-source-expected.json" "$output/wrong-source-actual.json" "$output/initial.json" "$output/limits.json" "$output/wrong-source-fixture.json" > "$output/wrong-source-gpu.log" 2>&1
        $controlLog = Get-Content -LiteralPath "$output/wrong-source-gpu.log" -Raw
        $controlRejected = $LASTEXITCODE -eq 1 -and $controlLog.Contains('driver accepted the SPIR-V') -and $controlLog.Contains('mismatch(es)')
        if (!$controlRejected) { throw 'Wrong per-group source ID control did not reject' }
        $badWords = @($controlLog -split '\r?\n' | Where-Object { $_ -match '^FAIL  bounded export word (\d+):' } | ForEach-Object {
            [int]([regex]::Match($_, 'word (\d+):').Groups[1].Value)
        })
        if ($badWords.Count -eq 0 -or @($badWords | Where-Object { $_ -lt 128 * 12 -or $_ -ge 256 * 12 }).Count -gt 0) {
            throw 'Source-ID control failed outside the intended second-group vertex exports'
        }
    }
    @{
        Scope = if ($Mesh) { 'Original Gears captured triangle-list mesh; explicit audit groups, independent GLSL float reference; exact material/connectivity/idle guards and unchanged inputs; no scene rendering or hardware scheduling' }
                else { 'Original Gears GS first native clear triangle only; separate captured inputs, exact position/packed parameters/connectivity and idle guards; no scene rendering' }
        GpuExecuted = $true; NumericalOutputsVerified = $true; RenderingVerified = $false
        ReplayFile = $replay; ReplaySha256 = (Get-FileHash -LiteralPath $replay).Hash
        Device = ($log -split '\r?\n' | Where-Object { $_ -like 'executing on:*' } | Select-Object -First 1)
        GroupCount = $audit.GroupCount; DrawCount = $audit.DrawContext.DrawCount
        FloatTolerance = if ($Mesh) { 0.00002 } else { 0 }
        WrongSourceIdsRejected = $controlRejected
        ConnectivityRemapVerified = [bool]$Mesh
        Hashes = @('original-gears-compute.spv','audit-report.json','storage-fixture.json','expected.json','actual.json','initial.json','limits.json','gpu.log',
            'wrong-source-fixture.json','wrong-source-expected.json','wrong-source-actual.json','wrong-source-gpu.log',
            'remapped-indices.json','remap-report.json') | Where-Object { Test-Path -LiteralPath (Join-Path $output $_) } | ForEach-Object {
            @{ File = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $output $_)).Hash }
        }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/execution-report.json" -Encoding UTF8
    Write-Output "PASS original captured draw compute outputs; rendering unverified: $output"
} finally { Pop-Location }
