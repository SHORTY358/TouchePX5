# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateSet('write-watch','disabled')][string]$TrackingMode='write-watch')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base=Join-Path $root 'artifacts/native-guest-threads'
$build=Get-Content "$base/build-report.json" -Raw | ConvertFrom-Json
$inputElf=Join-Path $base 'threads.elf'
if((Get-FileHash $inputElf).Hash -ne $build.ElfSha256) { throw 'ELF provenance mismatch' }
$exitStyle=if($build.ExitSecondWorker) { 'explicit-exit' } else { 'return' }
$gateStyle=if($build.SemaphoreGate) { 'semaphore' } else { 'atomic' }
$out=Join-Path $base ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+$TrackingMode+'-'+$exitStyle+'-'+$gateStyle)
New-Item -ItemType Directory -Path $out -Force | Out-Null
Copy-Item -LiteralPath $inputElf -Destination "$out/threads.elf"
Copy-Item -LiteralPath "$base/build-report.json" -Destination "$out/build-report.json"
$exe=Join-Path $root 'artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe'
$previous=$env:SHARPEMU_GUEST_IMAGE_CPU_SYNC
$previousSema=$env:SHARPEMU_LOG_SEMA
try {
    $env:SHARPEMU_GUEST_IMAGE_CPU_SYNC=if($TrackingMode -eq 'write-watch') { 'write-watch' } else { '0' }
    if($build.SemaphoreGate) { $env:SHARPEMU_LOG_SEMA='1' }
    $process=Start-Process -FilePath $exe -ArgumentList @('--cpu-engine=native','--generation=ps5',"--log-file=$out/runtime.log",$inputElf) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
    if(-not $process.WaitForExit(20000)) {
        $process.Kill($true)
        throw "Owned native-thread probe timed out; see $out"
    }
    $log=[IO.File]::ReadAllText("$out/stderr.log")
    $success='Guest notification: guest_threads: PASS workers=2 counter=2000 totals=1000,1000 returns=12345678CAFE0001,12345678CAFE0002'
    $passed=$process.ExitCode -eq 0 -and $log.Contains($success)
    $blocks=([regex]::Matches($log,'wait-block [^\r\n]*name=''guest-probe-gate''')).Count
    $wakes=([regex]::Matches($log,'wait-wake [^\r\n]*name=''guest-probe-gate''')).Count
    if($build.SemaphoreGate -and ($blocks -ne 4 -or $wakes -ne 4)) { $passed=$false }
    @{
        Passed=$passed; Scope='two native guest-created workers, atomic shared counter and join return values; no graphics'
        TrackingMode=$TrackingMode; ElfSha256=$build.ElfSha256
        ExitSecondWorker=[bool]$build.ExitSecondWorker
        SemaphoreGate=[bool]$build.SemaphoreGate; SemaphoreBlocks=$blocks; SemaphoreWakes=$wakes
        RuntimeHleSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'SharpEmu.HLE.dll')).Hash
        RuntimeCoreSha256=(Get-FileHash (Join-Path (Split-Path $exe) 'SharpEmu.Core.dll')).Hash
        RuntimeExeSha256=(Get-FileHash $exe).Hash
        HostExit=$process.ExitCode; GuestAssertionsVerified=$passed
        Workers=$(if($passed) { 2 } else { $null })
        SharedCounter=$(if($passed) { 2000 } else { $null })
        WorkerTotals=$(if($passed) { @(1000,1000) } else { $null })
        ReturnValues=$(if($passed) { @('12345678CAFE0001','12345678CAFE0002') } else { $null })
    } | ConvertTo-Json | Set-Content "$out/report.json" -Encoding utf8
    if(-not $passed) {
        throw "Native guest-thread assertions failed (exit $($process.ExitCode)); see $out"
    }
    Write-Output "PASS: native guest-created threads, atomic data and join results. Folder: $out"
} finally {
    $env:SHARPEMU_GUEST_IMAGE_CPU_SYNC=$previous
    $env:SHARPEMU_LOG_SEMA=$previousSema
}
