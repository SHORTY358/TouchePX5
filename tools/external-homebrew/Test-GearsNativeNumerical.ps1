# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param([ValidateRange(1,10000)][int]$FrameOrdinal=100,[ValidateRange(10,60)][int]$TimeoutSeconds=12)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $repo ('artifacts/gears-native-numerical/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$variables=@('TOUCHEPX5_GEARS_NATIVE_AUDIT_DIR','TOUCHEPX5_GEARS_NATIVE_AUDIT_FRAME')
$saved=@{}
foreach($name in $variables) { $saved[$name]=[Environment]::GetEnvironmentVariable($name) }
try {
    $env:TOUCHEPX5_GEARS_NATIVE_AUDIT_DIR=$output
    $env:TOUCHEPX5_GEARS_NATIVE_AUDIT_FRAME=[string]$FrameOrdinal
    & "$PSScriptRoot/Test-GearsNativeNgg.ps1" -RuntimeScalars -PreserveFlipPayloads -TimeoutSeconds $TimeoutSeconds -CaptureEvery 60
    if(@(Get-ChildItem -LiteralPath $output -Directory -Filter 'draw-*').Count -ne 4) { throw 'Selected late native frame did not complete all four draws' }
    $dotnet='C:/Dev/dotnet10/dotnet.exe'
    & $dotnet run --project "$repo/tools/SharpEmu.Tools.GearsComputeAudit" -c Release --no-build -- --native-frame $output
    if($LASTEXITCODE -ne 0) { throw "Actual native numerical audit failed: $output" }
    Write-Output "PASS independent live native GS numerical outputs: $output"
} finally {
    foreach($name in $variables) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
