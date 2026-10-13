# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Require stable first-object cells while the second texture changes palettes."""
import argparse
import importlib.util
import json
import re
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    'bindings', Path(__file__).with_name('Check-PS5LinkTextureBindings.py'))
bindings = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bindings)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture_folder', type=Path)
    parser.add_argument('--require-complete-first-frame', action='store_true')
    args = parser.parse_args()
    results = []
    for path in sorted(args.capture_folder.glob('present-*.bgra')):
        match = re.fullmatch(r'present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm.bgra', path.name)
        if not match:
            raise ValueError(f'Unsupported capture: {path}')
        dimensions = tuple(map(int, match.groups()))
        pixels = path.read_bytes()
        if len(pixels) != dimensions[0] * dimensions[1] * 4:
            raise ValueError(f'Truncated capture: {path}')
        counts = [bindings.cell_matches(dimensions, pixels, phase) for phase in range(3)]
        phase = next((i for i, count in enumerate(counts) if count == [64, 64]), None)
        blank = not results and all(pixels[i:i+3] == pixels[:3] for i in range(0, len(pixels), 4))
        results.append({'Input': str(path.resolve()), 'MatchesBySecondPalette': counts,
                        'SecondPalette': phase, 'InitialUniformBackground': blank})
    sequence = [r['SecondPalette'] for r in results if r['SecondPalette'] is not None]
    # Capture 1 and presentation 150 can both precede the first palette change.
    # Allow repeated palettes, but require cyclic order whenever it changes.
    ordered = all(b == a or b == (a + 1) % 3 for a, b in zip(sequence, sequence[1:]))
    passed = {0, 2}.issubset(sequence) and ordered and all(
        r['SecondPalette'] is not None or r['InitialUniformBackground'] for r in results)
    first_complete = bool(results) and results[0]['SecondPalette'] is not None
    passed = passed and (first_complete or not args.require_complete_first_frame)
    report = {'Captures': results, 'SecondPaletteSequence': sequence,
              'FirstFrameComplete': first_complete,
              'CompleteFirstFrameRequired': args.require_complete_first_frame,
              'FirstTextureStableAndSecondUpdated': passed,
              'Scope': '64 cell centers per object per nonblank captured frame',
              'VisualCorrectnessAutomaticallyVerified': False}
    (args.capture_folder / 'independent-texture-updates-report.json').write_text(
        json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    if not passed:
        raise SystemExit('Independent texture update validation failed')


if __name__ == '__main__':
    main()
