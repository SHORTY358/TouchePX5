# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([ValidateRange(10,600)][int]$TimeoutSeconds=180)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=Join-Path $repo 'artifacts/external-homebrew/ps5-xash3d-halflife'
$pin='be0731b4fcc4233349bba5e0d3ff33c79428f695'
if((& git -C $source rev-parse HEAD) -ne $pin -or (& git -C $source status --porcelain)) { throw 'Clean pinned Xash3D source required' }
# Upstream make test removes only its own Python caches. Reject a redirected
# source root before invoking that project-owned cleanup.
if((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source checkout must be a real directory' }
$hostRoot=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/cygwin-host'
$setup=Get-Content (Join-Path $hostRoot 'setup-report.json') -Raw | ConvertFrom-Json
if(-not $setup.Installed) { throw 'Existing isolated Cygwin host is required' }
$out=Join-Path $repo ('artifacts/xash3d-host/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force | Out-Null
function CygwinPath([string]$path) { '/cygdrive/'+$path.Substring(0,1).ToLower()+$path.Substring(2).Replace('\','/') }
$gitDirectory=Split-Path (Get-Command git -ErrorAction Stop).Source
$packages=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain/python/Lib/site-packages'
$body=@'
set -eu
export PATH='/usr/bin:/bin:GIT_PATH'
export PYTHONDONTWRITEBYTECODE=1
export PYTHONPATH='PYTHON_PACKAGES'
cd 'SOURCE_PATH'
gcc --version
make -B test CC=gcc CFLAGS='-O2 -std=c11 -Wall -Wextra -Werror -D_POSIX_C_SOURCE=200809L'
make audit
'@
$script=Join-Path $out 'run.sh'
[IO.File]::WriteAllText($script,$body.Replace('SOURCE_PATH',(CygwinPath $source)).Replace('GIT_PATH',(CygwinPath $gitDirectory)).Replace('PYTHON_PACKAGES',(CygwinPath $packages)).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
$report=[ordered]@{SourceUrl='https://github.com/mpereiraesaa/ps5-xash3d-halflife';SourcePin=$pin
 MakefileSha256=(Get-FileHash "$source/Makefile").Hash;Submodules=@(& git -C $source submodule status)
 Host='Existing isolated Cygwin POSIX on Windows';TestAndAuditPassed=$false;TimedOut=$false;ExitCode=$null
 GuestBuilt=$false;GuestExecuted=$false;GameAssetsUsed=$false;StartedUtc=[DateTime]::UtcNow.ToString('o')}
$process=$null
try {
 $process=Start-Process -FilePath "$($setup.Root)/bin/bash.exe" -ArgumentList @('--noprofile','--norc',('"'+$script.Replace('\','/')+'"')) -WorkingDirectory $out -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
 if(-not $process.WaitForExit($TimeoutSeconds*1000)) { $report.TimedOut=$true; throw "Host checks reached deadline: $out" }
 $report.ExitCode=$process.ExitCode
 if($process.ExitCode -ne 0) { throw "Xash3D upstream host checks failed: $out" }
 $report.TestAndAuditPassed=$true
} finally {
 if($null -ne $process -and -not $process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
 $report.FinishedUtc=[DateTime]::UtcNow.ToString('o')
 $report.SourceChangesAfter=@(& git -C $source status --porcelain)
 $report | ConvertTo-Json -Depth 5 | Set-Content "$out/report.json" -Encoding UTF8
 if($null -ne $process) { $process.Dispose() }
}
Write-Output "PASS upstream Xash3D host contracts and source audit: $out"
