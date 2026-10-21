# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildRoot,[ValidateRange(8,30)][int]$TimeoutSeconds=10)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$result=@(& "$PSScriptRoot/Invoke-Xash3DRenderer.ps1" -BuildRoot $BuildRoot -TimeoutSeconds $TimeoutSeconds -ExperimentalBsp -PreserveFlipPayloads -CaptureFrames -CaptureInputs -AuditNgg -PassThru)
$invocation=$result|Where-Object {$_ -isnot [string] -and $_.NativeRun}|Select-Object -Last 1
if($null -eq $invocation){throw 'No exact native BSP invocation identity'}
$run=$invocation.NativeRun
$report=Get-Content -LiteralPath $invocation.ReportPath -Raw|ConvertFrom-Json
$log=Get-Content "$run/stderr.log" -Raw
if(-not $report.TerminatedAtDeadline -or $report.StoppedAfterDiagnosticPark -or
    $log -match 'XASH_RENDERER_LOOP_FAILURE|XASH_RENDERER_PARK|consume failed|schedule failed|shader_translate_miss|vk.device_lost|delivery rejected') {throw "Native BSP loop failed: $run"}
$consumed=[regex]::Matches($log,'experimental_bsp_ngg consumed seq=').Count
$flips=[regex]::Matches($log,'videoout.resolve_flip').Count
if($consumed -lt 8 -or $flips -lt 3){throw 'Insufficient repeated native BSP draws and flips'}
$reference=$run+'-independent-reference'
$manifest=Join-Path $report.NativeBuild.InputRoot 'shaders/bsp_textured.manifest.json'
$dotnet='C:/Dev/dotnet10/dotnet.exe'
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release -- --native-bsp-compute $run $manifest $reference
if($LASTEXITCODE -ne 0){throw 'Native BSP source reference failed'}
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --verify-native-bsp-completed $run $reference
if($LASTEXITCODE -ne 0){throw 'Completed native BSP exports/indices differ'}
$bad=Join-Path $reference 'wrong-native-export'
New-Item -ItemType Directory -Path $bad | Out-Null
Copy-Item -LiteralPath "$run/ngg-audit","$run/replay" -Destination $bad -Recurse
$wrongFile=Join-Path $bad 'ngg-audit/draw-000002/actual.json'
$words=Get-Content $wrongFile -Raw|ConvertFrom-Json
$words[4]=[BitConverter]::SingleToUInt32Bits([single]0.25)
ConvertTo-Json -InputObject $words -Compress|Set-Content $wrongFile -Encoding utf8
$hashPath=Join-Path $bad 'ngg-audit/draw-000002/hashes.json'
$hashes=Get-Content $hashPath -Raw|ConvertFrom-Json
$hashes.'actual.json'=(Get-FileHash $wrongFile).Hash
$hashes|ConvertTo-Json|Set-Content $hashPath -Encoding utf8
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --verify-native-bsp-completed $bad $reference > "$reference/wrong-native-export.log" 2>&1
if($LASTEXITCODE -eq 0 -or -not (Get-Content "$reference/wrong-native-export.log" -Raw).Contains('Native BSP export mismatches: 1')){throw 'Wrong native GS output did not reject exactly one word'}
# Recheck positive artifacts after the negative copy. This also restores the
# positive verification report's exact original-run identity.
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --verify-native-bsp-completed $run $reference
if($LASTEXITCODE -ne 0){throw 'Positive native BSP evidence changed'}
$frames=@(Get-ChildItem "$run/frames" -File -Filter '*.rgba'|Sort-Object Name)
if($frames.Count -lt 2 -or @($frames|ForEach-Object{(Get-FileHash $_.FullName).Hash}|Select-Object -Unique).Count -lt 2){throw 'Need distinct actual native clear/world target readbacks'}
@{Scope='Repeated original standalone native BSP clear/world draws using pinned indexed NGG and original pixel resources; exact first native exports/indices verified; whole-scene pixel fidelity unverified'
    NativeRun=$run;ConsumedDraws=$consumed;PresentedTickets=$flips;NativeExportsAndIndicesVerified=$true
    ChangedNativeExportRejected=$true;DistinctNativeTargetReadbacks=$true;ScenePixelFidelityVerified=$false
    DefaultEnabled=$false;FullEngineExecuted=$false;CommercialTitleCompatibilityVerified=$false
    Files=@(Get-ChildItem -LiteralPath $reference -File|ForEach-Object{@{File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
} | ConvertTo-Json -Depth 7|Set-Content "$reference/execution-report.json" -Encoding utf8
Write-Output "PASS bounded original native BSP frame path: $reference"
