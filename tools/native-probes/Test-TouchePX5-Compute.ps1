[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'C:\Dev\TouchePX5\artifacts\smoke-compute'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$fixture = Join-Path $PSScriptRoot 'fixtures\ps5-touche-compute'
$clang = 'C:\BuildTools\VC\Tools\Llvm\x64\bin\clang.exe'
& $clang --target=x86_64-pc-linux-gnu -O1 -ffreestanding -fno-builtin -fno-stack-protector -fpie -mno-sse -c (Join-Path $fixture 'compute.c') -o (Join-Path $root 'compute.o')
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
    & 'C:\Dev\TouchePX5\artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe' '--cpu-engine=native' '--fps-limit=60' '--log-level=info' "--log-file=$log" $guest
    if ($LASTEXITCODE -ne 0) { throw "Compute launch failed: $log" }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $output = if (Test-Path -LiteralPath $log) { '' + (Get-Content -LiteralPath $log -Raw) } else { '' }
        if ($output.Contains('TouchePx5 execution completed.')) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $output.Contains('[RUNTIME] DispatchEntry returned: ORBIS_GEN2_OK')) { throw "Compute guest failed: $log" }
    if ($output -notmatch 'agc.compute_shader[^\r\n]*gpu=True[^\r\n]*global_writes=True') { throw "Missing translated GPU compute dispatch: $log" }
    if ([regex]::Matches($output,'vk.compute_dispatch groups=1x1x1').Count -ne 2) { throw "Expected two Vulkan executions: $log" }
    if ($output.Contains('agc.compute_semantic_fast_path')) { throw 'Probe unexpectedly used a CPU semantic replacement.' }
    Write-Output 'PASS: Two translated compute dispatches with different inputs, EXEC masking, completion and guest-memory readback match.'
}
finally { $env:SHARPEMU_LOG_AGC = $oldAgc }
