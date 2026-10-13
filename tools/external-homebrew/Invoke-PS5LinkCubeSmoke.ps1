# Copyright (C) 2026 Touché PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateSet('Base','Depth','DepthDisabled','DepthStatic','DepthDisabledStatic','DepthCpuClear','DepthReverseStatic','DepthDisabledReverseStatic','TextureStatic','TextureDepth','TextureBindingsUpdatesFenced','TextureBindingsReverseUpdatesFenced','TextureBindingsUpdates','TextureBindingsReverseUpdates','TextureBindingsStatic','TextureBindingsReverseStatic','TextureBindingsSharedStatic','TexturePairOverlap','TexturePairOverlapStatic','TexturePairOverlapReverseStatic','TextureUpdates','TextureOverlap','TextureOverlapStatic','TextureOverlapReverseStatic','TextureOverlapDisabledStatic','TextureOverlapDisabledReverseStatic')][string]$Profile='Base')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$capture=Join-Path $out "smoke-$Profile-$stamp"
$exe=Join-Path $root 'artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe'
$inputName=switch($Profile) {
    'Depth' {'app-depth.elf'}
    'DepthDisabled' {'app-depth-disabled.elf'}
    'DepthStatic' {'app-depth-static.elf'}
    'DepthDisabledStatic' {'app-depth-disabled-static.elf'}
    'DepthCpuClear' {'app-depth-cpu-clear.elf'}
    'DepthReverseStatic' {'app-depth-reverse-static.elf'}
    'DepthDisabledReverseStatic' {'app-depth-disabled-reverse-static.elf'}
    'TextureStatic' {'app-texture-static.elf'}
    'TextureDepth' {'app-texture-depth.elf'}
    'TextureUpdates' {'app-texture-updates.elf'}
    'TextureBindingsStatic' {'app-texture-bindings-static.elf'}
    'TextureBindingsUpdates' {'app-texture-bindings-updates.elf'}
    'TextureBindingsUpdatesFenced' {'app-texture-bindings-updates-fenced.elf'}
    'TextureBindingsReverseUpdatesFenced' {'app-texture-bindings-reverse-updates-fenced.elf'}
    'TextureBindingsReverseUpdates' {'app-texture-bindings-reverse-updates.elf'}
    'TextureBindingsReverseStatic' {'app-texture-bindings-reverse-static.elf'}
    'TextureBindingsSharedStatic' {'app-texture-bindings-shared-static.elf'}
    'TexturePairOverlap' {'app-texture-overlap-pair.elf'}
    'TexturePairOverlapStatic' {'app-texture-overlap-pair-static.elf'}
    'TexturePairOverlapReverseStatic' {'app-texture-overlap-pair-reverse-static.elf'}
    'TextureOverlap' {'app-texture-overlap.elf'}
    'TextureOverlapStatic' {'app-texture-overlap-static.elf'}
    'TextureOverlapReverseStatic' {'app-texture-overlap-reverse-static.elf'}
    'TextureOverlapDisabledStatic' {'app-texture-overlap-disabled-static.elf'}
    'TextureOverlapDisabledReverseStatic' {'app-texture-overlap-disabled-reverse-static.elf'}
    default {'app-init-abi.elf'}
}
$elf=Join-Path $out $inputName
if(-not (Test-Path -LiteralPath $elf)) { throw "Build the selected cube profile first: $inputName" }
$buildReportPath=Join-Path $out ($inputName.Replace('app','build-report').Replace('.elf','.json'))
$buildReport=Get-Content -LiteralPath $buildReportPath -Raw | ConvertFrom-Json
$inputHash=(Get-FileHash -LiteralPath $elf).Hash
if($buildReport.ElfSha256 -ne $inputHash) { throw 'Input ELF does not match its build provenance' }
New-Item -ItemType Directory -Path $capture -Force | Out-Null
# This profile needs direct CPU framebuffer-write tracking. Windows leaves
# it off by default because it can regress other titles; do not set globally.
$settings=@{
    SHARPEMU_GUEST_IMAGE_CPU_SYNC='1'
    SHARPEMU_TRACE_GUEST_IMAGES='present'
    SHARPEMU_GUEST_IMAGE_DUMP_DIR=$capture
    SHARPEMU_SWAPCHAIN_DUMP_EVERY='150'
}
if($Profile.StartsWith('Texture')) {
    $settings.SHARPEMU_STRICT_SHADER_DESCRIPTORS='1'
    $settings.SHARPEMU_TRACE_DRAWS='1'
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
    if($Profile.StartsWith('Texture') -and
       ($log.Contains(':FALLBACK') -or
        $log -notmatch 'textures=0x[0-9A-Fa-f]+:64x64:f10/n0/t9/dFAC')) {
        throw 'Expected a real 64x64 RGBA8 texture binding without fallback; inspect the draw trace'
    }
    $textureAddresses=@([regex]::Matches($log,'textures=(0x[0-9A-Fa-f]+):64x64:f10/n0/t9/dFAC') |
        ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if($buildReport.DistinctObjectTextures -and $textureAddresses.Count -lt 2) {
        throw 'Expected two different real per-draw texture addresses'
    }
    $frames=@(Get-ChildItem -LiteralPath $capture -Filter '*.bgra')
    $hashes=@($frames | Get-FileHash | Select-Object -ExpandProperty Hash -Unique)
    $staticView=$Profile.EndsWith('Static')
    if($frames.Count -lt 1 -or (-not $staticView -and $hashes.Count -lt 2)) { throw 'Expected presented frame captures (and varying captures for animated inputs)' }
    @{
        Profile=$Profile; FrozenView=$staticView; Input=$elf; InputSha256=$inputHash
        SourceModified=$buildReport.SourceModified; DepthClearMethod=$buildReport.DepthClearMethod
        ReversedFaceOrder=$buildReport.ReversedFaceOrder
        ReversedObjectOrder=$buildReport.ReversedObjectOrder; ObjectCount=$buildReport.ObjectCount
        TextureCpuUpdates=$buildReport.TextureCpuUpdates
        DistinctObjectTextures=$buildReport.DistinctObjectTextures
        BoundTextureAddresses=$textureAddresses
        TerminalGpuFence=$buildReport.TerminalGpuFence; FrameOwnership=$buildReport.FrameOwnership
        StrictShaderDescriptors=$Profile.StartsWith('Texture')
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
