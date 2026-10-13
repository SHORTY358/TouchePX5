# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Bound one owned Windows compiler build; retain Ninja outputs for resumption."""
import json
import subprocess
import sys
import time
from pathlib import Path

root = Path(sys.argv[1]).resolve()
seconds = int(sys.argv[2])
if not 1 <= seconds <= 1800 or not (root / 'build-windows.cmd').is_file():
    raise SystemExit('Invalid bounded build parameters')
start = time.monotonic()
timeout = False
with (root / 'build-windows.log').open('w', encoding='utf-8') as log:
    process = subprocess.Popen(['cmd.exe', '/d', '/c', str(root / 'build-windows.cmd')],
                               cwd=root, stdout=log, stderr=subprocess.STDOUT)
    (root / 'active-build.json').write_text(json.dumps(dict(Pid=process.pid, MaxSeconds=seconds)), encoding='utf-8')
    try:
        process.wait(timeout=seconds)
    except subprocess.TimeoutExpired:
        timeout = True
        # Only terminate this still-live owned command and its build children.
        subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'],
                       check=True, capture_output=True)
        process.wait(timeout=30)
    finally:
        if process.poll() is None:
            subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'],
                           check=True, capture_output=True)
            process.wait(timeout=30)
        (root / 'active-build.json').unlink(missing_ok=True)
report = dict(BuildComplete=not timeout and process.returncode == 0,
              BoundedIncomplete=timeout, ExitCode=process.returncode,
              ElapsedSeconds=round(time.monotonic() - start, 1),
              Scope='amdllpc/llvm-readelf/llvm-objcopy Windows build; no Gears shader or emulator launch')
(root / 'build-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
if not timeout and process.returncode:
    raise SystemExit(process.returncode)
