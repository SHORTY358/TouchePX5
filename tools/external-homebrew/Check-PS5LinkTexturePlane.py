# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Validate the adapted plane's cell centers; does not certify whole images."""
import argparse
import json
import re
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture_folder", type=Path)
    args = parser.parse_args()
    paths = sorted(args.capture_folder.glob("present-*.bgra"))
    if not paths:
        raise SystemExit("No presented captures")
    path = paths[-1]
    match = re.fullmatch(r"present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm.bgra", path.name)
    if not match:
        raise SystemExit("Expected BGRA8 UNORM capture")
    width, height = map(int, match.groups())
    pixels = path.read_bytes()
    if len(pixels) != width * height * 4:
        raise SystemExit("Capture length does not match dimensions")
    mismatches = []
    # The clip-space plane spans +/-0.8. Normalized UV spans [0,1],
    # with V=0 at the bottom; sample well away from checker and quadrant edges.
    for cell_y in range(8):
        for cell_x in range(8):
            x = round(width * (.1 + .8 * (cell_x + .5) / 8))
            y = round(height * (.9 - .8 * (cell_y + .5) / 8))
            value = 48 if (cell_x + cell_y) & 1 else 255
            red = value if (cell_x < 4 if cell_y < 4 else cell_x >= 4) else 0
            green = value if cell_x >= 4 else 0
            blue = value if cell_y >= 4 and cell_x < 4 else 0
            expected = (blue, green, red, 255)
            offset = (y * width + x) * 4
            actual = tuple(pixels[offset:offset + 4])
            if actual != expected:
                mismatches.append({"Cell": [cell_x, cell_y], "Pixel": [x, y],
                                   "ExpectedBgra": expected, "ActualBgra": actual})
    report = {
        "Input": str(path.resolve()),
        "SampledCellCenters": 64,
        "Mismatches": mismatches,
        "TextureCellColorsVerified": not mismatches,
        "VisualCorrectnessAutomaticallyVerified": False,
    }
    (args.capture_folder / "texture-cell-report.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8")
    print(f"Texture cell centers: {64 - len(mismatches)}/64 match")
    if mismatches:
        raise SystemExit("Texture cell validation failed; inspect the report and captures")


if __name__ == "__main__":
    main()
