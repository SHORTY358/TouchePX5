# Copyright (C) 2026 Touché PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/native-entry-exit'
$cube=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$clang='C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe'
$exe=Join-Path $root 'artifacts/bin/Release/net10.0/win-x64/TouchePx5.exe'
New-Item -ItemType Directory -Path $out -Force | Out-Null
foreach($value in @(0,7)) {
    [IO.File]::WriteAllText("$out/exit-$value.c", "int main(void) { return $value; }")
    & $clang --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -c "$out/exit-$value.c" -o "$out/exit-$value.o"
    if($LASTEXITCODE) { throw 'Entry test compilation failed' }
    & "$cube/link_real.exe" "$out/exit-$value.elf" "$cube/crt1.o" "$out/exit-$value.o" | Out-Null
    if($LASTEXITCODE) { throw 'Entry test link failed' }
    $p=Start-Process -FilePath $exe -ArgumentList @('--cpu-engine=native','--generation=ps5',"--log-file=$out/runtime-$value.log","$out/exit-$value.elf") -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout-$value.log" -RedirectStandardError "$out/stderr-$value.log"
    if(-not $p.WaitForExit(20000)) {
        # The handle belongs to the probe created above, never another session.
        $p.Kill($true)
        throw "Owned entry probe timed out (PID $($p.Id))"
    }
    $expectedHost=if($value -eq 0) {0} else {4}
    if($p.ExitCode -ne $expectedHost) { throw "Host exit $($p.ExitCode), expected $expectedHost" }
    $log=[IO.File]::ReadAllText("$out/stderr-$value.log")
    if(-not $log.Contains("Guest returned: $value")) { throw "Guest return value $value lost" }
    if(-not $log.Contains(('reason=exit value=0x{0:X16}' -f $value))) { throw 'Explicit guest exit was not observed' }
    Write-Output "PASS: guest exit $value preserved; host exit $expectedHost"
}
