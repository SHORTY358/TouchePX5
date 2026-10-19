# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(1,600)][int]$TimeoutSeconds=20,[switch]$PublicInitAbi,[switch]$BootDiagnostics,[switch]$LegacyDefaultsAbi,[switch]$ShaderAperture,
 [ValidateSet(0,250,500,1000,2000,5000)][int]$ResourceSampleIntervalMilliseconds=0,[switch]$PassThru)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$repo/tools/native-probes/RuntimeManifest.ps1"
$deps=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain'
if($BootDiagnostics -and -not $PublicInitAbi) { throw 'Select public-init ABI for the diagnostic variant' }
if($LegacyDefaultsAbi -and (-not $PublicInitAbi -or -not $BootDiagnostics)) { throw 'Select public-init and diagnostics for the legacy defaults experiment' }
if($ShaderAperture -and -not $LegacyDefaultsAbi) { throw 'Select legacy defaults for the shader aperture experiment' }
$variant=if($ShaderAperture) { 'gears-shader-aperture-windows' } elseif($LegacyDefaultsAbi) { 'gears-legacy-defaults-windows' } elseif($BootDiagnostics) { 'gears-public-init-diagnostics' } elseif($PublicInitAbi) { 'gears-public-init-windows' } else { 'gears-native-windows' }
$native=Get-Content "$deps/$variant/native-report.json" -Raw | ConvertFrom-Json
$hostChecks=Get-Content "$deps/cygwin-host/host-verification-report.json" -Raw | ConvertFrom-Json
if(-not $hostChecks.TestCompleted -or -not $hostChecks.AuditCompleted) { throw 'Complete unchanged upstream host tests and audit first' }
$gears=Join-Path $repo 'artifacts/external-homebrew/ps5-agc-gears'
if($hostChecks.GearsPin -ne (& git -C $gears rev-parse HEAD) -or
   $hostChecks.MakefileSha256 -ne (Get-FileHash "$gears/Makefile").Hash -or
   (& git -C $gears status --porcelain)) { throw 'Host verification no longer matches the clean upstream checkout' }
if(-not $native.NativeTitleBuilt) { throw 'Actual compiled Gears title required' }
$app=$native.App
$inputFile=Join-Path $app 'eboot.bin'
if((Get-FileHash $inputFile).Hash -ne $native.SelfSha256) { throw 'Native input differs from the retained build report' }
$exe=Join-Path $repo 'artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe'
$out=Join-Path $deps ($variant+'/runs/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
Copy-Item -LiteralPath "$deps/$variant/native-report.json" -Destination "$out/native-report.json"
$log=Join-Path $out 'runtime.log'
$report=[ordered]@{SourceCommit=(& git -C $repo rev-parse HEAD); SourceChanges=@(& git -C $repo status --porcelain);
 InputSha256=$native.SelfSha256; BuildVariant=$variant; AppFiles=@(Get-ChildItem $app -Recurse -File | ForEach-Object { @{Path=[IO.Path]::GetRelativePath($app,$_.FullName);Sha256=(Get-FileHash $_.FullName).Hash} });
 RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe)); HostVerification=$hostChecks;
 StartedUtc=[DateTime]::UtcNow.ToString('o'); TimeoutSeconds=$TimeoutSeconds;
 TerminatedAtDeadline=$false; ExitCode=$null; ExecutionResult=$null; BootstrapFailureStage=$null; ParkReason=$null; RenderingVerified=$false;
 TraceVideoOut=$env:SHARPEMU_LOG_VIDEOOUT; TraceAgc=$env:SHARPEMU_LOG_AGC;
 Scope='Bounded bootstrap investigation; window creation, process exit and build success are not rendering evidence'}
$process=$null
$resourceSamples=[Collections.Generic.List[object]]::new()
$resourceClock=[Diagnostics.Stopwatch]::new()
$resourceWorker=$null
function Add-ResourceSample {
 $process.Refresh()
 if($process.HasExited) { return }
 # The Windows CLI is a small launcher. Attribute emulator allocations to its
 # mitigated worker, identified by both ancestry and the internal CLI flag.
 if($null -eq $script:resourceWorker) {
  $children=@(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)" |
   Where-Object { $_.Name -eq [IO.Path]::GetFileName($exe) -and $_.CommandLine -match '--sharpemu-mitigated-child(?:\s|$)' })
  if($children.Count -gt 1) { throw 'Ambiguous mitigated emulator worker' }
  if($children.Count -eq 0) { return }
  $script:resourceWorker=[Diagnostics.Process]::GetProcessById([int]$children[0].ProcessId)
 }
 $script:resourceWorker.Refresh()
 if($script:resourceWorker.HasExited) { return }
 $resourceSamples.Add([ordered]@{
  ElapsedSeconds=$resourceClock.Elapsed.TotalSeconds
  ProcessId=$script:resourceWorker.Id;Role='MitigatedEmulatorChild';LauncherProcessId=$process.Id
  WorkingSetBytes=$script:resourceWorker.WorkingSet64; PrivateBytes=$script:resourceWorker.PrivateMemorySize64
  Handles=$script:resourceWorker.HandleCount; Threads=$script:resourceWorker.Threads.Count
  CpuSeconds=$script:resourceWorker.TotalProcessorTime.TotalSeconds
  LauncherWorkingSetBytes=$process.WorkingSet64;LauncherPrivateBytes=$process.PrivateMemorySize64
 })
}
try {
 $arguments=@('--generation=ps5','--cpu-engine=native','--fps-limit=60','--log-level=debug','--trace-imports=64',('--log-file="'+$log+'"'),('"'+$inputFile+'"'))
 $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $app -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
 $resourceClock.Start()
 if($ResourceSampleIntervalMilliseconds -eq 0) {
  $exited=$process.WaitForExit($TimeoutSeconds*1000)
 } else {
  while(-not $process.HasExited -and $resourceClock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
   Add-ResourceSample
   $remaining=[Math]::Max(1,[Math]::Ceiling(($TimeoutSeconds-$resourceClock.Elapsed.TotalSeconds)*1000))
   if($process.WaitForExit([int][Math]::Min($ResourceSampleIntervalMilliseconds,$remaining))) { break }
  }
  Add-ResourceSample
  $exited=$process.HasExited
 }
 if(-not $exited) {
  $report.TerminatedAtDeadline=$true
  & taskkill.exe /PID $process.Id /T /F | Out-Null
  $process.WaitForExit(5000) | Out-Null
 }
 if($process.HasExited) { $report.ExitCode=$process.ExitCode }
} finally {
 if($null -ne $process -and -not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
 $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
 $report.ResourceSamplingIntervalMilliseconds=$ResourceSampleIntervalMilliseconds
 $report.ResourceSamples=$resourceSamples.ToArray()
 $report.ObservedProcessSeconds=$resourceClock.Elapsed.TotalSeconds
 if(Test-Path $log) {
  $runtimeText=Get-Content $log -Raw
  if($runtimeText -match 'execution completed\. Result=(\w+)') { $report.ExecutionResult=$Matches[1] }
 }
 if(Test-Path "$out/stderr.log") {
  $stderrText=Get-Content "$out/stderr.log" -Raw
  if($stderrText -match 'GEARS_PRE_SUBMIT_FAILURE step=(\w+)') { $report.BootstrapFailureStage=$Matches[1] }
  if($stderrText -match 'GEARS_PARKED reason=([\w-]+)') { $report.ParkReason=$Matches[1] }
 }
 $report | ConvertTo-Json -Depth 8 | Set-Content "$out/report.json" -Encoding UTF8
 if($null -ne $process) { $process.Dispose() }
 if($null -ne $resourceWorker) { $resourceWorker.Dispose() }
}
Write-Output "Gears boot evidence: $out"
if($PassThru) { [pscustomobject]@{NativeRun=$out;ReportPath=(Join-Path $out 'report.json')} }
