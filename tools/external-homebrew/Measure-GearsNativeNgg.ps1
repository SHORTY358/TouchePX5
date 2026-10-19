# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param([ValidateRange(10,600)][int]$TimeoutSeconds=30,[ValidateRange(0,30)][int]$WarmupSeconds=3,[switch]$PreserveFlipPayloads,[switch]$SampleResources)
$ErrorActionPreference='Stop'
if($WarmupSeconds -ge $TimeoutSeconds-3) { throw 'Leave at least three seconds after warmup' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$variables=@('TOUCHEPX5_EXPERIMENTAL_GEARS_NGG','TOUCHEPX5_GEARS_RUNTIME_SCALARS',
    'TOUCHEPX5_GEARS_VERIFY_TRANSLATION_CACHE','SHARPEMU_LOG_VIDEOOUT','SHARPEMU_TRACE_GUEST_IMAGES',
    'SHARPEMU_GUEST_IMAGE_DUMP_DIR','SHARPEMU_SWAPCHAIN_DUMP_EVERY','SHARPEMU_SLOWDOWN_DIAGNOSTICS_SECONDS',
    'TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO','TOUCHEPX5_GEARS_NATIVE_AUDIT_DIR','TOUCHEPX5_GEARS_NATIVE_AUDIT_FRAME')
$saved=@{}
foreach($name in $variables) { $saved[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    foreach($name in $variables) { [Environment]::SetEnvironmentVariable($name,$null) }
    $env:TOUCHEPX5_EXPERIMENTAL_GEARS_NGG='1'
    $env:TOUCHEPX5_GEARS_RUNTIME_SCALARS='1'
    $env:SHARPEMU_LOG_VIDEOOUT='1'
    $env:TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO=if($PreserveFlipPayloads) { '1' } else { '0' }
    $sampleInterval=if($SampleResources) { 1000 } else { 0 }
    $invocation=@(& "$PSScriptRoot/Invoke-GearsNative.ps1" -PublicInitAbi -BootDiagnostics -LegacyDefaultsAbi -ShaderAperture -TimeoutSeconds $TimeoutSeconds -ResourceSampleIntervalMilliseconds $sampleInterval -PassThru)
    $identity=@($invocation | Where-Object { $_ -is [pscustomobject] -and $_.ReportPath })
    if($identity.Count -ne 1) { throw 'Expected one exact native invocation identity' }
    $run=Get-Item -LiteralPath $identity[0].NativeRun
    $native=Get-Content -LiteralPath $identity[0].ReportPath -Raw | ConvertFrom-Json
    if(-not $native.TerminatedAtDeadline -and $native.ExitCode -ne 0) { throw "Native process failed before its deadline: $($run.FullName)" }
    $log=Get-Content -LiteralPath "$($run.FullName)/stderr.log" -Raw
    if($log -match 'GEARS_EVENT_FAILURE|GEARS_LOOP_FAILURE|GEARS_PARKED|experimental_gears_ngg (consume|schedule) failed|experimental_flip_fifo delivery rejected|unsupported graphics workgroup barrier|ORBIS_GEN2_ERROR_CPU_TRAP|vk.device_lost') { throw "Native frame loop failed: $($run.FullName)" }
    if($log -match 'vk.swapchain_dump|vk.swapchain_image') { throw 'Timing run unexpectedly read back a diagnostic screenshot' }
    $ticks=@([regex]::Matches($log,'videoout.resolve_flip[^\r\n]*present_ticks=(\d+)') | ForEach-Object { [long]$_.Groups[1].Value })
    if($ticks.Count -lt 16) { throw 'Insufficient actual presentation completions' }
    $frequency=[Diagnostics.Stopwatch]::Frequency
    $start=$ticks[0]+$WarmupSeconds*$frequency
    $measured=@($ticks | Where-Object { $_ -ge $start })
    if($measured.Count -lt 16) { throw 'Insufficient post-warmup completions' }
    $intervals=@(for($i=1;$i -lt $measured.Count;$i++) {
        if($measured[$i] -le $measured[$i-1]) { throw 'Non-monotonic presentation clock' }
        ($measured[$i]-$measured[$i-1])*1000.0/$frequency
    })
    $ordered=@($intervals | Sort-Object)
    function Percentile([double]$p) { $ordered[[Math]::Max(0,[Math]::Ceiling($ordered.Count*$p)-1)] }
    $seconds=($measured[-1]-$measured[0])/[double]$frequency
    $resourceSummary=$null
    if($SampleResources) {
        $samples=@($native.ResourceSamples | Where-Object { $_.ElapsedSeconds -ge $WarmupSeconds })
        if($samples.Count -lt 5) { throw 'Insufficient post-warmup process-resource samples' }
        if(@($samples | Where-Object { $_.Role -ne 'MitigatedEmulatorChild' }).Count -ne 0 -or
           @($samples.ProcessId | Select-Object -Unique).Count -ne 1) { throw 'Resource samples must belong to one actual mitigated emulator worker' }
        if($native.ObservedProcessSeconds -lt $TimeoutSeconds-1) { throw 'Native process ended before the resource-observation deadline' }
        # Compare medians of disjoint windows, not a noisy single start/end sample.
        $window=[Math]::Min(20,[Math]::Floor(($samples[-1].ElapsedSeconds-$samples[0].ElapsedSeconds)/3))
        $early=@($samples | Where-Object { $_.ElapsedSeconds -le $samples[0].ElapsedSeconds+$window })
        $late=@($samples | Where-Object { $_.ElapsedSeconds -ge $samples[-1].ElapsedSeconds-$window })
        function MedianProperty($items,[string]$property) {
            $values=@($items | ForEach-Object { [double]$_.$property } | Sort-Object)
            $middle=[int][Math]::Floor($values.Count/2)
            if($values.Count%2) { return $values[$middle] }
            return ($values[$middle-1]+$values[$middle])/2
        }
        $firstMedian=MedianProperty $early 'PrivateBytes'
        $lastMedian=MedianProperty $late 'PrivateBytes'
        $firstTime=MedianProperty $early 'ElapsedSeconds'
        $lastTime=MedianProperty $late 'ElapsedSeconds'
        $resourceSummary=[ordered]@{
            Scope='One-second actual mitigated emulator-worker snapshots; launcher excluded; not GPU-local memory or proof of absence/presence of a leak'
            ProcessId=$samples[0].ProcessId;LauncherProcessId=$samples[0].LauncherProcessId
            SampleCount=$samples.Count;WindowSeconds=$window
            FirstWindowPrivateMedianBytes=$firstMedian;LastWindowPrivateMedianBytes=$lastMedian
            PrivateMedianDeltaBytes=$lastMedian-$firstMedian
            PrivateMedianGrowthBytesPerSecond=($lastMedian-$firstMedian)/($lastTime-$firstTime)
            MaximumPrivateBytes=($samples.PrivateBytes | Measure-Object -Maximum).Maximum
            MaximumWorkingSetBytes=($samples.WorkingSetBytes | Measure-Object -Maximum).Maximum
            FirstWindowHandlesMedian=(MedianProperty $early 'Handles');LastWindowHandlesMedian=(MedianProperty $late 'Handles')
            MaximumThreads=($samples.Threads | Measure-Object -Maximum).Maximum
            CpuSeconds=$samples[-1].CpuSeconds-$samples[0].CpuSeconds
            ObservedProcessSeconds=$native.ObservedProcessSeconds
        }
    }
    $output=Join-Path $repo ('artifacts/gears-native-timing/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    @{Scope='Capture-free host presentation-completion timing for pinned opt-in native Gears; not GPU timestamps, image fidelity, commercial compatibility or a calibrated benchmark'
        SourceCommit=$native.SourceCommit;NativeRun=$run.FullName;InputSha256=$native.InputSha256
        RuntimeFiles=$native.RuntimeFiles;WarmupSeconds=$WarmupSeconds;ClockFrequency=$frequency
        TotalPresentedTickets=$ticks.Count;MeasuredPresentedTickets=$measured.Count
        MeasuredSeconds=$seconds;MeanPresentationRate=($measured.Count-1)/$seconds
        MinimumIntervalMs=$ordered[0];MedianIntervalMs=(Percentile .5)
        P95IntervalMs=(Percentile .95);P99IntervalMs=(Percentile .99);MaximumIntervalMs=$ordered[-1]
        DiagnosticImageReadbackObserved=$false;ImageFidelityVerified=$false
        ExperimentalPayloadPreservingEvents=[bool]$PreserveFlipPayloads
        TerminatedAtDeadline=$native.TerminatedAtDeadline;NativeLoopFailureObserved=$false
        NativeStderrSha256=(Get-FileHash -LiteralPath "$($run.FullName)/stderr.log").Hash
        ProcessResources=$resourceSummary
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/timing-report.json" -Encoding UTF8
    Write-Output "PASS bounded capture-free presentation timing: $output"
} finally {
    foreach($name in $variables) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
