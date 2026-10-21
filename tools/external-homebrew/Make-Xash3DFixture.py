# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Use upstream synthetic test data; never require or copy Valve assets."""
import hashlib
import json
from pathlib import Path
import runpy
import sys
import math
import struct

sys.dont_write_bytecode = True
if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] != "--look-down-camera"):
    raise SystemExit("usage: Make-Xash3DFixture.py <source> <output> [--look-down-camera]")
source, output = map(Path, sys.argv[1:3])
source, output = source.resolve(), output.resolve()
test = source / "tests/test_bake_bsp.py"
namespace = runpy.run_path(str(test), run_name="touchepx5_source_fixture")
raw = namespace["tiny_bsp"]()
bundle = namespace["BAKER"].bake(raw)
original_bundle_hash = hashlib.sha256(bundle).hexdigest()
look_down = len(sys.argv) == 4
if look_down:
    # Explicit diagnostic camera overlay in the bundle HEADER only. Keep all
    # original synthetic geometry/textures and its payload CRC unchanged.
    # The regression map's horizontal spawn is above a tiny floor; a 60-degree
    # downward view supplies a visible scene without changing emulator math.
    bundle = bytearray(bundle)
    struct.pack_into("<3f", bundle, 36, 0.0, -math.sqrt(3.0) / 2.0, -0.5)
    bundle = bytes(bundle)
output.mkdir(parents=True, exist_ok=True)
(output / "synthetic.bsp").write_bytes(raw)
(output / "map.ps5bsp").write_bytes(bundle)
(output / "fixture-report.json").write_text(json.dumps({
    "Scope": "Upstream synthetic BSP regression data, not a Half-Life map",
    "FactorySourceSha256": hashlib.sha256(test.read_bytes()).hexdigest(),
    "BspSha256": hashlib.sha256(raw).hexdigest(),
    "BundleSha256": hashlib.sha256(bundle).hexdigest(),
    "BspBytes": len(raw), "BundleBytes": len(bundle), "GameAssetsUsed": False,
    "OriginalBundleSha256": original_bundle_hash,
    "CameraOverlay": "look down 60 degrees; header direction only" if look_down else None,
}, indent=2), encoding="utf-8")
print(f"Prepared {len(bundle)}-byte source-derived synthetic BSP bundle")
