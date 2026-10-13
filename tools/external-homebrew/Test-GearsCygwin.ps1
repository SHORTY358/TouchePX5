# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(10,600)][int]$TimeoutSeconds=120)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$gears=Join-Path $repo 'artifacts/external-homebrew/ps5-agc-gears'
$hostRoot=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/cygwin-host'
$setup=Get-Content "$hostRoot/setup-report.json" -Raw | ConvertFrom-Json
if(-not $setup.Installed) { throw 'Complete the isolated Cygwin setup first' }
$pin='1ae1f9182abd2770c131b97419034fb85173c2dc'
if((& git -C $gears rev-parse HEAD) -ne $pin -or (& git -C $gears status --porcelain)) { throw 'Clean pinned upstream source required' }
$out=Join-Path $hostRoot ('tests/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
$script=Join-Path $out 'run.sh'
$cygGears='/cygdrive/'+$gears.Substring(0,1).ToLower()+$gears.Substring(2).Replace('\','/')
$gitDirectory=Split-Path (Get-Command git -ErrorAction Stop).Source
$cygGit='/cygdrive/'+$gitDirectory.Substring(0,1).ToLower()+$gitDirectory.Substring(2).Replace('\','/')
$pythonPackages=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/python/Lib/site-packages'
if(-not(Test-Path "$pythonPackages/yaml/__init__.py")) { throw 'Run Setup-GearsPython.ps1 to provide the pinned PyYAML package first' }
$cygPythonPackages='/cygdrive/'+$pythonPackages.Substring(0,1).ToLower()+$pythonPackages.Substring(2).Replace('\','/')
$body=@'
set -eu
export PATH='/usr/bin:/bin:GIT_PATH'
export PYTHONDONTWRITEBYTECODE=1
export PYTHONPATH='PYTHON_PACKAGES'
cd 'GEARS_PATH'
uname -a
gcc --version
python3 -c 'import yaml; print("PyYAML", yaml.__version__)'
# Strict C11 hides POSIX mkstemp unless the standard feature set is selected.
# Keep every upstream test/assertion and -Werror; do not substitute APIs.
make -B test CC=gcc CFLAGS='-O2 -std=c11 -Wall -Wextra -Werror -D_POSIX_C_SOURCE=200809L'
make audit
'@
[IO.File]::WriteAllText($script,$body.Replace('GEARS_PATH',$cygGears).Replace('GIT_PATH',$cygGit).Replace('PYTHON_PACKAGES',$cygPythonPackages).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
$report=[ordered]@{GearsPin=$pin; Host='Cygwin POSIX on Windows, not Linux';
 MakefileSha256=(Get-FileHash "$gears/Makefile").Hash;
 InstallerSha512=$setup.InstallerSha512; InstalledPackagesSha256=(Get-FileHash "$($setup.Root)/etc/setup/installed.db").Hash;
 CFlags='-O2 -std=c11 -Wall -Wextra -Werror -D_POSIX_C_SOURCE=200809L';
 TestCompleted=$false; AuditCompleted=$false; GuestExecuted=$false; TimedOut=$false; ExitCode=$null}
$process=$null
try {
 $process=Start-Process -FilePath "$($setup.Root)/bin/bash.exe" -ArgumentList @('--noprofile','--norc',('"'+$script.Replace('\','/')+'"')) -WorkingDirectory $out -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
 if(-not $process.WaitForExit($TimeoutSeconds*1000)) { $report.TimedOut=$true; throw 'Owned upstream host test reached its timeout' }
 $report.ExitCode=$process.ExitCode
 if($process.ExitCode) { throw "Upstream host verification failed with exit $($process.ExitCode); see $out" }
 $report.TestCompleted=$true; $report.AuditCompleted=$true
} finally {
 if($null -ne $process -and -not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
 $report | ConvertTo-Json -Depth 4 | Set-Content "$out/report.json" -Encoding UTF8
 if($report.TestCompleted -and $report.AuditCompleted) { Copy-Item "$out/report.json" "$hostRoot/host-verification-report.json" }
 if($null -ne $process) { $process.Dispose() }
}
Write-Output "Upstream verification evidence: $out"
