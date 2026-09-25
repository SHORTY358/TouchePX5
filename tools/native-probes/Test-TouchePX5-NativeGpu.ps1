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
try {
    foreach($case in @(
        @{Name='compute-repeat'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute\compute.log'; Parameters=@{}},
        @{Name='triangle-repeat'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle\triangle.log'; Parameters=@{}},
        @{Name='triangle-scissor'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-scissor\triangle.log'; Parameters=@{Scissor=$true}},
        @{Name='triangle-blend'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-blend\triangle.log'; Parameters=@{Blend=$true}},
        @{Name='triangle-depth'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-depth\triangle.log'; Parameters=@{Depth=$true}},
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
        @{Name='triangle-index16'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-index16\triangle.log'; Parameters=@{IndexBits=16}},
        @{Name='triangle-index32-scissor'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle-scissor-index32\triangle.log'; Parameters=@{IndexBits=32; Scissor=$true}}
    )) {
        $result=[ordered]@{Name=$case.Name; Status='Running'; Log=(Join-Path $reportRoot ($case.Name+'.log')); Error=$null; GuestLog=$null}
        $results.Add($result); Save-Report
        Write-Output "RUN: $($case.Name)"
        $caseStartedUtc=[DateTime]::UtcNow
        try {
            $parameters=$case.Parameters
            & (Join-Path $PSScriptRoot $case.Script) @parameters *> $result.Log
            if(-not $?) { throw "Failed: $($case.Name)" }
            if($case.Name -ne 'compute-repeat') {
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
