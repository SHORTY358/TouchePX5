# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Check captured palette changes at fixed plane cell centers."""
import argparse
import json
import re
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture_folder', type=Path)
    args = parser.parse_args()
    # RGB quadrant oracles for the three channel-rotation phases, bottom row first.
    palettes = [
        [(1, 0, 0), (0, 1, 0), (0, 0, 1), (1, 1, 0)],
        [(0, 0, 1), (1, 0, 0), (0, 1, 0), (1, 0, 1)],
        [(0, 1, 0), (0, 0, 1), (1, 0, 0), (0, 1, 1)],
    ]
    results = []
    for path in sorted(args.capture_folder.glob('present-*.bgra')):
        match = re.fullmatch(r'present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm.bgra', path.name)
        if not match:
            raise ValueError(f'Unsupported capture: {path}')
        width, height = map(int, match.groups())
        pixels = path.read_bytes()
        if len(pixels) != width * height * 4:
            raise ValueError(f'Truncated capture: {path}')
        matches = [0, 0, 0]
        for cy in range(8):
            for cx in range(8):
                x = round(width * (.1 + .8 * (cx + .5) / 8))
                y = round(height * (.9 - .8 * (cy + .5) / 8))
                offset = (y * width + x) * 4
                actual = tuple(pixels[offset:offset + 4])
                brightness = 48 if (cx + cy) & 1 else 255
                quadrant = (2 if cy >= 4 else 0) + (1 if cx >= 4 else 0)
                for phase, palette in enumerate(palettes):
                    r, g, b = palette[quadrant]
                    if actual == (b * brightness, g * brightness, r * brightness, 255):
                        matches[phase] += 1
        phase = next((i for i, count in enumerate(matches) if count == 64), None)
        # The backend may emit one initial uniform background without geometry.
        initial_blank = not results and all(pixels[i:i+3] == pixels[:3]
                                          for i in range(0, len(pixels), 4))
        results.append({'Input': str(path.resolve()), 'MatchesPerPhase': matches,
                        'Phase': phase, 'InitialBlank': initial_blank})
    phases = {r['Phase'] for r in results if r['Phase'] is not None}
    sequence = [r['Phase'] for r in results if r['Phase'] is not None]
    ordered = all(b == (a + 1) % 3 for a, b in zip(sequence, sequence[1:]))
    passed = {1, 2}.issubset(phases) and ordered and all(
        r['Phase'] is not None or r['InitialBlank'] for r in results)
    report = {'Captures': results, 'PaletteSequence': sequence,
              'PaletteSequenceOrdered': ordered, 'TextureUpdatesVerified': passed,
              'Scope': '64 cell centers per nonblank captured frame; one linear allocation',
              'VisualCorrectnessAutomaticallyVerified': False}
    (args.capture_folder / 'texture-updates-report.json').write_text(
        json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    if not passed:
        raise SystemExit('Texture update validation failed: stale or incorrect cell colors')


if __name__ == '__main__':
    main()
