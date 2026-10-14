# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param(
    [Parameter(Mandatory)][string]$CaptureDirectory,
    [Parameter(Mandatory)][string]$ComputeDirectory,
    [string]$Dotnet = 'C:\Dev\dotnet10\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$capture = (Resolve-Path -LiteralPath $CaptureDirectory).Path
$compute = (Resolve-Path -LiteralPath $ComputeDirectory).Path
$output = Join-Path $repo ('artifacts/gears-graphics-replay/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
Push-Location $repo
try {
    & $Dotnet run --project tools/SharpEmu.Tools.GearsComputeAudit -c Release -- --graphics $capture $compute $output > "$repo/artifacts/gears-graphics-run-latest.log" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Independent graphics replay failed; see artifacts/gears-graphics-run-latest.log' }
    Copy-Item -LiteralPath "$repo/artifacts/gears-graphics-run-latest.log" -Destination "$output/gpu.log"
    $image = Get-ChildItem -LiteralPath $output -Filter 'present-*.bgra' | Select-Object -First 1
    & $Dotnet run --project tools/SharpEmu.Tools.GearsComputeAudit -c Release --no-build -- --lighting-reference $compute $image.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Independent lighting reference failed' }
    $comparison = Get-Content -LiteralPath "$output/lighting-reference-report.json" -Raw | ConvertFrom-Json
    if ($comparison.CheckedInteriorPixels -lt 10000 -or $comparison.MismatchedPixels -ne 0) {
        throw "Interior lighting differs: $($comparison.MismatchedPixels)/$($comparison.CheckedInteriorPixels)"
    }
    # Convert the actual GPU byte capture for visual inspection, not a reference image.
    Add-Type -AssemblyName System.Drawing
    $dimensions = [regex]::Match($image.Name, 'present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm.bgra')
    if (!$dimensions.Success) { throw 'Unsupported screenshot format' }
    $width = [int]$dimensions.Groups[1].Value; $height = [int]$dimensions.Groups[2].Value
    $bitmap = New-Object System.Drawing.Bitmap($width,$height,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $locked = $bitmap.LockBits((New-Object System.Drawing.Rectangle(0,0,$width,$height)),[System.Drawing.Imaging.ImageLockMode]::WriteOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $bytes = [IO.File]::ReadAllBytes($image.FullName)
            if ($bytes.Length -ne $width * $height * 4 -or $locked.Stride -ne $width * 4) { throw 'Wrong capture row extent' }
            [Runtime.InteropServices.Marshal]::Copy($bytes,0,$locked.Scan0,$bytes.Length)
        } finally { $bitmap.UnlockBits($locked) }
        $bitmap.Save("$output/mesh-replay.png",[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    @{
        Scope = 'Independent captured mesh GPU replay; original pixel shader; depth-tested GLSL interior lighting only; edges/presentation scaling and native runtime unverified'
        InteriorLightingPassed = $true; Comparison = $comparison; NativeGearsRenderingVerified = $false
        CaptureDirectory = $capture; ComputeDirectory = $compute
        Hashes = Get-ChildItem -LiteralPath $output -File | ForEach-Object { @{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/verification-report.json" -Encoding UTF8
    Write-Output "PASS independent depth-tested mesh interior lighting: $output"
} finally { Pop-Location }
