# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$ExpectFailure,[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$PSScriptRoot/RuntimeManifest.ps1"
if(-not $SkipBuild) { & "$PSScriptRoot/Build-SincosfProbe.ps1" -CameraMath }
$app=Join-Path $repo 'artifacts/native-camera-math/app'
$build=Get-Content "$repo/artifacts/native-camera-math/build-report.json" -Raw|ConvertFrom-Json
if((Get-FileHash "$app/eboot.bin").Hash -ne $build.SelfSha256 -or (Get-FileHash "$app/sce_module/libc.prx").Hash -ne $build.CompanionSha256) { throw 'Camera math native input changed' }
$out=Join-Path $repo ('artifacts/native-camera-math/runs/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe=Join-Path $repo 'artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe'
$process=$null
$report=@{InputSha256=$build.SelfSha256;CompanionSha256=$build.CompanionSha256;RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe));ExpectedFailure=[bool]$ExpectFailure}
try {
    $process=Start-Process $exe -ArgumentList @('--generation=ps5','--cpu-engine=native',('--log-file="'+$out+'/runtime.log"'),('"'+$app+'/eboot.bin"')) -WorkingDirectory $app -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
    if(-not $process.WaitForExit(15000)) { & taskkill.exe /PID $process.Id /T /F | Out-Null;throw 'Native camera math deadline' }
    $report.ExitCode=$process.ExitCode
    $text=Get-Content "$out/stderr.log" -Raw
    $report.CasesPassed=$text.Contains('CAMERA_MATH_NATIVE_PASS cases=4')
    $report.FailureObserved=$text.Contains('CAMERA_MATH_NATIVE_FAIL case=1')
    if($ExpectFailure) { if(-not $report.FailureObserved -or $report.CasesPassed){throw 'Expected native camera math failure not observed'} }
    elseif(-not $report.CasesPassed -or $text.Contains('CAMERA_MATH_NATIVE_FAIL')) {throw 'Native camera math failed'}
} finally {
    if($null -ne $process -and -not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
    $report | ConvertTo-Json -Depth 6 | Set-Content "$out/report.json" -Encoding utf8
    if($null -ne $process){$process.Dispose()}
}
Write-Output "Camera math native evidence: $out"
