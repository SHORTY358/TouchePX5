# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param([ValidateRange(5,60)][int]$TimeoutSeconds=12,[switch]$RuntimeScalars,[switch]$VerifyTranslationCache,
    [ValidateRange(1,600)][int]$CaptureEvery=5,[switch]$PreserveFlipPayloads)
$ErrorActionPreference='Stop'
if($VerifyTranslationCache -and -not $RuntimeScalars) { throw 'Translation cache verification requires runtime scalars' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $repo ('artifacts/gears-native-ngg/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$variables=@('TOUCHEPX5_EXPERIMENTAL_GEARS_NGG','TOUCHEPX5_GEARS_RUNTIME_SCALARS','TOUCHEPX5_GEARS_VERIFY_TRANSLATION_CACHE','TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO','SHARPEMU_LOG_VIDEOOUT','SHARPEMU_TRACE_GUEST_IMAGES','SHARPEMU_GUEST_IMAGE_DUMP_DIR','SHARPEMU_SWAPCHAIN_DUMP_EVERY')
$saved=@{}
foreach($name in $variables) { $saved[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $env:TOUCHEPX5_EXPERIMENTAL_GEARS_NGG='1'
    $env:TOUCHEPX5_GEARS_RUNTIME_SCALARS=if($RuntimeScalars) { '1' } else { '0' }
    $env:TOUCHEPX5_GEARS_VERIFY_TRANSLATION_CACHE=if($VerifyTranslationCache) { '1' } else { '0' }
    $env:TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO=if($PreserveFlipPayloads) { '1' } else { '0' }
    $env:SHARPEMU_LOG_VIDEOOUT='1'
    $env:SHARPEMU_TRACE_GUEST_IMAGES='present'
    $env:SHARPEMU_GUEST_IMAGE_DUMP_DIR=$output
    $env:SHARPEMU_SWAPCHAIN_DUMP_EVERY=[string]$CaptureEvery
    & "$PSScriptRoot/Invoke-GearsNative.ps1" -PublicInitAbi -BootDiagnostics -LegacyDefaultsAbi -ShaderAperture -TimeoutSeconds $TimeoutSeconds
    $runs=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/gears-shader-aperture-windows/runs'
    $run=Get-ChildItem -LiteralPath $runs -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $nativeReport=Get-Content -LiteralPath "$($run.FullName)/report.json" -Raw | ConvertFrom-Json
    if(-not $nativeReport.TerminatedAtDeadline -and $nativeReport.ExitCode -ne 0) { throw "Native process failed before its deadline: $($run.FullName)" }
    $log=Get-Content -LiteralPath "$($run.FullName)/stderr.log" -Raw
    if($log -match 'GEARS_EVENT_FAILURE|GEARS_LOOP_FAILURE|GEARS_PARKED|experimental_gears_ngg (consume|schedule) failed|experimental_flip_fifo delivery rejected|unsupported graphics workgroup barrier|ORBIS_GEN2_ERROR_CPU_TRAP|vk.device_lost') {
        throw "Native NGG run failed; inspect $($run.FullName)"
    }
    $frames=@(Get-ChildItem -LiteralPath $output -Filter 'present-*.bgra' | Sort-Object Name)
    if($frames.Count -lt 2) { throw 'Need at least two actual native presented frames' }
    $hashes=@($frames | ForEach-Object { @{File=$_.FullName;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    if(@($hashes.Sha256 | Select-Object -Unique).Count -lt 2) { throw 'Native frame captures did not change' }
    $consumed=[regex]::Matches($log,'experimental_gears_ngg consumed seq=').Count
    $submittedFlips=[regex]::Matches($log,'videoout.submit_flip[^\r\n]*submitted=True').Count
    $presentTimes=@([regex]::Matches($log,'videoout.resolve_flip[^\r\n]*present_ticks=(\d+)') | ForEach-Object { [long]$_.Groups[1].Value })
    $intervals=@(for($i=1;$i -lt $presentTimes.Count;$i++) { ($presentTimes[$i]-$presentTimes[$i-1])*1000.0/[Diagnostics.Stopwatch]::Frequency })
    if($consumed -lt 8 -or $submittedFlips -lt 3) { throw 'Insufficient repeated native draw/flip evidence' }
    $cacheHits=[regex]::Matches($log,'translation_cache=hit').Count
    $verifiedHits=[regex]::Matches($log,'translation_cache_verified').Count
    if($VerifyTranslationCache -and ($verifiedHits -lt 4 -or $verifiedHits -ne $cacheHits)) { throw 'Every reused compute translation must match a fresh compilation' }
    $pixelHits=[regex]::Matches($log,'pixel_cache=hit').Count
    $bridgeHits=[regex]::Matches($log,'bridge_cache=hit').Count
    $pixelVerified=[regex]::Matches($log,'pixel_cache_verified').Count
    $bridgeVerified=[regex]::Matches($log,'bridge_cache_verified').Count
    if($VerifyTranslationCache -and ($pixelHits -lt 4 -or $bridgeHits -lt 4 -or $pixelVerified -ne $pixelHits -or $bridgeVerified -ne $bridgeHits)) { throw 'Every reused pixel/bridge translation must match a fresh compilation' }
    @{Scope='Bounded original native Gears with opt-in pinned GS/PS NGG expansion, owned scratch and ordered producer/readback/graphics; not commercial-game compatibility'
        NativeRun=$run.FullName;InputSha256=$nativeReport.InputSha256
        CapturedFrames=$frames.Count;DistinctFrameHashes=@($hashes.Sha256 | Select-Object -Unique).Count
        ConsumedDraws=$consumed;SubmittedFlips=$submittedFlips
        PresentedTickets=$presentTimes.Count
        MinimumPresentIntervalMs=($intervals | Measure-Object -Minimum).Minimum
        MaximumPresentIntervalMs=($intervals | Measure-Object -Maximum).Maximum
        NativePresentedImagesVerified=$true;FrameImagesChange=$true;OwnershipFailureObserved=$false
        RenderingFidelityAutomaticallyVerified=$false
        RuntimeComputeScalars=[bool]$RuntimeScalars
        ExperimentalPayloadPreservingEvents=[bool]$PreserveFlipPayloads
        CaptureEvery=$CaptureEvery
        ComputeTranslationCacheHits=$cacheHits;FreshTranslationComparisons=$verifiedHits
        PixelTranslationCacheHits=$pixelHits;PixelFreshComparisons=$pixelVerified
        BridgeTranslationCacheHits=$bridgeHits;BridgeFreshComparisons=$bridgeVerified
        TerminatedAtDeadline=$nativeReport.TerminatedAtDeadline;ExperimentalModeOnly=$true;Frames=$hashes
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$output/native-ngg-report.json" -Encoding UTF8
    Write-Output "PASS actual native NGG frames change without frame-loop failure: $output"
} finally {
    foreach($name in $variables) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
