[CmdletBinding()]
param([switch]$IndirectDimensions, [switch]$BaseRelative)
$ErrorActionPreference = 'Stop'
if($BaseRelative) { $IndirectDimensions=$true }
$root = 'C:\Dev\TouchePX5\artifacts\smoke-compute'
if($IndirectDimensions) { $root += '-indirect-dimensions' }
if($BaseRelative) { $root += '-base-relative' }
New-Item -ItemType Directory -Path $root -Force | Out-Null
$fixture = Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$clang = 'C:\BuildTools\VC\Tools\Llvm\x64\bin\clang.exe'
$source=if($IndirectDimensions) { 'compute-indirect.c' } else { 'compute.c' }
$fixtureOptions=@(if($BaseRelative) { '-DINDIRECT_BASE_RELATIVE=1' })
& $clang --target=x86_64-pc-linux-gnu -O1 -ffreestanding -fno-builtin -fno-stack-protector -fpie -mno-sse @fixtureOptions -c (Join-Path $fixture $source) -o (Join-Path $root 'compute.o')
if ($LASTEXITCODE -ne 0) { throw 'Compute guest compilation failed.' }
& $clang --target=x86_64-pc-linux-gnu -c (Join-Path $fixture 'imports.S') -o (Join-Path $root 'imports.o')
if ($LASTEXITCODE -ne 0) { throw 'Compute imports compilation failed.' }
$guest = Join-Path $root 'compute.elf'
& 'C:\BuildTools\VC\Tools\Llvm\x64\bin\ld.lld.exe' -T (Join-Path $PSScriptRoot 'fixtures\ps5-touche-input-demo\demo.ld') -o $guest (Join-Path $root 'compute.o') (Join-Path $root 'imports.o')
if ($LASTEXITCODE -ne 0) { throw 'Compute guest link failed.' }
$bytes = [IO.File]::ReadAllBytes($guest)
$bytes[7]=9; $bytes[8]=2; $bytes[16]=0x10; $bytes[17]=0xfe
[IO.File]::WriteAllBytes($guest,$bytes)
$log = Join-Path $root 'compute.log'
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Write-Output "Log: $log"
$oldAgc = $env:SHARPEMU_LOG_AGC
try {
    $env:SHARPEMU_LOG_AGC = '1'
    $guestProcess=Start-Process -FilePath 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe' -ArgumentList @('--cpu-engine=native','--fps-limit=60','--log-level=info',"--log-file=$log",$guest) -WindowStyle Hidden -PassThru
    try {
        if(-not $guestProcess.WaitForExit(60000)) {
            $guestProcess.Kill()
            $guestProcess.WaitForExit(5000) | Out-Null
            throw "Compute guest timed out: $log"
        }
        if($guestProcess.ExitCode -ne 0) { throw "Compute launch failed ($($guestProcess.ExitCode)): $log" }
    } finally { $guestProcess.Dispose() }
    $output = if (Test-Path -LiteralPath $log) { '' + (Get-Content -LiteralPath $log -Raw) } else { '' }
    if (-not $output.Contains('[RUNTIME] DispatchEntry returned: ORBIS_GEN2_OK')) { throw "Compute guest failed: $log" }
    if ($output -notmatch 'agc.compute_shader[^\r\n]*gpu=True[^\r\n]*global_writes=True') { throw "Missing translated GPU compute dispatch: $log" }
    if($IndirectDimensions) {
        $expectedX=if($BaseRelative) { 1 } else { 2 }
        $expectedY=if($BaseRelative) { 1 } else { 0 }
        if([regex]::Matches($output,'vk.compute_dispatch groups=').Count -ne 8 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x1x1').Count -ne 6 -or
           [regex]::Matches($output,'vk.compute_dispatch groups=2x1x1').Count -ne $expectedX -or
           [regex]::Matches($output,'vk.compute_dispatch groups=1x2x1').Count -ne $expectedY) { throw "Unexpected indirect dispatch sizes: $log" }
    } elseif ([regex]::Matches($output,'vk.compute_dispatch groups=1x1x1').Count -ne 2) { throw "Expected two Vulkan executions: $log" }
    if ($output.Contains('agc.compute_semantic_fast_path')) { throw 'Probe unexpectedly used a CPU semantic replacement.' }
    if($IndirectDimensions) { Write-Output "PASS: Atomic baseline and GPU-produced indirect dimensions (1 -> 2, 2 -> 1, 0 -> 2, 2 -> 0), completion and buffer guards match. BaseRelative=$BaseRelative" }
    else { Write-Output 'PASS: Two translated compute dispatches with different inputs, EXEC masking, completion and guest-memory readback match.' }
}
finally { $env:SHARPEMU_LOG_AGC = $oldAgc }
