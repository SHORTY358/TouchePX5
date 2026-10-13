# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Check distinct left/right native producer palettes at known capture times."""
import argparse
import json
import re
from pathlib import Path

# Expected RGB cells, independently specified rather than reading guest memory.
PALETTES = [
    [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0)],
    [(0, 0, 255), (255, 0, 0), (0, 255, 0), (255, 0, 255)],
    [(0, 255, 0), (0, 0, 255), (255, 0, 0), (0, 255, 255)],
]


def matches(width, height, pixels, left, right):
    counts = [0, 0, 0]  # Immutable object; mutable left; mutable right.
    for plane in range(2):
        for cy in range(8):
            for cx in range(8):
                phase = 0 if plane == 0 else (left if cx < 4 else right)
                color = PALETTES[phase][(2 if cy >= 4 else 0) + (1 if cx >= 4 else 0)]
                if (cx + cy) & 1:
                    color = tuple(48 if component else 0 for component in color)
                x = round(width * ((.075 if plane == 0 else .525) + .4 * (cx + .5) / 8))
                y = round(height * (.9 - .8 * (cy + .5) / 8))
                offset = (y * width + x) * 4
                if tuple(pixels[offset:offset + 4]) == (*reversed(color), 255):
                    counts[0 if plane == 0 else (1 if cx < 4 else 2)] += 1
    return counts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture_folder', type=Path)
    args = parser.parse_args()
    runtime = json.loads((args.capture_folder / 'report.json').read_text(encoding='utf-8-sig'))
    if not runtime['IndependentProducerPalettes'] or runtime['CaptureEvery'] != 100:
        raise ValueError('Independent-column fixture and capture interval 100 required')
    captures = sorted(args.capture_folder.glob('present-*.bgra'))
    if len(captures) != 7:
        raise ValueError('All seven captures required')
    results = []
    for index, path in enumerate(captures):
        shape = re.fullmatch(r'present-(\d+)-(\d+)x(\d+)-B8G8R8A8Unorm.bgra', path.name)
        if not shape:
            raise ValueError(f'Unknown capture format: {path}')
        capture_index, width, height = map(int, shape.groups())
        if capture_index != index + 1:
            raise ValueError('Capture sequence is incomplete')
        pixels = path.read_bytes()
        if len(pixels) != width * height * 4:
            raise ValueError(f'Truncated capture: {path}')
        frame = 0 if index == 0 else index * 100 - 1
        expected = [(1 + frame // 150) % 3, (1 + frame // 100) % 3]
        counts = matches(width, height, pixels, *expected)
        alternatives = [[l, r] for l in range(3) for r in range(3)
                        if matches(width, height, pixels, l, r) == [64, 32, 32]]
        results.append(dict(Input=str(path.resolve()), SourceFrame=frame,
                            ExpectedPalettes=expected, MatchingCells=counts,
                            CompletePalettePairs=alternatives))
    passed = all(r['MatchingCells'] == [64, 32, 32] and
                 r['CompletePalettePairs'] == [r['ExpectedPalettes']] for r in results)
    report = dict(Passed=passed, Captures=results,
                  Scope='64 immutable cells and 32 cells per mutable producer region in all seven captures',
                  VisualCorrectnessAutomaticallyVerified=False)
    (args.capture_folder / 'producer-region-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    if not passed:
        raise SystemExit('Producer region palette validation failed')


if __name__ == '__main__':
    main()
