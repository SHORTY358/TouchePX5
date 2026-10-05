# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(1,60)][int]$TimeoutSeconds = 20)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $root 'tools\native-probes\RuntimeManifest.ps1')
$inputRoot = Join-Path $root 'artifacts\external-homebrew\wolf4sdl-v1.0'
$elf = Join-Path $inputRoot 'Wolf4SDL\payloads\wolf3d-shareware.elf'
$exe = Join-Path $root 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe'
$data = Join-Path $inputRoot 'Wolf4SDL\shareware'
foreach ($path in @($elf,$exe,$data)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing input: $path" }
}
$output = Join-Path $inputRoot ('runs\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$log = Join-Path $output 'runtime.log'
$report = [ordered]@{
    SourceCommit = (& git -C $root rev-parse HEAD)
    SourceChanges = @(& git -C $root status --porcelain)
    ElfSha256 = (Get-FileHash -LiteralPath $elf).Hash
    ExecutableSha256 = (Get-FileHash -LiteralPath $exe).Hash
    RuntimeFiles = @(Get-ToucheRuntimeManifest -Directory (Split-Path $exe))
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    TimeoutSeconds = $TimeoutSeconds
    TerminatedAtDeadline = $false
    ExitCode = $null
    ResolverFailureObserved = $false
    UnsupportedSyscall = $null
    ExecutionResult = $null
    GameplayVerified = $false
    Note = 'Boot investigation only. No guest --datadir/--configdir arguments supplied yet. Exit is not a success verdict.'
}
$process = $null
try {
    $arguments = @('--cpu-engine=native','--fps-limit=60','--log-level=info',
        '--trace-imports=32', ('--log-file="' + $log + '"'), ('"' + $elf + '"'))
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $data `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'stdout.log') `
        -RedirectStandardError (Join-Path $output 'stderr.log')
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $report.TerminatedAtDeadline = $true
        $process.Kill()
        $process.WaitForExit()
    }
    $report.ExitCode = $process.ExitCode
} finally {
    if ($null -ne $process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('o')
    if (Test-Path -LiteralPath $log) {
        $runtimeText = Get-Content -LiteralPath $log -Raw
        $report.ResolverFailureObserved = $runtimeText.Contains("bootstrap_bridge unresolved: handle=0x1 symbol='sceKernelDlsym'")
        if ($runtimeText -match 'Unsupported payload syscall (0x[0-9A-F]+)') { $report.UnsupportedSyscall = $Matches[1] }
        if ($runtimeText -match 'execution completed\. Result=(\w+)') { $report.ExecutionResult = $Matches[1] }
    }
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'report.json') -Encoding utf8
    if ($null -ne $process) { $process.Dispose() }
}
Write-Output "Boot evidence: $output"
