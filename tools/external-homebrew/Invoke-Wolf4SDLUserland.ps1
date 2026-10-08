# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(1,120)][int]$TimeoutSeconds=20,
      [ValidateRange(1,4096)][int]$TraceImports=32,
      [string]$KeyboardReplay='',
      [ValidatePattern('^[A-Za-z0-9_-]*$')][string]$ConfigSubdirectory='',
      [ValidateRange(0,9)][Nullable[int]]$StartLevel=$null,
      [switch]$InspectSurfaces,
      [switch]$Mouse,
      [ValidateRange(0,105)][int]$SurfaceSampleStartSeconds=10)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $root 'tools\native-probes\RuntimeManifest.ps1')
$inputRoot=Join-Path $root 'artifacts\external-homebrew\wolf4sdl-userland'
$guestConfig='/app0'
$configRoot=$null
if($ConfigSubdirectory.Length -gt 0) {
    $configRoot=Join-Path $inputRoot $ConfigSubdirectory
    New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
    $guestConfig='/temp0'
}
$elf=Join-Path $inputRoot 'wolf3d-shareware-userland.elf'
if($Mouse) { $elf=Join-Path $inputRoot 'wolf3d-shareware-userland-mouse.elf' }
$data=Join-Path $root 'artifacts\external-homebrew\wolf4sdl-v1.0\Wolf4SDL\shareware'
$exe=Join-Path $root 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe'
foreach($path in @($elf,$data,$exe)) { if(-not (Test-Path -LiteralPath $path)) { throw "Missing input: $path" } }
$dataFiles=@(Get-ChildItem -LiteralPath $data -File | Where-Object { $_.Extension -eq '.WL1' })
foreach($file in $dataFiles) { Copy-Item -LiteralPath $file.FullName -Destination $inputRoot -Force }
$output=Join-Path $inputRoot ('runs\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$log=Join-Path $output 'runtime.log'
$guestArguments="--configdir $guestConfig"
if($null -ne $StartLevel) {
    # Public engine options: bypass timed menu input without changing guest code.
    # This runner uses episode-one shareware data, hence levels 0..9 only.
    $guestArguments+=" --tedlevel $StartLevel --nowait --normal"
}
$report=[ordered]@{
    Profile='Explicit PS5 source build with emulator userland entry'
    SourceCommit=(& git -C $root rev-parse HEAD)
    SourceChanges=@(& git -C $root status --porcelain)
    ElfSha256=(Get-FileHash -LiteralPath $elf).Hash
    RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe))
    DataFiles=@($dataFiles | ForEach-Object { [ordered]@{Name=$_.Name; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    GuestArguments=$guestArguments
    StartLevel=$StartLevel
    InspectSurfaces=[bool]$InspectSurfaces
    MouseBridge=[bool]$Mouse
    SurfaceSampleStartSeconds=$SurfaceSampleStartSeconds
    ConfigHostDirectory=$configRoot
    ExplicitGeneration='ps5'
    KeyboardReplay=$KeyboardReplay
    TraceImports=$TraceImports
    DiagnosticEnvironment=[ordered]@{
        LogAllImports=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_ALL_IMPORTS','Process')
        ImportFilter=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_IMPORT_FILTER','Process')
        ImportFrames=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_IMPORT_FRAMES','Process')
        Pthreads=[Environment]::GetEnvironmentVariable('SHARPEMU_LOG_PTHREADS','Process')
        PeriodicSnapshots=[Environment]::GetEnvironmentVariable('SHARPEMU_PERIODIC_SNAPSHOT_SECONDS','Process')
        VideoOutDump=[Environment]::GetEnvironmentVariable('SHARPEMU_DUMP_VIDEOOUT','Process')
        DumpAfterFlip=[Environment]::GetEnvironmentVariable('SHARPEMU_DUMP_VIDEOOUT_AFTER_FLIP','Process')
        DumpEveryFlip=[Environment]::GetEnvironmentVariable('SHARPEMU_DUMP_VIDEOOUT_EVERY_FLIP','Process')
        MaximumFrames=[Environment]::GetEnvironmentVariable('SHARPEMU_DUMP_VIDEOOUT_MAX_FRAMES','Process')
        SwapchainDumpDirectory=[Environment]::GetEnvironmentVariable('SHARPEMU_GUEST_IMAGE_DUMP_DIR','Process')
        SwapchainDumpEvery=[Environment]::GetEnvironmentVariable('SHARPEMU_SWAPCHAIN_DUMP_EVERY','Process')
        SwapchainDumpContinuous=[Environment]::GetEnvironmentVariable('SHARPEMU_GUEST_IMAGE_DUMP_CONTINUOUS','Process')
    }
    StartedUtc=[DateTime]::UtcNow.ToString('o')
    TerminatedAtDeadline=$false
    ExitCode=$null
    GameplayVerified=$false
    CapturedFrames=@()
}
$previousArgs=[Environment]::GetEnvironmentVariable('SHARPEMU_GUEST_ARGS','Process')
$previousReplay=[Environment]::GetEnvironmentVariable('SHARPEMU_TEST_KEYBOARD_REPLAY','Process')
$previousTemp0=[Environment]::GetEnvironmentVariable('SHARPEMU_TEMP0_DIR','Process')
$previousArgv=[Environment]::GetEnvironmentVariable('SHARPEMU_SOURCE_PROFILE_ARGV','Process')
$previousRawDump=[Environment]::GetEnvironmentVariable('SHARPEMU_TILED_RAW_DUMP_DIR','Process')
$previousSourceSlots=[Environment]::GetEnvironmentVariable('SHARPEMU_SOURCE_SURFACE_SLOTS','Process')
$previousMouseRelative=[Environment]::GetEnvironmentVariable('SHARPEMU_MOUSE_RELATIVE','Process')
$process=$null
try {
    if($Mouse) { [Environment]::SetEnvironmentVariable('SHARPEMU_MOUSE_RELATIVE','1','Process') }
    [Environment]::SetEnvironmentVariable('SHARPEMU_GUEST_ARGS',$report.GuestArguments,'Process')
    [Environment]::SetEnvironmentVariable('SHARPEMU_SOURCE_PROFILE_ARGV','1','Process')
    if($InspectSurfaces) {
        [Environment]::SetEnvironmentVariable('SHARPEMU_TILED_RAW_DUMP_DIR',$output,'Process')
        $symbols=& 'C:\BuildTools\VC\Tools\Llvm\x64\bin\llvm-nm.exe' $elf
        if($LASTEXITCODE -ne 0) { throw 'Cannot resolve source surface slots' }
        $slots=@(foreach($name in @('screenBuffer','screen','_ZL7texture','window')) {
            $symbol=@($symbols | Where-Object { $_ -match ('^[0-9a-fA-F]+\s+[bBdD]\s+'+$name+'$') })
            if($symbol.Count -ne 1) { throw "Missing unique source slot: $name" }
            (0x400000UL+[Convert]::ToUInt64(($symbol[0] -split '\s+')[0],16)).ToString('X')
        })
        [Environment]::SetEnvironmentVariable('SHARPEMU_SOURCE_SURFACE_SLOTS',($slots -join ';'),'Process')
    }
    [Environment]::SetEnvironmentVariable('SHARPEMU_TEST_KEYBOARD_REPLAY',$KeyboardReplay,'Process')
    if($null -ne $configRoot) { [Environment]::SetEnvironmentVariable('SHARPEMU_TEMP0_DIR',$configRoot,'Process') }
    $process=Start-Process -FilePath $exe -ArgumentList @('--cpu-engine=native','--generation=ps5',('--trace-imports='+$TraceImports),('--log-file="'+$log+'"'),('"'+$elf+'"')) `
        -WorkingDirectory $inputRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $output 'stdout.log') -RedirectStandardError (Join-Path $output 'stderr.log')
    if($InspectSurfaces) {
        . (Join-Path $PSScriptRoot 'Read-Wolf4SDLSurfaces.ps1')
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $sample=0
        while(-not $process.HasExited -and $clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            if($sample -lt 4 -and $clock.Elapsed.TotalSeconds -ge ($SurfaceSampleStartSeconds+$sample*5)) {
                $sample++
                try { Save-WolfSurfaceSample -Process $process -Output $output -Elf $elf -Sample $sample }
                catch { Write-Warning "Surface sample $sample failed: $_" }
            }
            [void]$process.WaitForExit(250)
        }
        if(-not $process.HasExited) { $report.TerminatedAtDeadline=$true; $process.Kill(); $process.WaitForExit() }
    } elseif(-not $process.WaitForExit($TimeoutSeconds*1000)) { $report.TerminatedAtDeadline=$true; $process.Kill(); $process.WaitForExit() }
    $report.ExitCode=$process.ExitCode
} finally {
    if($null -ne $process) { if(-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    [Environment]::SetEnvironmentVariable('SHARPEMU_GUEST_ARGS',$previousArgs,'Process')
    [Environment]::SetEnvironmentVariable('SHARPEMU_TEST_KEYBOARD_REPLAY',$previousReplay,'Process')
    [Environment]::SetEnvironmentVariable('SHARPEMU_TEMP0_DIR',$previousTemp0,'Process')
    [Environment]::SetEnvironmentVariable('SHARPEMU_SOURCE_PROFILE_ARGV',$previousArgv,'Process')
    [Environment]::SetEnvironmentVariable('SHARPEMU_TILED_RAW_DUMP_DIR',$previousRawDump,'Process')
    [Environment]::SetEnvironmentVariable('SHARPEMU_SOURCE_SURFACE_SLOTS',$previousSourceSlots,'Process')
    [Environment]::SetEnvironmentVariable('SHARPEMU_MOUSE_RELATIVE',$previousMouseRelative,'Process')
    $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
    # Preserve only dumps produced by this launch, never stale files from another run.
    $frameRoot=Join-Path $root 'logs'
    $frames=@(Get-ChildItem -LiteralPath $frameRoot -Filter 'videoout_frame_*.bmp' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge [DateTime]::Parse($report.StartedUtc).ToUniversalTime() })
    if($frames.Count -gt 0) {
        $savedFrames=Join-Path $output 'frames'
        New-Item -ItemType Directory -Path $savedFrames -Force | Out-Null
        $report.CapturedFrames=@(foreach($frame in $frames) {
            Copy-Item -LiteralPath $frame.FullName -Destination $savedFrames
            $metadata=[IO.Path]::ChangeExtension($frame.FullName,'.txt')
            if(Test-Path -LiteralPath $metadata) { Copy-Item -LiteralPath $metadata -Destination $savedFrames }
            [ordered]@{ Name=$frame.Name; Sha256=(Get-FileHash -LiteralPath $frame.FullName).Hash }
        })
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'report.json') -Encoding utf8
}
Write-Output "Userland launch evidence: $output"
