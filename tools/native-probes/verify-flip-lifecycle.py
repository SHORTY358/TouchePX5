# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later

"""Verify actual swapchain readbacks independently of guest status/event checks."""
import json
import re
import sys
from pathlib import Path

log, mode, output = sys.argv[1:]
captures = re.findall(r"vk.swapchain_dump path=([^\r\n]+)", Path(log).read_text(encoding="utf-8-sig"))
expected = [(255, 0, 0), (0, 255, 0), (0, 0, 255)] if mode in ("cpu", "burst") else [(32, 32, 32), (255, 0, 0), (0, 0, 255)]
if mode == "burst":
    expected += [(255,255,0), (0,255,255), (255,0,255)]
assert len(captures) == len(expected), f"Expected {len(expected)} presented frames, found {len(captures)}"
checks = []
for name, rgb in zip(captures, expected):
    match = re.search(r"-(\d+)x(\d+)-(\w+)\.bgra$", name)
    assert match, name
    w, h = map(int, match.group(1, 2))
    data = Path(name).read_bytes()
    assert len(data) == w * h * 4
    assert match.group(3) in ("B8G8R8A8Unorm", "R8G8B8A8Unorm"), match.group(3)
    # A grid inside the triangle, well away from its edges. CPU solid-color
    # frames also check all four corners so a small correct patch cannot pass.
    positions = [(w*x//20, h*y//20) for x in range(4,7) for y in range(8,12)]
    if mode in ("cpu", "burst"):
        positions += [(0,0), (w-1,0), (0,h-1), (w-1,h-1)]
    for x, y in positions:
        offset = (y*w+x)*4
        pixel = tuple(data[offset:offset+3])
        observed = pixel[::-1] if match.group(3).startswith("B8") else pixel
        assert observed == rgb, (name, x, y, observed, rgb)
    checks.append({"capture": name, "rgb": rgb, "checked_pixels": len(positions)})
Path(output).write_text(json.dumps({"mode": mode, "frames": checks, "passed": True}, indent=2), encoding="utf-8")
print(f"PASS: {len(checks)} independently verified displayed colors")
