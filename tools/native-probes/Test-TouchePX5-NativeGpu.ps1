[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
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
    Cases=$results
}
$reportPath=Join-Path $reportRoot 'report.json'
function Save-Report { $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8 }
Save-Report
try {
    foreach($case in @(
        @{Name='compute-repeat'; Script='Test-TouchePX5-Compute.ps1'; Guest='smoke-compute\compute.log'},
        @{Name='triangle-repeat'; Script='Test-TouchePX5-Triangle.ps1'; Guest='smoke-triangle\triangle.log'}
    )) {
        $result=[ordered]@{Name=$case.Name; Status='Running'; Log=(Join-Path $reportRoot ($case.Name+'.log')); Error=$null; GuestLog=$null}
        $results.Add($result); Save-Report
        Write-Output "RUN: $($case.Name)"
        try {
            & (Join-Path $PSScriptRoot $case.Script) *> $result.Log
            if(-not $?) { throw "Failed: $($case.Name)" }
            $result.Status='Passed'
        } catch { $result.Status='Failed'; $result.Error=$_.Exception.Message; throw }
        finally {
            $guestLog=Join-Path $sourceRoot ('artifacts\'+$case.Guest)
            if(Test-Path -LiteralPath $guestLog) {
                $result.GuestLog=Join-Path $reportRoot ($case.Name+'-guest.log')
                Copy-Item -LiteralPath $guestLog -Destination $result.GuestLog
            }
            Save-Report
        }
        Write-Output "PASS: $($case.Name)"
    }
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'artifacts\smoke-triangle\verification.json') -Destination (Join-Path $reportRoot 'triangle-verification.json')
    if((Get-FileHash -LiteralPath $exe).Hash -ne $report.ExecutableSha256) { throw 'Executable changed during checks.' }
    $report.Status='Passed'
} catch { $report.Status='Failed'; throw }
finally { $report.FinishedUtc=[DateTime]::UtcNow.ToString('o'); Save-Report; Write-Output "Report: $reportPath" }
