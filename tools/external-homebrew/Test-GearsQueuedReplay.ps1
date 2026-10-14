# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param(
    [Parameter(Mandatory)][string]$CaptureDirectory,
    [Parameter(Mandatory)][string]$ComputeDirectory,
    [string]$Dotnet = 'C:\Dev\dotnet10\dotnet.exe'
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$capture=(Resolve-Path -LiteralPath $CaptureDirectory).Path
$compute=(Resolve-Path -LiteralPath $ComputeDirectory).Path
$output=Join-Path $repo ('artifacts/gears-queued-compute/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $repo
try {
    $cases=@()
    foreach ($name in @('positive','partial-limit')) {
        $directory=Join-Path $output $name
        $arguments=@('run','--project','tools/SharpEmu.Tools.GearsComputeAudit','-c','Release','--',
            '--queued-compute',"$capture/0000000200001000.replay.json",$compute,$directory)
        if ($name -eq 'partial-limit') { $arguments+='--partial-limit' }
        & $Dotnet @arguments > "$output/$name.log" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Queued compute audit failed: $name" }
        $report=Get-Content -LiteralPath "$directory/queued-report.json" -Raw | ConvertFrom-Json
        if ($report.Mismatches -ne 0 -or !$report.GpuCompletedAndReadBack -or
            $report.GuestOutputWriteCalls -eq 0 -or $report.ReadbackSequence -le $report.DispatchSequence) {
            throw "Invalid producer/readback evidence: $name"
        }
        $cases+=@{Name=$name;Report=$report}
    }
    $graphics=Join-Path $output 'graphics'
    & $Dotnet run --project tools/SharpEmu.Tools.GearsComputeAudit -c Release --no-build -- --graphics $capture "$output/positive" $graphics > "$output/graphics.log" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Queued-output graphics consumption failed' }
    $image=Get-ChildItem -LiteralPath $graphics -Filter 'present-*.bgra' | Select-Object -First 1
    & $Dotnet run --project tools/SharpEmu.Tools.GearsComputeAudit -c Release --no-build -- --lighting-reference "$output/positive" $image.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Queued-output lighting reference failed' }
    $lighting=Get-Content -LiteralPath "$graphics/lighting-reference-report.json" -Raw | ConvertFrom-Json
    if (!$lighting.InteriorLightingMatches) { throw 'Queued-output interior lighting differs' }
    @{Scope='Original captured GS through emulator compute queue, completion, selective guest writeback, checked remap and original-PS graphics; not native runtime animation'
        Cases=$cases;InteriorLighting=$lighting;NativeGearsRenderingVerified=$false
        Files=Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object { @{File=[IO.Path]::GetRelativePath($output,$_.FullName);Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$output/verification-report.json" -Encoding UTF8
    Write-Output "PASS emulator-queued original GS exports and graphics consume: $output"
} finally { Pop-Location }
