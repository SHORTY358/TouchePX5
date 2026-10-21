# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory)][string]$NativeRun,[Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$native=(Resolve-Path -LiteralPath $NativeRun).Path
$manifest=(Resolve-Path -LiteralPath $ManifestPath).Path
$out=Join-Path $repo ('artifacts/xash3d-native-compute/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$dotnet='C:/Dev/dotnet10/dotnet.exe'
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release -- --native-bsp-compute $native $manifest $out
if($LASTEXITCODE -ne 0) { throw 'Captured native BSP preparation failed' }
# Integrity negative control: change one owned copy of a captured input while
# keeping the original execution report unchanged. Original evidence is retained.
$bad=Join-Path $out 'corrupted-capture'
Copy-Item -LiteralPath $native -Destination $bad -Recurse
$badReplay=Join-Path $bad 'replay/draw-000002/0000000210001000.replay.json'
$badText=[IO.File]::ReadAllText($badReplay)
if(-not $badText.Contains('"Schema": 1')) { throw 'Review changed native replay schema' }
[IO.File]::WriteAllText($badReplay,$badText.Replace('"Schema": 1','"Schema": 2'))
& $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --native-bsp-compute $bad $manifest "$out/corrupted-capture-output" > "$out/corrupted-capture.log" 2>&1
if($LASTEXITCODE -eq 0 -or -not (Get-Content "$out/corrupted-capture.log" -Raw).Contains('Native evidence hash changed')) { throw 'Changed capture did not reject by hash' }
$cases=@()
foreach($case in @('native-clear','native-world')) {
    $dir=Join-Path $out $case
    $hashes=[ordered]@{}
    foreach($file in @('original-bsp-compute.spv','expected.json','initial.json','limits.json','storage-fixture.json','fixture-report.json')) { $hashes[$file]=(Get-FileHash "$dir/$file").Hash }
    & $dotnet run --project "$repo/tools/SharpEmu.Tools.GpuConformance" -c Release --no-build -- "$dir/original-bsp-compute.spv" "$dir/expected.json" "$dir/actual.json" "$dir/initial.json" "$dir/limits.json" "$dir/storage-fixture.json" > "$dir/gpu.log" 2>&1
    if($LASTEXITCODE -ne 0 -or -not (Get-Content "$dir/gpu.log" -Raw).Contains('RESULT: all values match')) { throw "Native captured BSP GS mismatch: $dir" }
    foreach($file in @($hashes.Keys)) { if($hashes[$file] -ne (Get-FileHash "$dir/$file").Hash) { throw 'GPU input changed during execution' } }
    $hashes['actual.json']=(Get-FileHash "$dir/actual.json").Hash
    @{NumericalOutputsVerified=$true;Hashes=$hashes} | ConvertTo-Json -Depth 4 | Set-Content "$dir/gpu-result.json" -Encoding utf8
    & $dotnet run --project "$repo/tools/SharpEmu.Tools.NativeShaderAudit" -c Release --no-build -- --consume-native-bsp $dir
    if($LASTEXITCODE -ne 0) { throw 'Native captured BSP remap mismatch' }
    if($case -eq 'native-world') {
        # Wrong shared-vertex connectivity, not a changed reference mesh.
        # Update only the input source word in expected storage, leaving the
        # independently expected primitive exports untouched.
        $fixture=Get-Content "$dir/storage-fixture.json" -Raw | ConvertFrom-Json
        $meta=Get-Content "$dir/fixture-report.json" -Raw | ConvertFrom-Json
        $expected=Get-Content "$dir/expected.json" -Raw | ConvertFrom-Json
        $word=[int]$meta.SourceFirstWord+9
        $wrong=0u -bor (1u -shl 10) -bor (3u -shl 20)
        $fixture.Buffers[$meta.OutputBinding][$word]=$wrong;$expected[$word]=$wrong
        $fixture | ConvertTo-Json -Depth 7 -Compress | Set-Content "$dir/wrong-connectivity-fixture.json" -Encoding utf8
        ConvertTo-Json -InputObject $expected -Compress | Set-Content "$dir/wrong-connectivity-expected.json" -Encoding utf8
        & $dotnet run --project "$repo/tools/SharpEmu.Tools.GpuConformance" -c Release --no-build -- "$dir/original-bsp-compute.spv" "$dir/wrong-connectivity-expected.json" "$dir/wrong-connectivity-actual.json" "$dir/initial.json" "$dir/limits.json" "$dir/wrong-connectivity-fixture.json" > "$dir/wrong-connectivity.log" 2>&1
        $code=$LASTEXITCODE;$text=Get-Content "$dir/wrong-connectivity.log" -Raw
        if($code -ne 1 -or -not $text.Contains('driver accepted the SPIR-V') -or -not $text.Contains('mismatch(es)')) { throw 'Wrong native shared-vertex topology did not reject' }
        $failures=@(Select-String "$dir/wrong-connectivity.log" -Pattern '^FAIL  bounded export word (\d+):' | ForEach-Object { [int]$_.Matches[0].Groups[1].Value })
        if($failures.Count -eq 0 -or @($failures | Where-Object { $_ -lt 1541 -or $_ -ge 1546 }).Count) { throw 'Connectivity control failed outside changed primitive exports' }
    }
    $cases+=@{Case=$case;NumericalOutputsVerified=$true;ConnectivityVerified=$true;WrongSharedVertexConnectivityRejected=($case -eq 'native-world')
        Files=@(Get-ChildItem -LiteralPath $dir -File | ForEach-Object { @{Path=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })}
}
@{Scope='Original BSP GS on captured native clear/world inputs with independent GLSL equations and explicit indexed reuse transport; not native scene rendering'
    NativeRun=$native;Manifest=$manifest;CapturedOriginalInputs=$true;GpuExecuted=$true;NativeRenderingVerified=$false;ChangedCaptureHashRejected=$true;Cases=$cases
} | ConvertTo-Json -Depth 7 | Set-Content "$out/execution-report.json" -Encoding utf8
Write-Output "PASS original GS on captured native indexed BSP inputs: $out"
