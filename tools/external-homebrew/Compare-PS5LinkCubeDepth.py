# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Check the captured depth-order relationship, not general visual correctness."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def capture(folder):
    frames = sorted(folder.glob("present-*.bgra"))
    if not frames:
        raise ValueError(f"No presented captures: {folder}")
    path = frames[-1]
    match = re.fullmatch(r"present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm.bgra", path.name)
    if not match:
        raise ValueError(f"Unsupported capture format: {path.name}")
    dimensions = tuple(map(int, match.groups()))
    pixels = path.read_bytes()
    if len(pixels) != dimensions[0] * dimensions[1] * 4:
        raise ValueError(f"Truncated capture: {path}")
    return path, dimensions, pixels


def differing_pixels(a, b):
    return sum(a[i:i + 4] != b[i:i + 4] for i in range(0, len(a), 4))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("enabled", "enabled_reverse", "disabled", "disabled_reverse"):
        parser.add_argument(name, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    captures = [capture(getattr(args, name)) for name in
                ("enabled", "enabled_reverse", "disabled", "disabled_reverse")]
    if len({dimensions for _, dimensions, _ in captures}) != 1:
        raise ValueError("Capture dimensions differ; rerun with a fixed window size")
    buffers = [pixels for _, _, pixels in captures]
    enabled = differing_pixels(buffers[0], buffers[1])
    disabled = differing_pixels(buffers[2], buffers[3])
    control = differing_pixels(buffers[0], buffers[2])
    passed = enabled == 0 and disabled > 1000 and control > 1000
    report = {
        "Inputs": [str(path.resolve()) for path, _, _ in captures],
        "Sha256": [hashlib.sha256(pixels).hexdigest() for pixels in buffers],
        "DepthEnabledReversedOrderDifferingPixels": enabled,
        "DepthDisabledReversedOrderDifferingPixels": disabled,
        "EnabledVersusDisabledDifferingPixels": control,
        "DepthOrderRelationshipPassed": passed,
        "VisualCorrectnessAutomaticallyVerified": False,
    }
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    if not passed:
        raise SystemExit("Depth-order comparison failed; inspect the captures")


if __name__ == "__main__":
    main()
