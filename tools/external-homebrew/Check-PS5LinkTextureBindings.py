# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Validate per-draw textures on two separated, transformed native planes."""
import argparse
import json
from pathlib import Path
import importlib.util

# Reuse the format/length checks from the existing capture reader.
spec = importlib.util.spec_from_file_location(
    'depth_capture', Path(__file__).with_name('Compare-PS5LinkCubeDepth.py'))
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)


def cell_matches(dimensions, pixels):
    width, height = dimensions
    palettes = [
        [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0)],
        [(0, 0, 255), (255, 0, 0), (0, 255, 0), (255, 0, 255)],
    ]
    matches = [0, 0]
    for plane in range(2):
        for cy in range(8):
            for cx in range(8):
                x = round(width * ((.075 if plane == 0 else .525) + .4 * (cx+.5)/8))
                y = round(height * (.9 - .8 * (cy+.5)/8))
                r, g, b = palettes[plane][(2 if cy >= 4 else 0)+(1 if cx >= 4 else 0)]
                if (cx + cy) & 1:
                    r, g, b = (48 if c else 0 for c in (r, g, b))
                offset = (y*width+x)*4
                if tuple(pixels[offset:offset+4]) == (b, g, r, 255):
                    matches[plane] += 1
    return matches


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('forward', 'reverse', 'shared_control'):
        parser.add_argument(name, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    captures = [reader.capture(getattr(args, name)) for name in
                ('forward', 'reverse', 'shared_control')]
    if len({dimensions for _, dimensions, _ in captures}) != 1:
        raise ValueError('Capture dimensions differ')
    counts = [cell_matches(dimensions, pixels) for _, dimensions, pixels in captures]
    order_diff = reader.differing_pixels(captures[0][2], captures[1][2])
    control_diff = reader.differing_pixels(captures[0][2], captures[2][2])
    passed = counts[:2] == [[64, 64], [64, 64]] and counts[2] == [64, 0] and (
        order_diff == 0 and control_diff > 1000)
    report = {'Inputs': [str(path.resolve()) for path, _, _ in captures],
              'MatchingCellsPerObject': counts, 'ReversedOrderDifferingPixels': order_diff,
              'SharedControlDifferingPixels': control_diff,
              'DistinctPerDrawTexturesVerified': passed,
              'VisualCorrectnessAutomaticallyVerified': False}
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    if not passed:
        raise SystemExit('Per-draw texture binding validation failed')


if __name__ == '__main__':
    main()
