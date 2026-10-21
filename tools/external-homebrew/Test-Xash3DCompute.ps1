# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$manifest=(Resolve-Path -LiteralPath $ManifestPath).Path
$out=Join-Path $repo ('artifacts/xash3d-compute/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$dotnet='C:/Dev/dotnet10/dotnet.exe'
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release -- --bsp-compute $manifest $out
if($LASTEXITCODE -ne 0) { throw 'Original BSP compute preparation failed' }
function Invoke-CheckedGpu([string]$directory,[string]$expected,[string]$storage,[string]$label,[bool]$positive) {
    & $dotnet run --project "$repo/tools/SharpEmu.Tools.GpuConformance" -c Release --no-build -- "$directory/original-bsp-compute.spv" $expected "$directory/$label-actual.json" "$directory/initial.json" "$directory/limits.json" $storage > "$directory/$label-gpu.log" 2>&1
    $exitCode=$LASTEXITCODE
    $log=Get-Content -LiteralPath "$directory/$label-gpu.log" -Raw
    if($positive) {
        if($exitCode -ne 0 -or -not $log.Contains('RESULT: all values match')) { throw "Original BSP numerical mismatch: $directory" }
    } elseif($exitCode -ne 1 -or -not $log.Contains('driver accepted the SPIR-V') -or -not $log.Contains('mismatch(es)')) {
        throw "BSP negative control did not reject numerically: $directory/$label"
    }
}
$reports=@()
foreach($case in @('identity','projective','two-groups')) {
    $dir=Join-Path $out $case
    $hashes=[ordered]@{}
    foreach($file in @('expected.json','original-bsp-compute.spv','fixture-report.json','storage-fixture.json','initial.json','limits.json')) { $hashes[$file]=(Get-FileHash -LiteralPath "$dir/$file").Hash }
    Invoke-CheckedGpu $dir "$dir/expected.json" "$dir/storage-fixture.json" 'positive' $true
    foreach($file in @($hashes.Keys)) { if($hashes[$file] -ne (Get-FileHash -LiteralPath "$dir/$file").Hash) { throw "GPU fixture changed during execution: $file" } }
    Copy-Item -LiteralPath "$dir/positive-actual.json" -Destination "$dir/actual.json"
    $hashes['actual.json']=(Get-FileHash -LiteralPath "$dir/actual.json").Hash
    @{NumericalOutputsVerified=$true;Hashes=$hashes} | ConvertTo-Json -Depth 4 | Set-Content "$dir/gpu-result.json" -Encoding utf8
    & $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --consume-bsp $dir
    if($LASTEXITCODE -ne 0) { throw 'Completed original BSP connectivity remap failed' }
    $fixture=Get-Content "$dir/storage-fixture.json" -Raw | ConvertFrom-Json
    if($case -eq 'projective') {
        # Change a real source base-U coordinate; its independently expected
        # output remains original. Non-output buffers are compared to this input.
        $fixture.Buffers[1][3]=[BitConverter]::SingleToUInt32Bits([single]0.375)
        $fixture | ConvertTo-Json -Depth 7 -Compress | Set-Content "$dir/wrong-uv-fixture.json" -Encoding utf8
        Invoke-CheckedGpu $dir "$dir/expected.json" "$dir/wrong-uv-fixture.json" 'wrong-uv' $false
    }
    if($case -eq 'two-groups') {
        $meta=Get-Content "$dir/fixture-report.json" -Raw | ConvertFrom-Json
        $expected=Get-Content "$dir/expected.json" -Raw | ConvertFrom-Json
        for($v=0;$v -lt 6;$v++) {
            $word=[int]$meta.SourceFirstWord+128*9+$v*9+5
            $fixture.Buffers[2][$word]=$v
            $expected[$word]=$v # match only the changed input table, not exports
        }
        $fixture | ConvertTo-Json -Depth 7 -Compress | Set-Content "$dir/wrong-source-fixture.json" -Encoding utf8
        ConvertTo-Json -InputObject $expected -Compress | Set-Content "$dir/wrong-source-expected.json" -Encoding utf8
        Invoke-CheckedGpu $dir "$dir/wrong-source-expected.json" "$dir/wrong-source-fixture.json" 'wrong-source' $false
        $failures=@(Select-String -LiteralPath "$dir/wrong-source-gpu.log" -Pattern '^FAIL  bounded export word (\d+):' | ForEach-Object { [int]$_.Matches[0].Groups[1].Value })
        if($failures.Count -eq 0 -or @($failures | Where-Object { $_ -lt 128*12 -or $_ -ge 134*12 }).Count) { throw 'Wrong source-ID control failed outside second-group exports' }
    }
    $meta=Get-Content "$dir/fixture-report.json" -Raw | ConvertFrom-Json
    $reports+=@{Case=$case;VertexCount=$meta.VertexCount;Groups=$meta.GroupCount;NumericalOutputsVerified=$true;ConnectivityRemapVerified=$true
        WrongUvRejected=($case -eq 'projective');WrongGroupSourceIdsRejected=($case -eq 'two-groups')
        Files=@(Get-ChildItem -LiteralPath $dir -File | ForEach-Object { @{File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })}
}
@{Scope='Original pinned BSP GS on actual GPU with source-derived GLSL inputs; exact UV/control/connectivity/guards and tolerant positions; not title execution or rendering'
    Manifest=$manifest;ManifestSha256=(Get-FileHash -LiteralPath $manifest).Hash;Cases=$reports;GpuExecuted=$true;RenderingVerified=$false
} | ConvertTo-Json -Depth 7 | Set-Content "$out/execution-report.json" -Encoding utf8
Write-Output "PASS independent original BSP compute and connectivity: $out"
