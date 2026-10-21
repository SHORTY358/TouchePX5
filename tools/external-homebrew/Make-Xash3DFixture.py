# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Use upstream synthetic test data; never require or copy Valve assets."""
import hashlib
import json
from pathlib import Path
import runpy
import sys

sys.dont_write_bytecode = True
source, output = map(Path, sys.argv[1:])
source, output = source.resolve(), output.resolve()
test = source / "tests/test_bake_bsp.py"
namespace = runpy.run_path(str(test), run_name="touchepx5_source_fixture")
raw = namespace["tiny_bsp"]()
bundle = namespace["BAKER"].bake(raw)
output.mkdir(parents=True, exist_ok=True)
(output / "synthetic.bsp").write_bytes(raw)
(output / "map.ps5bsp").write_bytes(bundle)
(output / "fixture-report.json").write_text(json.dumps({
    "Scope": "Upstream synthetic BSP regression data, not a Half-Life map",
    "FactorySourceSha256": hashlib.sha256(test.read_bytes()).hexdigest(),
    "BspSha256": hashlib.sha256(raw).hexdigest(),
    "BundleSha256": hashlib.sha256(bundle).hexdigest(),
    "BspBytes": len(raw), "BundleBytes": len(bundle), "GameAssetsUsed": False,
}, indent=2), encoding="utf-8")
print(f"Prepared {len(bundle)}-byte source-derived synthetic BSP bundle")
