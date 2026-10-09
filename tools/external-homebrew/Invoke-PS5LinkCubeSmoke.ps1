# Copyright (C) 2026 Touché PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$capture=Join-Path $out "smoke-$stamp"
$exe=Join-Path $root 'artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe'
$elf=Join-Path $out 'app-init-abi.elf'
if(-not (Test-Path -LiteralPath $elf)) { throw 'Build-PS5LinkCube.ps1 -CorrectInitAbi must run first' }
New-Item -ItemType Directory -Path $capture -Force | Out-Null
# This profile needs direct CPU framebuffer-write tracking. Windows leaves
# it off by default because it can regress other titles; do not set globally.
$settings=@{
    SHARPEMU_GUEST_IMAGE_CPU_SYNC='1'
    SHARPEMU_TRACE_GUEST_IMAGES='present'
    SHARPEMU_GUEST_IMAGE_DUMP_DIR=$capture
    SHARPEMU_SWAPCHAIN_DUMP_EVERY='150'
}
$previous=@{}
try {
    foreach($key in $settings.Keys) {
        $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$settings[$key],'Process')
    }
    $p=Start-Process -FilePath $exe -ArgumentList @('--cpu-engine=native','--generation=ps5',"--log-file=$capture/runtime.log",$elf) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$capture/stdout.log" -RedirectStandardError "$capture/stderr.log"
    if(-not $p.WaitForExit(25000)) {
        $p.Kill($true)
        throw "Owned cube probe timed out (PID $($p.Id))"
    }
    if($p.ExitCode -ne 0) { throw "Cube host exit $($p.ExitCode); see $capture/stderr.log" }
    $log=[IO.File]::ReadAllText("$capture/stderr.log")
    if(-not $log.Contains('Guest notification: mesh_test: fin')) { throw 'Cube did not finish its 600-frame source loop' }
    $frames=@(Get-ChildItem -LiteralPath $capture -Filter '*.bgra')
    $hashes=@($frames | Get-FileHash | Select-Object -ExpandProperty Hash -Unique)
    if($hashes.Count -lt 2) { throw 'Expected at least two distinct presented frame captures' }
    @{
        Input=$elf; InputSha256=(Get-FileHash -LiteralPath $elf).Hash
        InputHasInitAbiCorrection=$true; StartupModified=$false
        CpuFramebufferTracking=$true; HostExit=$p.ExitCode
        SourceLoopCompleted=$true; PresentedCaptureCount=$frames.Count
        DistinctCaptureCount=$hashes.Count
        VisualCorrectnessAutomaticallyVerified=$false
    } | ConvertTo-Json | Set-Content -LiteralPath "$capture/report.json" -Encoding utf8
    Write-Output "PASS: corrected-input cube completed; $($frames.Count) captured frames ($($hashes.Count) distinct). Capture folder: $capture"
} finally {
    foreach($key in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')
    }
}
