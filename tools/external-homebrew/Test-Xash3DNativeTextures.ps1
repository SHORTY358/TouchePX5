# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ComputeRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compute=(Resolve-Path -LiteralPath $ComputeRoot).Path
$out=Join-Path $repo ('artifacts/xash3d-native-textures/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out | Out-Null
$dotnet='C:/Dev/dotnet10/dotnet.exe'
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release -- --native-bsp-textures $compute $out > "$out/gpu.log" 2>&1
if($LASTEXITCODE -ne 0) { throw "Native BSP texture qualification failed: $out/gpu.log" }
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --verify-native-bsp-textures $out > "$out/pixel.log" 2>&1
if($LASTEXITCODE -ne 0) { throw 'Retained native BSP pixel reference failed' }
$report=Get-Content "$out/texture-report.json" -Raw | ConvertFrom-Json
$last=$report.Captures[-1].File
$originalHash=(Get-FileHash -LiteralPath $last).Hash
$bad=Join-Path $out 'corrupted-pixel';New-Item -ItemType Directory -Path $bad | Out-Null
$copy=Join-Path $bad 'changed.rgba'
$bytes=[IO.File]::ReadAllBytes($last)
$bytes[(45*640+80)*4]=[byte]([int]$bytes[(45*640+80)*4] -bxor 255)
[IO.File]::WriteAllBytes($copy,$bytes)
$report.Captures[-1].File=$copy
$report | ConvertTo-Json -Depth 12 | Set-Content "$bad/texture-report.json" -Encoding utf8
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --verify-native-bsp-textures $bad > "$bad/rejection.log" 2>&1
if($LASTEXITCODE -eq 0 -or -not (Get-Content "$bad/rejection.log" -Raw).Contains('Native texture pixel mismatch: 1')) { throw 'One-pixel native texture negative control did not reject exactly once' }
if((Get-FileHash -LiteralPath $last).Hash -ne $originalHash) { throw 'Positive native texture image changed' }
# Lossless preview of the actual RGBA target; no enhancement or synthetic pixels.
Add-Type -AssemblyName System.Drawing
$pixels=[IO.File]::ReadAllBytes($last)
$bitmap=[Drawing.Bitmap]::new(640,360,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    $locked=$bitmap.LockBits([Drawing.Rectangle]::new(0,0,640,360),[Drawing.Imaging.ImageLockMode]::WriteOnly,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bgra=[byte[]]::new($pixels.Length)
        for($i=0;$i -lt $pixels.Length;$i+=4) { $bgra[$i]=$pixels[$i+2];$bgra[$i+1]=$pixels[$i+1];$bgra[$i+2]=$pixels[$i];$bgra[$i+3]=$pixels[$i+3] }
        [Runtime.InteropServices.Marshal]::Copy($bgra,0,$locked.Scan0,$bgra.Length)
    } finally { $bitmap.UnlockBits($locked) }
    $bitmap.Save("$out/native-resource-tiles.png",[Drawing.Imaging.ImageFormat]::Png)
} finally { $bitmap.Dispose() }
@{Scope='Original native PS and descriptors on controlled constant-UV tiles with original source-bundle resources; not native scene rendering'
    ComputeRoot=$compute;InteriorPixelSamplingVerified=$true;OnePixelCorruptionRejected=$true
    NativeSceneRenderingVerified=$false;FullMipSamplingVerified=$false
    Files=@(Get-ChildItem -LiteralPath $out -File | ForEach-Object { @{File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
} | ConvertTo-Json -Depth 6 | Set-Content "$out/execution-report.json" -Encoding utf8
Write-Output "PASS controlled native BSP pixel resources: $out"
