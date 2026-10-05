# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = Join-Path $root ('artifacts\payload-bootstrap\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$llvm = 'C:\BuildTools\VC\Tools\Llvm\x64\bin'
$exe = Join-Path $root 'artifacts\bin\Release\net10.0\win-x64\TouchePx5.exe'
$cases = @()
foreach ($name in @('resolver','syscall')) {
    $object = Join-Path $output "$name.o"
    $elf = Join-Path $output "$name.elf"
    $log = Join-Path $output "$name.log"
    $defines = @()
    if ($name -eq 'syscall') { $defines = @('-DPROBE_SYSCALL=1') }
    & (Join-Path $llvm 'clang.exe') --target=x86_64-pc-linux-gnu @defines -c (Join-Path $PSScriptRoot 'fixtures\payload-bootstrap.S') -o $object
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
    & (Join-Path $llvm 'ld.lld.exe') -T (Join-Path $PSScriptRoot 'fixtures\payload-bootstrap.ld') -o $elf $object
    if ($LASTEXITCODE -ne 0) { throw 'Fixture link failed' }
    $bytes = [IO.File]::ReadAllBytes($elf)
    $bytes[7] = 9 # FreeBSD, matching the actual third-party payload.
    [IO.File]::WriteAllBytes($elf,$bytes)
    $process = $null
    try {
        $process = Start-Process -FilePath $exe -ArgumentList @('--cpu-engine=native',('--log-file="' + $log + '"'),('"' + $elf + '"')) `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output "$name.stdout.log") `
            -RedirectStandardError (Join-Path $output "$name.stderr.log")
        if (-not $process.WaitForExit(20000)) { throw "Fixture $name timed out" }
        $text = Get-Content -LiteralPath $log -Raw
        if ($name -eq 'resolver') {
            if ($process.ExitCode -ne 0 -or -not $text.Contains('Guest returned: 0') -or
                -not $text.Contains('Result=ORBIS_GEN2_OK')) { throw 'Payload resolver ABI checks failed' }
        } else {
            if ($process.ExitCode -ne 4 -or
                -not $text.Contains('Unsupported payload syscall 0x289: rdi=0x2 rsi=0x8 rdx=0x12345678 r10=0xABCDEF') -or
                -not $text.Contains('reason=unsupported_payload_syscall') -or
                -not $text.Contains('Result=ORBIS_GEN2_ERROR_CPU_TRAP')) { throw 'Payload syscall register/exit checks failed' }
        }
        $cases += [ordered]@{ Case=$name; Passed=$true; ExitCode=$process.ExitCode; Log=$log }
    } finally {
        if ($null -ne $process) {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
            $process.Dispose()
        }
    }
}
$cases | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'report.json') -Encoding utf8
Write-Output "PASS: payload resolver ABI and syscall boundary. Evidence: $output"
