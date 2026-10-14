# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param([ValidateRange(5,60)][int]$TimeoutSeconds=12)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $repo ('artifacts/gears-native-ngg/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$variables=@('TOUCHEPX5_EXPERIMENTAL_GEARS_NGG','SHARPEMU_LOG_VIDEOOUT','SHARPEMU_TRACE_GUEST_IMAGES','SHARPEMU_GUEST_IMAGE_DUMP_DIR','SHARPEMU_SWAPCHAIN_DUMP_EVERY')
$saved=@{}
foreach($name in $variables) { $saved[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $env:TOUCHEPX5_EXPERIMENTAL_GEARS_NGG='1'
    $env:SHARPEMU_LOG_VIDEOOUT='1'
    $env:SHARPEMU_TRACE_GUEST_IMAGES='present'
    $env:SHARPEMU_GUEST_IMAGE_DUMP_DIR=$output
    $env:SHARPEMU_SWAPCHAIN_DUMP_EVERY='5'
    & "$PSScriptRoot/Invoke-GearsNative.ps1" -PublicInitAbi -BootDiagnostics -LegacyDefaultsAbi -ShaderAperture -TimeoutSeconds $TimeoutSeconds
    $runs=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/gears-shader-aperture-windows/runs'
    $run=Get-ChildItem -LiteralPath $runs -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $nativeReport=Get-Content -LiteralPath "$($run.FullName)/report.json" -Raw | ConvertFrom-Json
    $log=Get-Content -LiteralPath "$($run.FullName)/stderr.log" -Raw
    if($log -match 'GEARS_EVENT_FAILURE|GEARS_LOOP_FAILURE|GEARS_PARKED|experimental_gears_ngg (consume|schedule) failed|unsupported graphics workgroup barrier') {
        throw "Native NGG run failed; inspect $($run.FullName)"
    }
    $frames=@(Get-ChildItem -LiteralPath $output -Filter 'present-*.bgra' | Sort-Object Name)
    if($frames.Count -lt 2) { throw 'Need at least two actual native presented frames' }
    $hashes=@($frames | ForEach-Object { @{File=$_.FullName;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    if(@($hashes.Sha256 | Select-Object -Unique).Count -lt 2) { throw 'Native frame captures did not change' }
    $consumed=[regex]::Matches($log,'experimental_gears_ngg consumed seq=').Count
    $submittedFlips=[regex]::Matches($log,'videoout.submit_flip[^\r\n]*submitted=True').Count
    if($consumed -lt 8 -or $submittedFlips -lt 3) { throw 'Insufficient repeated native draw/flip evidence' }
    @{Scope='Bounded original native Gears with opt-in pinned GS/PS NGG expansion, owned scratch and ordered producer/readback/graphics; not commercial-game compatibility'
        NativeRun=$run.FullName;InputSha256=$nativeReport.InputSha256
        CapturedFrames=$frames.Count;DistinctFrameHashes=@($hashes.Sha256 | Select-Object -Unique).Count
        ConsumedDraws=$consumed;SubmittedFlips=$submittedFlips
        NativePresentedImagesVerified=$true;FrameImagesChange=$true;OwnershipFailureObserved=$false
        RenderingFidelityAutomaticallyVerified=$false
        TerminatedAtDeadline=$nativeReport.TerminatedAtDeadline;ExperimentalModeOnly=$true;Frames=$hashes
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$output/native-ngg-report.json" -Encoding UTF8
    Write-Output "PASS actual native NGG frames change without frame-loop failure: $output"
} finally {
    foreach($name in $variables) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
