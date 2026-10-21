# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$ComputeRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$manifest=(Resolve-Path -LiteralPath $ManifestPath).Path
$compute=(Resolve-Path -LiteralPath $ComputeRoot).Path
$out=Join-Path $repo ('artifacts/xash3d-graphics/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
$dotnet='C:/Dev/dotnet10/dotnet.exe'
$project=Join-Path $repo 'tools/SharpEmu.Tools.NativeShaderAudit'
& $dotnet run --project $project -c Release -- --bsp-graphics $manifest $compute $out > "$out/driver.log" 2>&1
if($LASTEXITCODE -ne 0) { throw "Original BSP graphics draw failed: $out" }
& $dotnet run --project $project -c Release --no-build -- --verify-bsp-pixels $compute $out > "$out/reference.log" 2>&1
if($LASTEXITCODE -ne 0) { throw "Original BSP pixel reference failed: $out" }
$reference=Get-Content "$out/pixel-reference.json" -Raw | ConvertFrom-Json
$negative=New-Item -ItemType Directory -Path (Join-Path $out 'corrupted-pixel')
$graphics=Get-Content "$out/graphics-report.json" -Raw | ConvertFrom-Json
foreach($capture in $graphics.Captures) {
    $destination=Join-Path $negative.FullName ([IO.Path]::GetFileName($capture.File))
    Copy-Item -LiteralPath $capture.File -Destination $destination
    $capture.File=$destination
}
$bad=$graphics.Captures[-1].File
$bytes=[IO.File]::ReadAllBytes($bad)
$offset=[int]$reference.CorruptionControlPixel*4
if($offset -lt 0 -or $offset -ge $bytes.Length) { throw 'Missing reference-selected interior control pixel' }
$bytes[$offset]=$bytes[$offset] -bxor 255
[IO.File]::WriteAllBytes($bad,$bytes)
$graphics.Captures[-1].Sha256=(Get-FileHash -LiteralPath $bad).Hash
$graphics | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $negative 'graphics-report.json') -Encoding utf8
& $dotnet run --project $project -c Release --no-build -- --verify-bsp-pixels $compute $negative.FullName > (Join-Path $negative 'reference.log') 2>&1
$code=$LASTEXITCODE
$badReference=Get-Content (Join-Path $negative 'pixel-reference.json') -Raw | ConvertFrom-Json
if($code -eq 0 -or $badReference.InteriorTexturingAndDiscardVerified -or $badReference.Mismatches -ne 1) { throw 'Altered interior pixel did not fail exactly its numerical check' }
$positive=Get-Content "$out/graphics-report.json" -Raw | ConvertFrom-Json
foreach($capture in $positive.Captures) { if((Get-FileHash -LiteralPath $capture.File).Hash -ne $capture.Sha256) { throw 'Positive capture changed during negative check' } }
@{Scope='Original hash-pinned BSP GS/PS with source-derived inputs, actual GPU geometry and two-texture/discard rendering; not native Xash3D execution'
    InteriorTexturingAndDiscardVerified=$true;NativeTitleExecuted=$false;WholeImageConformanceVerified=$false
    InteriorPixels=$reference.InteriorPixels;DiscardedPixels=$reference.DiscardedPixels;ExteriorPixels=$reference.ExteriorPixels
    MaximumChannelError=$reference.MaximumChannelError;CorruptedInteriorPixelRejected=$true
    MissingLightmapRejected=($reference.MissingLightmapMismatches -gt 1000);SwappedTexturesRejected=($reference.SwappedTexturesMismatches -gt 1000)
    BlackInsteadOfDiscardRejected=($reference.BlackDiscardMismatches -gt 1000)
    ComputeRoot=$compute;ManifestSha256=(Get-FileHash -LiteralPath $manifest).Hash
    Files=@(Get-ChildItem -LiteralPath $out -File | ForEach-Object { @{File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
} | ConvertTo-Json -Depth 6 | Set-Content "$out/execution-report.json" -Encoding utf8
Write-Output "PASS original BSP textured pixels and discard with negative controls: $out"
