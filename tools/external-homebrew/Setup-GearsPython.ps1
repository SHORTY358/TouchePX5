# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$python=Join-Path $out 'python/Scripts/python.exe'
if(-not (Test-Path $python)) {
    & py -3 -m venv "$out/python"
    if($LASTEXITCODE) { throw 'Gears Python environment creation failed' }
}
$version=& $python -c 'import importlib.metadata; print(importlib.metadata.version("PyYAML"))' 2>$null
if($LASTEXITCODE -ne 0 -or $version -ne '6.0.3') {
    & $python -m pip install --disable-pip-version-check --only-binary=:all: --no-deps PyYAML==6.0.3 > "$out/python-install.log" 2>&1
    if($LASTEXITCODE) { throw 'Isolated PyYAML installation failed' }
}
& $python -c 'import yaml; assert yaml.__version__ == "6.0.3"; print("PASS: isolated Gears PyYAML 6.0.3")'
if($LASTEXITCODE) { throw 'Gears PyYAML version verification failed' }
