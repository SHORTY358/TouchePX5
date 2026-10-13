# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(1,60)][int]$TimeoutSeconds=20,[switch]$PublicInitAbi,[switch]$BootDiagnostics)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$repo/tools/native-probes/RuntimeManifest.ps1"
$deps=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain'
if($BootDiagnostics -and -not $PublicInitAbi) { throw 'Select public-init ABI for the diagnostic variant' }
$variant=if($BootDiagnostics) { 'gears-public-init-diagnostics' } elseif($PublicInitAbi) { 'gears-public-init-windows' } else { 'gears-native-windows' }
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
$log=Join-Path $out 'runtime.log'
$report=[ordered]@{SourceCommit=(& git -C $repo rev-parse HEAD); SourceChanges=@(& git -C $repo status --porcelain);
 InputSha256=$native.SelfSha256; BuildVariant=$variant; AppFiles=@(Get-ChildItem $app -Recurse -File | ForEach-Object { @{Path=[IO.Path]::GetRelativePath($app,$_.FullName);Sha256=(Get-FileHash $_.FullName).Hash} });
 RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe)); HostVerification=$hostChecks;
 StartedUtc=[DateTime]::UtcNow.ToString('o'); TimeoutSeconds=$TimeoutSeconds;
 TerminatedAtDeadline=$false; ExitCode=$null; ExecutionResult=$null; BootstrapFailureStage=$null; RenderingVerified=$false;
 TraceVideoOut=$env:SHARPEMU_LOG_VIDEOOUT; TraceAgc=$env:SHARPEMU_LOG_AGC;
 Scope='Bounded bootstrap investigation; window creation, process exit and build success are not rendering evidence'}
$process=$null
try {
 $arguments=@('--generation=ps5','--cpu-engine=native','--fps-limit=60','--log-level=debug','--trace-imports=64',('--log-file="'+$log+'"'),('"'+$inputFile+'"'))
 $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $app -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
 if(-not $process.WaitForExit($TimeoutSeconds*1000)) {
  $report.TerminatedAtDeadline=$true
  & taskkill.exe /PID $process.Id /T /F | Out-Null
  $process.WaitForExit(5000) | Out-Null
 }
 if($process.HasExited) { $report.ExitCode=$process.ExitCode }
} finally {
 if($null -ne $process -and -not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
 $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
 if(Test-Path $log) {
  $runtimeText=Get-Content $log -Raw
  if($runtimeText -match 'execution completed\. Result=(\w+)') { $report.ExecutionResult=$Matches[1] }
 }
 if(Test-Path "$out/stderr.log") {
  $stderrText=Get-Content "$out/stderr.log" -Raw
  if($stderrText -match 'GEARS_PRE_SUBMIT_FAILURE step=(\w+)') { $report.BootstrapFailureStage=$Matches[1] }
 }
 $report | ConvertTo-Json -Depth 8 | Set-Content "$out/report.json" -Encoding UTF8
 if($null -ne $process) { $process.Dispose() }
}
Write-Output "Gears boot evidence: $out"
