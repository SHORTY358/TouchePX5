# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$SkipProducer,[switch]$ColorParameters,[switch]$WrongParameterMapping)
$ErrorActionPreference='Stop'
if($WrongParameterMapping -and -not $ColorParameters) { throw 'Wrong parameter control requires the color parameter scenario' }
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root ('artifacts/native-vertex-bridge/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
$source="$PSScriptRoot/fixtures/ps5-touche-compute/compute-vertex-bridge.c"
$imports="$PSScriptRoot/fixtures/ps5-touche-compute/imports.S"
$layout="$PSScriptRoot/fixtures/ps5-touche-input-demo/demo.ld"
$clang='C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe'
$options=@()
if($SkipProducer) { $options+='-DBRIDGE_SKIP_PRODUCER=1' }
if($ColorParameters) { $options+='-DBRIDGE_PARAMETERS=1' }
if($WrongParameterMapping) { $options+='-DBRIDGE_WRONG_PARAMETER=1' }
& $clang --target=x86_64-pc-linux-gnu -O1 -ffreestanding -fno-builtin -fno-stack-protector -fpie -mno-sse @options -c $source -o "$out/bridge.o"
if($LASTEXITCODE) { throw 'Bridge compile failed' }
& $clang --target=x86_64-pc-linux-gnu -c $imports -o "$out/imports.o"
if($LASTEXITCODE) { throw 'Imports compile failed' }
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/ld.lld.exe' -T $layout -o "$out/bridge.elf" "$out/bridge.o" "$out/imports.o"
if($LASTEXITCODE) { throw 'Bridge link failed' }
$bytes=[IO.File]::ReadAllBytes("$out/bridge.elf")
$bytes[7]=9;$bytes[8]=2;$bytes[16]=0x10;$bytes[17]=0xfe
[IO.File]::WriteAllBytes("$out/bridge.elf",$bytes)
$exe="$root/artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe"
. "$PSScriptRoot/RuntimeManifest.ps1"
$settings=@{SHARPEMU_LOG_AGC='1';SHARPEMU_TRACE_GUEST_IMAGES='present';SHARPEMU_GUEST_IMAGE_DUMP_DIR=$out;SHARPEMU_GUEST_IMAGE_DUMP_CONTINUOUS='1';SHARPEMU_OVERLAY='0'}
$report=[ordered]@{Scope='Independent compute output to graphics vertex fetch; not original NGG execution';
 SourceSha256=(Get-FileHash $source).Hash;ImportsSha256=(Get-FileHash $imports).Hash;LayoutSha256=(Get-FileHash $layout).Hash;
 ElfSha256=(Get-FileHash "$out/bridge.elf").Hash;RuntimeFiles=@(Get-ToucheRuntimeManifest -Directory (Split-Path $exe));Settings=$settings;
 NativeAssertionsPassed=$false;RenderingVerified=$false;HostExit=$null;SkipProducer=[bool]$SkipProducer;ColorParameters=[bool]$ColorParameters;WrongParameterMapping=[bool]$WrongParameterMapping}
$old=@{};$process=$null
try {
 foreach($key in $settings.Keys) { $old[$key]=[Environment]::GetEnvironmentVariable($key);[Environment]::SetEnvironmentVariable($key,$settings[$key]) }
 $process=Start-Process $exe -ArgumentList @('--generation=ps5','--cpu-engine=native',('--log-file='+$out+'/runtime.log'),("$out/bridge.elf")) -WindowStyle Hidden -PassThru -RedirectStandardError "$out/stderr.log" -RedirectStandardOutput "$out/stdout.log"
 if(-not $process.WaitForExit(20000)) { $process.Kill($true);throw 'Owned bridge timeout' }
 $report.HostExit=$process.ExitCode
 $log=Get-Content "$out/stderr.log" -Raw
 $report.NativeAssertionsPassed=$process.ExitCode -eq 0 -and $log.Contains('Dispatch result: ORBIS_GEN2_OK')
 if(-not $report.NativeAssertionsPassed) { throw "Bridge native assertions failed: $out" }
 if([regex]::Matches($log,'vk.compute_dispatch groups=').Count -ne 1 -or
    [regex]::Matches($log,'vk.offscreen_draw mrt=1 size=320x180 textures=0').Count -ne 1 -or
    $log.Contains('agc.compute_semantic_fast_path')) { throw 'Expected actual GPU compute and graphics draw without CPU replacement' }
 & "$PSScriptRoot/Verify-VertexBridgePixels.ps1" -Directory $out -ColorParameters:$ColorParameters
 $report.RenderingVerified=$true
 Write-Output "Native bridge assertions pass; inspect pixels: $out"
} finally {
 if($process -and -not $process.HasExited) { $process.Kill($true) }
 foreach($key in $old.Keys) { [Environment]::SetEnvironmentVariable($key,$old[$key]) }
 $report | ConvertTo-Json -Depth 8 | Set-Content "$out/report.json" -Encoding utf8
 if($process) { $process.Dispose() }
}
