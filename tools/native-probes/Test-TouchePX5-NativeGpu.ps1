[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'RuntimeManifest.ps1')
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$reportRoot=Join-Path $sourceRoot ('artifacts\native-gpu\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$exe=Join-Path $sourceRoot 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe'
$results=[Collections.Generic.List[object]]::new()
$report=[ordered]@{
    StartedUtc=[DateTime]::UtcNow.ToString('o'); FinishedUtc=$null; Status='Running'
    SourceCommit=(& git -C $sourceRoot rev-parse HEAD)
    SourceChanges=@(& git -C $sourceRoot status --porcelain)
    ExecutableSha256=(Get-FileHash -LiteralPath $exe).Hash
    RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe))
    Cases=$results
}
$reportPath=Join-Path $reportRoot 'report.json'
function Save-Report { $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8 }
Save-Report
$byteCases=@(foreach($byteCase in 0..5) {
    foreach($signed in @($false,$true)) {
        foreach($acb in @($false,$true)) {
            $suffix=if($signed) { '-signed' } else { '' }
            if($acb) { $suffix+='-compute-queue' }
            @{Name="compute-r32-byte-$byteCase$suffix"; Script='Test-TouchePX5-Compute.ps1';
              Guest="smoke-compute-image-atomics-partial-byte-case$byteCase$suffix\compute.log";
              Parameters=@{UnalignedByte=$true;ByteCase=$byteCase;SignedImage=$signed;ComputeQueue=$acb}}
        }
    }
})
try {
    foreach($case in @(
        @{Name='flip-cpu'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-cpu-none\flip.log'; Parameters=@{Mode='cpu'}},
        @{Name='flip-cpu-acquire-retry'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-cpu-acquire\flip.log'; Parameters=@{Mode='cpu';Retry='acquire'}},
        @{Name='flip-cpu-present-retry'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-cpu-present\flip.log'; Parameters=@{Mode='cpu';Retry='present'}},
        @{Name='flip-burst'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-burst-none\flip.log'; Parameters=@{Mode='burst'}},
        @{Name='flip-direct'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-direct-none\flip.log'; Parameters=@{Mode='direct'}},
        @{Name='flip-ordered'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-ordered-none\flip.log'; Parameters=@{Mode='ordered'}},
        @{Name='flip-ordered-acquire-retry'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-ordered-acquire\flip.log'; Parameters=@{Mode='ordered';Retry='acquire'}},
        @{Name='flip-ordered-present-retry'; Script='Test-TouchePX5-FlipLifecycle.ps1'; Guest='smoke-flip-ordered-present\flip.log'; Parameters=@{Mode='ordered';Retry='present'}},
        @{Name='compute-repeat'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute\compute.log'; Parameters=@{}},
        @{Name='compute-indirect-dimensions'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-indirect-dimensions\compute.log'; Parameters=@{IndirectDimensions=$true}},
        @{Name='compute-indirect-base'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-indirect-dimensions-base-relative\compute.log'; Parameters=@{BaseRelative=$true}},
        @{Name='compute-indirect-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-indirect-dimensions-compute-queue\compute.log'; Parameters=@{ComputeQueue=$true}},
        @{Name='compute-image-atomics'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-image-atomics\compute.log'; Parameters=@{ImageAtomics=$true}},
        @{Name='compute-image-atomics-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-image-atomics-compute-queue\compute.log'; Parameters=@{ImageAtomics=$true;ComputeQueue=$true}},
        @{Name='compute-bounded-image-atomics'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics\compute.log'; Parameters=@{BoundedAtomics=$true}},
        @{Name='compute-bounded-image-atomics-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-compute-queue\compute.log'; Parameters=@{BoundedAtomics=$true;ComputeQueue=$true}},
        @{Name='compute-bounded-buffer-atomics'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-buffer\compute.log'; Parameters=@{BufferAtomics=$true}},
        @{Name='compute-bounded-buffer-atomics-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-buffer-compute-queue\compute.log'; Parameters=@{BufferAtomics=$true;ComputeQueue=$true}},
        @{Name='compute-bounded-signed-image-atomics'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-signed\compute.log'; Parameters=@{SignedImage=$true}},
        @{Name='compute-bounded-signed-image-atomics-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-signed-compute-queue\compute.log'; Parameters=@{SignedImage=$true;ComputeQueue=$true}},
        @{Name='compute-lds-atomics'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-lds\compute.log'; Parameters=@{LdsAtomics=$true}},
        @{Name='compute-lds-atomics-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-lds-compute-queue\compute.log'; Parameters=@{LdsAtomics=$true;ComputeQueue=$true}},
        @{Name='compute-lds-wide'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-lds-wide\compute.log'; Parameters=@{LdsWide=$true}},
        @{Name='compute-lds-wide-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-lds-wide-compute-queue\compute.log'; Parameters=@{LdsWide=$true;ComputeQueue=$true}},
        @{Name='compute-lds-paired'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-lds-paired\compute.log'; Parameters=@{LdsPaired=$true}},
        @{Name='compute-lds-paired-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-bounded-atomics-lds-paired-compute-queue\compute.log'; Parameters=@{LdsPaired=$true;ComputeQueue=$true}},
        @{Name='compute-image-atomics-cross-queue'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-image-atomics-cross-queue\compute.log'; Parameters=@{ImageAtomicsCrossQueue=$true}},
        @{Name='compute-r32-partial'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-image-atomics-partial\compute.log'; Parameters=@{PartialImage=$true}},
        @{Name='compute-r32-partial-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-image-atomics-partial-compute-queue\compute.log'; Parameters=@{PartialImage=$true;ComputeQueue=$true}},
        @{Name='compute-r32-partial-signed'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-image-atomics-partial-signed\compute.log'; Parameters=@{PartialImage=$true;SignedImage=$true}},
        @{Name='compute-r32-partial-signed-acb'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute-image-atomics-partial-signed-compute-queue\compute.log'; Parameters=@{PartialImage=$true;SignedImage=$true;ComputeQueue=$true}},
        @{Name='triangle-repeat'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle\triangle.log'; Parameters=@{}},
        @{Name='triangle-scissor'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-scissor\triangle.log'; Parameters=@{Scissor=$true}},
        @{Name='triangle-blend'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-blend\triangle.log'; Parameters=@{Blend=$true}},
        @{Name='triangle-depth'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth\triangle.log'; Parameters=@{Depth=$true}},
        @{Name='triangle-depth-indirect'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect\triangle.log'; Parameters=@{Indirect=$true}},
        @{Name='triangle-depth-indirect-multi'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect-multi\triangle.log'; Parameters=@{IndirectMulti=$true}},
        @{Name='triangle-depth-indirect-gpu-count'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect-multi-gpu-count\triangle.log'; Parameters=@{IndirectGpuCount=$true}},
        @{Name='triangle-depth-indirect-gpu-args'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect-gpu-args\triangle.log'; Parameters=@{IndirectGpuArgs=$true}},
        @{Name='triangle-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect-gpu-args-cross-queue\triangle.log'; Parameters=@{CrossQueue=$true}},
        @{Name='triangle-cross-queue-reuse'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect-gpu-args-cross-queue-reuse\triangle.log'; Parameters=@{CrossQueueReuse=$true}},
        @{Name='triangle-cross-queue-write-data'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect-gpu-args-cross-queue-write-data\triangle.log'; Parameters=@{CrossQueueWriteData=$true}},
        @{Name='triangle-cross-queue-dma'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth-indirect-gpu-args-cross-queue-dma\triangle.log'; Parameters=@{CrossQueueDma=$true}},
        @{Name='triangle-dma-image'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image\triangle.log'; Parameters=@{DmaImage=$true}},
        @{Name='triangle-dma-image-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue\triangle.log'; Parameters=@{DmaImageCrossQueue=$true}},
        @{Name='triangle-dma-image-prefix'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-prefix\triangle.log'; Parameters=@{DmaImagePrefix=$true}},
        @{Name='triangle-dma-image-prefix-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue-prefix\triangle.log'; Parameters=@{DmaImagePrefix=$true;DmaImageCrossQueue=$true}},
        @{Name='triangle-dma-image-span'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-prefix-span\triangle.log'; Parameters=@{DmaImageSpan=$true}},
        @{Name='triangle-dma-image-span-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue-prefix-span\triangle.log'; Parameters=@{DmaImageSpan=$true;DmaImageCrossQueue=$true}},
        @{Name='triangle-dma-image-dependent-draw'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-prefix-draw\triangle.log'; Parameters=@{DmaImageDraw=$true}},
        @{Name='triangle-dma-image-dependent-draw-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue-prefix-draw\triangle.log'; Parameters=@{DmaImageDraw=$true;DmaImageCrossQueue=$true}},
        @{Name='triangle-dma-image-sample'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-prefix-sample\triangle.log'; Parameters=@{DmaImageSample=$true}},
        @{Name='triangle-dma-image-sample-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue-prefix-sample\triangle.log'; Parameters=@{DmaImageSample=$true;DmaImageCrossQueue=$true}},
        @{Name='triangle-dma-image-sample-preserved'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-prefix-sample-preserved\triangle.log'; Parameters=@{DmaImageSamplePreserved=$true}},
        @{Name='triangle-dma-image-sample-preserved-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue-prefix-sample-preserved\triangle.log'; Parameters=@{DmaImageSamplePreserved=$true;DmaImageCrossQueue=$true}},
        @{Name='triangle-dma-image-compute-read'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-prefix-sample-preserved-compute-read\triangle.log'; Parameters=@{DmaImageComputeRead=$true}},
        @{Name='triangle-dma-image-compute-read-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue-prefix-sample-preserved-compute-read\triangle.log'; Parameters=@{DmaImageComputeRead=$true;DmaImageCrossQueue=$true}},
        @{Name='triangle-dma-image-compute-store'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-prefix-sample-preserved-compute-read-store\triangle.log'; Parameters=@{DmaImageComputeStore=$true}},
        @{Name='triangle-dma-image-compute-store-cross-queue'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-dma-image-cross-queue-prefix-sample-preserved-compute-read-store\triangle.log'; Parameters=@{DmaImageComputeStore=$true;DmaImageCrossQueue=$true}},
        @{Name='triangle-texture'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture\triangle.log'; Parameters=@{Texture=$true}},
        @{Name='triangle-texture-select'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select\triangle.log'; Parameters=@{TextureSelect=$true}},
        @{Name='triangle-texture-uv'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-uv\triangle.log'; Parameters=@{TextureUv=$true}},
        @{Name='triangle-perspective'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-uv-perspective\triangle.log'; Parameters=@{Perspective=$true}},
        @{Name='triangle-linear-filter'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-uv-linear\triangle.log'; Parameters=@{LinearFilter=$true}},
        @{Name='triangle-mip'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip\triangle.log'; Parameters=@{Mip=$true}},
        @{Name='triangle-mip-base-only'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-base-only\triangle.log'; Parameters=@{MipBaseOnly=$true}},
        @{Name='triangle-mip-lower-view'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-lower-view\triangle.log'; Parameters=@{MipLowerView=$true}},
        @{Name='triangle-mip-cache-pressure'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-pressure\triangle.log'; Parameters=@{MipCachePressure=$true}},
        @{Name='triangle-mip-2d'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-2d\triangle.log'; Parameters=@{Mip2D=$true}},
        @{Name='triangle-mip-odd'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-2d-odd\triangle.log'; Parameters=@{MipOdd=$true}},
        @{Name='triangle-mip-srgb'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-srgb\triangle.log'; Parameters=@{MipSrgb=$true}},
        @{Name='triangle-mip-bc1'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-bc1\triangle.log'; Parameters=@{MipBc1=$true}},
        @{Name='triangle-mip-bc1-2d'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-bc1-2d\triangle.log'; Parameters=@{MipBc1=$true;Mip2D=$true}},
        @{Name='triangle-mip-bc3'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-bc3\triangle.log'; Parameters=@{MipBc3=$true}},
        @{Name='triangle-mip-bc7'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-bc7\triangle.log'; Parameters=@{MipBc7=$true}},
        @{Name='triangle-mip-bc7-srgb'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-srgb-bc7\triangle.log'; Parameters=@{MipBc7=$true; MipSrgb=$true}},
        @{Name='triangle-mip-bc7-srgb-trilinear'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-srgb-bc7-trilinear\triangle.log'; Parameters=@{MipBc7=$true; MipSrgb=$true; MipTrilinear=$true}},
        @{Name='triangle-mip-bc1-srgb'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-srgb-bc1\triangle.log'; Parameters=@{MipBc1=$true; MipSrgb=$true}},
        @{Name='triangle-late-pixel-exit'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-late-pixel-exit\triangle.log'; Parameters=@{LatePixelExit=$true}},
        @{Name='triangle-mip-bc3-srgb'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-texture-select-mip-srgb-bc3\triangle.log'; Parameters=@{MipBc3=$true; MipSrgb=$true}},
        @{Name='triangle-index16'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-index16\triangle.log'; Parameters=@{IndexBits=16}},
        @{Name='triangle-index32-scissor'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-scissor-index32\triangle.log'; Parameters=@{IndexBits=32; Scissor=$true}}
    ) + $byteCases) {
        $result=[ordered]@{Name=$case.Name; Status='Running'; Log=(Join-Path $reportRoot ($case.Name+'.log')); Error=$null; GuestLog=$null}
        $results.Add($result); Save-Report
        Write-Output "RUN: $($case.Name)"
        $caseStartedUtc=[DateTime]::UtcNow
        try {
            $parameters=$case.Parameters
            & (Join-Path $PSScriptRoot $case.Script) @parameters *> $result.Log
            if(-not $?) { throw "Failed: $($case.Name)" }
            if($case.Script -in @('Test-TouchePX5-Triangle.ps1','Test-TouchePX5-FlipLifecycle.ps1')) {
                $verification=Join-Path (Split-Path (Join-Path $sourceRoot ('artifacts\'+$case.Guest))) 'verification.json'
                Copy-Item -LiteralPath $verification -Destination (Join-Path $reportRoot ($case.Name+'-verification.json'))
            }
            $result.Status='Passed'
        } catch { $result.Status='Failed'; $result.Error=$_.Exception.Message; throw }
        finally {
            $guestLog=Join-Path $sourceRoot ('artifacts\'+$case.Guest)
            if((Test-Path -LiteralPath $guestLog) -and (Get-Item -LiteralPath $guestLog).LastWriteTimeUtc -ge $caseStartedUtc) {
                $result.GuestLog=Join-Path $reportRoot ($case.Name+'-guest.log')
                Copy-Item -LiteralPath $guestLog -Destination $result.GuestLog
            }
            Save-Report
        }
        Write-Output "PASS: $($case.Name)"
    }
    $runtimeAfter=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe))
    if((ConvertTo-Json -InputObject $runtimeAfter -Depth 4 -Compress) -cne
       (ConvertTo-Json -InputObject $report.RuntimeFiles -Depth 4 -Compress)) {
        throw 'Runtime binaries or loading configuration changed during checks.'
    }
    $report.Status='Passed'
} catch { $report.Status='Failed'; throw }
finally { $report.FinishedUtc=[DateTime]::UtcNow.ToString('o'); Save-Report; Write-Output "Report: $reportPath" }
