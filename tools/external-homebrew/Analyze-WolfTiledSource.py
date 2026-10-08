"""Reconstruct bounded HUD samples from captured guest bytes, not live memory.

Altered Python tile-address equation from public SDL_ps5tilemap.c.
Copyright (C) 2026 John Tornblom <john.tornblom@gmail.com>.
This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from
the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path


def pixel_offset(x, y, width):
    a, b = x % 512, y % 128
    bits = [a & 1, (a >> 1) & 1, b & 1, (b >> 1) & 1,
            (b >> 2) & 1, (a >> 2) & 1, ((a >> 3) ^ (b >> 3)) & 1,
            ((a >> 4) ^ (b >> 4)) & 1, ((a >> 6) ^ (b >> 5)) & 1,
            ((a >> 5) ^ (b >> 6)) & 1, (b >> 3) & 1, (a >> 4) & 1,
            (b >> 6) & 1, (a >> 6) & 1, (a >> 7) & 1, (a >> 8) & 1]
    return (x // 512) * 65536 + (y // 128) * 128 * width + sum(bit << i for i, bit in enumerate(bits))


def write_rgba_bmp(path, rgba, width, rows):
    bgra = bytearray(rgba)
    bgra[0::4], bgra[2::4] = rgba[2::4], rgba[0::4]
    bgra[3::4] = bytes([255]) * (len(bgra) // 4)
    header = struct.pack('<2sIHHI', b'BM', 54 + len(bgra), 0, 0, 54)
    header += struct.pack('<IiiHHIIiiII', 40, width, -rows, 1, 32, 0, len(bgra), 2835, 2835, 0, 0)
    path.write_bytes(header + bgra)


def bmp_hud_hash(path):
    data = path.read_bytes()
    start = struct.unpack_from('<I', data, 10)[0]
    width, signed_height = struct.unpack_from('<ii', data, 18)
    height = abs(signed_height)
    bits = struct.unpack_from('<H', data, 28)[0]
    if bits != 24 or width != 1920 or height != 1080:
        raise ValueError('Expected 1920x1080 24-bit guest frame')
    stride = (width * 3 + 3) & ~3
    rgba = bytearray(width * 200 * 4)
    for row, y in enumerate(range(height - 200, height)):
        source = start + (y if signed_height < 0 else height - 1 - y) * stride
        for x in range(width):
            b, g, r = data[source + x * 3:source + x * 3 + 3]
            target = (row * width + x) * 4
            rgba[target:target + 4] = bytes((r, g, b, 255))
    return hashlib.sha256(rgba).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('run', type=Path)
    parser.add_argument('--width', type=int, default=1920)
    parser.add_argument('--height', type=int, default=1080)
    args = parser.parse_args()
    if not 1 <= args.width <= 3840 or not 200 <= args.height <= 2160:
        parser.error('Unsupported bounded dimensions')
    report = []
    settings = json.loads((args.run / 'report.json').read_text(encoding='utf-8-sig'))['DiagnosticEnvironment']
    first_flip, spacing = int(settings['DumpAfterFlip']), int(settings['DumpEveryFlip'])
    frames = {}
    for metadata in (args.run / 'frames').glob('*.txt'):
        fields = dict(line.split('=', 1) for line in metadata.read_text().splitlines() if '=' in line)
        frames[int(fields['flip_arg'])] = metadata.with_suffix('.bmp')
    for path in sorted(args.run.glob('tiled-source-*.bin')):
        if not re.fullmatch(r'tiled-source-\d+-0x[0-9A-Fa-f]+\.bin', path.name):
            continue
        data = path.read_bytes()
        if len(data) > 16 * 1024 * 1024:
            raise ValueError('Captured source exceeds diagnostic bound')
        output = bytearray(args.width * 200 * 4)
        for row, y in enumerate(range(args.height - 200, args.height)):
            for x in range(args.width):
                start = pixel_offset(x, y, args.width) * 4
                if start + 4 > len(data):
                    raise ValueError('Truncated tiled source')
                target = (row * args.width + x) * 4
                output[target:target + 4] = data[start:start + 4]
        reconstructed = path.with_suffix('.hud.bmp')
        write_rgba_bmp(reconstructed, output, args.width, 200)
        metadata_path = path.with_suffix('.json')
        if metadata_path.exists():
            capture = json.loads(metadata_path.read_text(encoding='utf-8-sig'))
            if (capture['width'], capture['height'], capture['pitch_in_pixel']) != (args.width, args.height, args.width):
                raise ValueError('Capture geometry does not match this profile')
            flip = capture['flip_arg']
            pairing = 'recorded-flip-argument'
        else:
            # Legacy captures: valid only for this single-port SDL scenario,
            # where flip_arg follows the diagnostic flip count without gaps.
            index = int(path.name.split('-')[2])
            flip = first_flip + (index - 1) * spacing
            pairing = 'inferred-legacy-single-port-sequence'
        captured = frames.get(flip)
        entry = {'source': path.name, 'bytes': len(data), 'flip_arg': flip, 'pairing': pairing,
                       'hud_sha256': hashlib.sha256(output).hexdigest(),
                       'reconstructed': reconstructed.name}
        if captured:
            entry['guest_bmp'] = captured.name
            entry['guest_bmp_hud_sha256'] = bmp_hud_hash(captured)
            entry['guest_bmp_matches_source'] = entry['hud_sha256'] == entry['guest_bmp_hud_sha256']
        report.append(entry)
    for path in sorted(args.run.glob('tiled-source-*.surfaces.json')):
        surfaces = json.loads(path.read_text(encoding='utf-8-sig'))['surfaces']
        for surface in surfaces:
            if surface['name'] not in ('textureSurface', 'windowSurface'):
                continue
            source = args.run / surface['file']
            data = source.read_bytes()
            width = surface['width']
            if len(data) != width * 200 * 4 or surface['pitch'] != width * 4:
                raise ValueError('Unexpected synchronous surface geometry')
            rows = 74 if surface['name'] == 'textureSurface' else 200
            write_rgba_bmp(source.with_suffix('.bmp'), data[-width * rows * 4:], width, rows)
    for path in sorted(args.run.glob('surface-*-windowSurface.bin')):
        data = path.read_bytes()
        if len(data) != args.width * 200 * 4:
            raise ValueError('Window sample dimensions do not match')
        write_rgba_bmp(path.with_suffix('.bmp'), data, args.width, 200)
    for path in sorted(args.run.glob('surface-*-textureSurface.bin')):
        # Pinned Wolf source profile: RGBA software texture, 640x400,
        # sampled last 200 rows. Preserve the numeric HUD for visual comparison.
        data = path.read_bytes()
        if len(data) != 640 * 200 * 4:
            raise ValueError('Unexpected source-profile texture dimensions')
        write_rgba_bmp(path.with_suffix('.bmp'), data[-640 * 74 * 4:], 640, 74)
    (args.run / 'tiled-source-analysis.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
