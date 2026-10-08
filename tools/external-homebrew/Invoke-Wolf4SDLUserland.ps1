# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(1,60)][int]$TimeoutSeconds=20,
      [ValidateRange(1,4096)][int]$TraceImports=32)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $root 'tools\native-probes\RuntimeManifest.ps1')
$inputRoot=Join-Path $root 'artifacts\external-homebrew\wolf4sdl-userland'
$elf=Join-Path $inputRoot 'wolf3d-shareware-userland.elf'
$data=Join-Path $root 'artifacts\external-homebrew\wolf4sdl-v1.0\Wolf4SDL\shareware'
$exe=Join-Path $root 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe'
foreach($path in @($elf,$data,$exe)) { if(-not (Test-Path -LiteralPath $path)) { throw "Missing input: $path" } }
$dataFiles=@(Get-ChildItem -LiteralPath $data -File | Where-Object { $_.Extension -eq '.WL1' })
foreach($file in $dataFiles) { Copy-Item -LiteralPath $file.FullName -Destination $inputRoot -Force }
$output=Join-Path $inputRoot ('runs\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$log=Join-Path $output 'runtime.log'
$report=[ordered]@{
    Profile='Explicit PS5 source build with emulator userland entry'
    SourceCommit=(& git -C $root rev-parse HEAD)
    SourceChanges=@(& git -C $root status --porcelain)
    ElfSha256=(Get-FileHash -LiteralPath $elf).Hash
    RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe))
    DataFiles=@($dataFiles | ForEach-Object { [ordered]@{Name=$_.Name; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    GuestArguments='--configdir /app0'
    ExplicitGeneration='ps5'
    TraceImports=$TraceImports
    DiagnosticEnvironment=[ordered]@{
        LogAllImports=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_ALL_IMPORTS','Process')
        ImportFilter=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_IMPORT_FILTER','Process')
        ImportFrames=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_IMPORT_FRAMES','Process')
        Pthreads=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_PTHREADS','Process')
        PeriodicSnapshots=[Environment]::GetEnvironmentVariable('SHARPEMU_PERIODIC_SNAPSHOT_SECONDS','Process')
        VideoOutDump=[Environment]::GetEnvironmentVariable('SHARPEMU_DUMP_VIDEOOUT','Process')
    }
    StartedUtc=[DateTime]::UtcNow.ToString('o')
    TerminatedAtDeadline=$false
    ExitCode=$null
    GameplayVerified=$false
}
$previousArgs=[Environment]::GetEnvironmentVariable('SHARPEMU_GUEST_ARGS','Process')
$process=$null
try {
    [Environment]::SetEnvironmentVariable('SHARPEMU_GUEST_ARGS',$report.GuestArguments,'Process')
    $process=Start-Process -FilePath $exe -ArgumentList @('--cpu-engine=native','--generation=ps5',('--trace-imports='+$TraceImports),('--log-file="'+$log+'"'),('"'+$elf+'"')) `
        -WorkingDirectory $inputRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $output 'stdout.log') -RedirectStandardError (Join-Path $output 'stderr.log')
    if(-not $process.WaitForExit($TimeoutSeconds*1000)) { $report.TerminatedAtDeadline=$true; $process.Kill(); $process.WaitForExit() }
    $report.ExitCode=$process.ExitCode
} finally {
    if($null -ne $process) { if(-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    [Environment]::SetEnvironmentVariable('SHARPEMU_GUEST_ARGS',$previousArgs,'Process')
    $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'report.json') -Encoding utf8
}
Write-Output "Userland launch evidence: $output"
