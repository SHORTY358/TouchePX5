# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildRoot,
      [ValidateRange(2,30)][int]$TimeoutSeconds=12,
      [switch]$ExperimentalNgg,[switch]$CaptureInputs,[switch]$ExperimentalBsp,[switch]$PreserveFlipPayloads,[switch]$CaptureFrames,[switch]$AuditNgg,[switch]$PassThru)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$repo/tools/native-probes/RuntimeManifest.ps1"
$build=(Resolve-Path -LiteralPath $BuildRoot).Path
$native=Get-Content "$build/native-report.json" -Raw | ConvertFrom-Json
$source=Join-Path $repo 'artifacts/external-homebrew/ps5-xash3d-halflife'
if(-not $native.NativeRendererBuilt -or $native.GameAssetsUsed -or
   $native.SourcePin -ne (& git -C $source rev-parse HEAD) -or
   (& git -C $source status --porcelain)) { throw 'Expected clean pinned synthetic renderer build' }
$app=$native.App
if(-not $native.AppFiles -or (Get-FileHash "$app/eboot.bin").Hash -ne $native.SelfSha256) { throw 'Missing or changed renderer build manifest' }
foreach($entry in $native.AppFiles) {
    if((Get-FileHash -LiteralPath (Join-Path $app $entry.Path)).Hash -ne $entry.Sha256) { throw "Native app changed: $($entry.Path)" }
}
$exe=Join-Path $repo 'artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe'
$out=Join-Path $repo ('artifacts/xash3d-native-runs/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$saved=@{}
foreach($name in @('TOUCHEPX5_EXPERIMENTAL_GEARS_NGG','SHARPEMU_LOG_AGC','SHARPEMU_LOG_VIDEOOUT',
    'TOUCHEPX5_EXPERIMENTAL_BSP_NGG','TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO','SHARPEMU_TRACE_GUEST_WRITES',
    'TOUCHEPX5_BSP_NATIVE_AUDIT_DIR',
    'SHARPEMU_TRACE_GUEST_WRITE_ORDINAL','SHARPEMU_GUEST_IMAGE_DUMP_DIR','SHARPEMU_OVERLAY',
    'TOUCHEPX5_SHADER_REPLAY_DIR','TOUCHEPX5_SHADER_REPLAY_ADDRESS','TOUCHEPX5_SHADER_REPLAY_DRAW_COUNT','TOUCHEPX5_SHADER_REPLAY_SEQUENCE_LIMIT')) { $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
$process=$null
$report=[ordered]@{
    SourceCommit=(& git -C $repo rev-parse HEAD);SourceChanges=@(& git -C $repo status --porcelain)
    BuildRoot=$build;BuildReportSha256=(Get-FileHash "$build/native-report.json").Hash
    NativeBuild=$native;RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe))
    StartedUtc=[DateTime]::UtcNow.ToString('o');TimeoutSeconds=$TimeoutSeconds
    ExperimentalNgg=[bool]$ExperimentalNgg;NativeRendererInvoked=$false
    TerminatedAtDeadline=$false;StoppedAfterDiagnosticPark=$false;ExitCode=$null;ExecutionResult=$null;BootstrapFailureStage=$null
    CaptureInputs=[bool]$CaptureInputs
    ExperimentalBsp=[bool]$ExperimentalBsp;PreserveFlipPayloads=[bool]$PreserveFlipPayloads
    NativeRenderingVerified=$false;FullEngineExecuted=$false;GameAssetsUsed=$false
    Scope='Bounded diagnostic of original standalone BSP renderer with explicit consumer ABI overlay; not full Xash3D or Half-Life compatibility'
}
try {
    [Environment]::SetEnvironmentVariable('TOUCHEPX5_EXPERIMENTAL_GEARS_NGG',$(if($ExperimentalNgg -or $ExperimentalBsp){'1'}else{$null}),'Process')
    $env:TOUCHEPX5_EXPERIMENTAL_BSP_NGG=$(if($ExperimentalBsp){'1'}else{$null})
    $env:TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO=$(if($PreserveFlipPayloads){'1'}else{$null})
    $env:TOUCHEPX5_BSP_NATIVE_AUDIT_DIR=$(if($AuditNgg){Join-Path $out 'ngg-audit'}else{$null})
    if($CaptureFrames) {
        $env:SHARPEMU_TRACE_GUEST_WRITES='0x10080000'
        $env:SHARPEMU_TRACE_GUEST_WRITE_ORDINAL='1:2'
        $env:SHARPEMU_GUEST_IMAGE_DUMP_DIR=Join-Path $out 'frames'
        $env:SHARPEMU_OVERLAY='0'
    }
    $env:SHARPEMU_LOG_AGC='1';$env:SHARPEMU_LOG_VIDEOOUT='1'
    $env:TOUCHEPX5_SHADER_REPLAY_DIR=$(if($CaptureInputs){Join-Path $out 'replay'}else{$null})
    $env:TOUCHEPX5_SHADER_REPLAY_SEQUENCE_LIMIT='2'
    [Environment]::SetEnvironmentVariable('TOUCHEPX5_SHADER_REPLAY_ADDRESS',$null,'Process')
    [Environment]::SetEnvironmentVariable('TOUCHEPX5_SHADER_REPLAY_DRAW_COUNT',$null,'Process')
    $report.Environment=@(Get-ChildItem Env: | Where-Object Name -Match '^(TOUCHEPX5_|SHARPEMU_)' | ForEach-Object { @{Name=$_.Name;Value=$_.Value} })
    $arguments=@('--generation=ps5','--cpu-engine=native','--fps-limit=60','--log-level=debug','--trace-imports=64',('--log-file="'+$out+'/runtime.log"'),('"'+$app+'/eboot.bin"'))
    $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $app -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
    $report.NativeRendererInvoked=$true;$report.LauncherProcessId=$process.Id
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while(-not $process.WaitForExit(250) -and $clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if((Test-Path "$out/stderr.log") -and (Get-Content "$out/stderr.log" -Raw) -match 'XASH_RENDERER_PARK reason=') {
            $report.StoppedAfterDiagnosticPark=$true;break
        }
    }
    if(-not $process.HasExited) {
        $report.TerminatedAtDeadline=-not $report.StoppedAfterDiagnosticPark
        # Only the process tree created by this invocation; never kill by name.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        $process.WaitForExit(5000) | Out-Null
    }
    if($process.HasExited) { $report.ExitCode=$process.ExitCode }
} catch {
    $report.RunnerError=$_.Exception.Message
    throw
} finally {
    if($null -ne $process -and -not $process.HasExited) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        $process.WaitForExit(5000) | Out-Null
    }
    # Start-Process closes redirected stream handles asynchronously after exit.
    # Wait for that bounded flush before taking the final evidence hashes.
    $files=@(Get-ChildItem -LiteralPath $out -File -Recurse)
    for($attempt=0;$attempt -lt 20;$attempt++) {
        try {
            $evidence=@($files | ForEach-Object { $_.Refresh();@{Path=[IO.Path]::GetRelativePath($out,$_.FullName);Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -ErrorAction Stop).Hash} })
            break
        } catch { if($attempt -eq 19){$report.EvidenceHashError=$_.Exception.Message};Start-Sleep -Milliseconds 100 }
    }
    foreach($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') }
    $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
    if(Test-Path "$out/runtime.log") {
        $runtimeText=Get-Content "$out/runtime.log" -Raw
        if($runtimeText -match 'execution completed\. Result=(\w+)') { $report.ExecutionResult=$Matches[1] }
    }
    if(Test-Path "$out/stderr.log") {
        $stderrText=Get-Content "$out/stderr.log" -Raw
        if($stderrText -match 'XASH_RENDERER_BOOT_FAILURE step=([^\s]+) result=(-?\d+)') { $report.BootstrapFailureStage=$Matches[1];$report.BootstrapFailureCode=$Matches[2] }
        if($stderrText -match 'XASH_RENDERER_PARK reason=([^\s]+)') { $report.ParkReason=$Matches[1] }
        if($stderrText -match 'XASH_RENDERER_LOOP_FAILURE result=(-?\d+) state=(-?\d+) callback=(-?\d+) frame=(\d+) completed=(\d+)') {
            $report.LoopFailure=@{Result=$Matches[1];State=$Matches[2];Callback=$Matches[3];Frame=$Matches[4];Completed=$Matches[5]}
        }
    }
    $report.EvidenceFiles=$evidence
    $report | ConvertTo-Json -Depth 12 | Set-Content "$out/execution-report.json" -Encoding utf8
    if($null -ne $process) { $process.Dispose() }
}
Write-Output "Native BSP renderer diagnostic evidence: $out"
if($PassThru){[pscustomobject]@{NativeRun=$out;ReportPath=(Join-Path $out 'execution-report.json')}}
