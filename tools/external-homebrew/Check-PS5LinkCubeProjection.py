# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Sparse independent CPU reference for the frozen two-texture cube fixture.

Checks interior pixels against projected triangles, nearest depth and
perspective-correct UVs. This is not a general rasterizer or a console oracle.
Samples near triangle/texture boundaries are excluded by fixed geometric rules,
before reading captured pixel values; background is not checked.
"""
import argparse
import hashlib
import json
import math
import re
from pathlib import Path

FACES = (
    ((-1,-1,-1),(-1,1,-1),(1,1,-1),(1,-1,-1)),
    ((1,-1,1),(1,1,1),(-1,1,1),(-1,-1,1)),
    ((-1,-1,1),(-1,1,1),(-1,1,-1),(-1,-1,-1)),
    ((1,-1,-1),(1,1,-1),(1,1,1),(1,-1,1)),
    ((-1,1,-1),(-1,1,1),(1,1,1),(1,1,-1)),
    ((-1,-1,1),(-1,-1,-1),(1,-1,-1),(1,-1,1)),
)
UV = ((0,0),(0,1),(1,1),(1,0))


def sine(x):
    # Fixture deliberately uses this approximation without a guest libc.
    while x > math.pi:
        x -= 2*math.pi
    while x < -math.pi:
        x += 2*math.pi
    x2 = x*x
    return x*(1-x2*(1/6-x2*(1/120-x2/5040)))


def cosine(x):
    return sine(x+math.pi/2)


def triangles():
    result = []
    focal = cosine(math.pi/6)/sine(math.pi/6)
    for obj, (rx, ry, offset, phase) in enumerate((
            (.75*.6, .75, (-.45,0,4), 0),
            (.75*.3, -.75*.7, (.45,.12,4.65), 1))):
        for face in FACES:
            vertices = []
            for (x,y,z), uv in zip(face, UV):
                # Apply row-vector X rotation, then Y rotation, then translation.
                y,z = y*cosine(rx)-z*sine(rx), y*sine(rx)+z*cosine(rx)
                x,z = x*cosine(ry)+z*sine(ry), -x*sine(ry)+z*cosine(ry)
                x,y,z = x+offset[0],y+offset[1],z+offset[2]
                sx = 960+540*focal*x/z
                sy = 540-540*focal*y/z
                vertices.append((sx,sy,z,*uv))
            for indices in ((0,1,2),(0,2,3)):
                result.append((obj,phase,tuple(vertices[i] for i in indices)))
    return result


def interior_reference(x, y, scene, affine_uv=False, shared_texture=False):
    hits = []
    for obj, phase, (a,b,c) in scene:
        den = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den) < 1e-8:
            continue
        wa = ((b[1]-c[1])*(x-c[0])+(c[0]-b[0])*(y-c[1]))/den
        wb = ((c[1]-a[1])*(x-c[0])+(a[0]-c[0])*(y-c[1]))/den
        weights = (wa,wb,1-wa-wb)
        if min(weights) < 0:
            continue
        inverse_z = sum(w/v[2] for w,v in zip(weights,(a,b,c)))
        if affine_uv:
            uv = tuple(sum(w*v[k] for w,v in zip(weights,(a,b,c))) for k in (3,4))
        else:
            uv = tuple(sum(w*v[k]/v[2] for w,v in zip(weights,(a,b,c)))/inverse_z
                       for k in (3,4))
        hits.append((1/inverse_z,obj,phase,min(weights),uv))
    if not hits:
        return None
    _,obj,phase,margin,(u,v) = min(hits)
    if shared_texture:
        phase = 0
    # Avoid precision-sensitive triangle edges and filtered checker transitions.
    if margin < .04 or min(u*8 % 1,1-u*8 % 1,v*8 % 1,1-v*8 % 1) < .2:
        return None
    tx,ty = int(u*64),int(v*64)
    brightness = 48 if ((tx//8+ty//8)&1) else 255
    rgb = ((brightness,0,0) if u < .5 else (0,brightness,0)) if v < .5 else (
        (0,0,brightness) if u < .5 else (brightness,brightness,0))
    rgb = tuple(rgb[(i+phase)%3] for i in range(3))
    return obj,bytes((*rgb[::-1],255))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--affine-uv-control', action='store_true',
                        help='Deliberately incorrect UV reference; must reject correct images')
    parser.add_argument('--shared-texture-control', action='store_true',
                        help='Deliberately use object zero palette for both objects')
    args = parser.parse_args()
    smoke = json.loads((args.folder/'report.json').read_text(encoding='utf-8-sig'))
    profiles = {'TextureCubeSlotsStatic','TextureCubeSlotsReverseStatic',
                'TextureCubeSlotsDisabledStatic','TextureCubeSlotsDisabledReverseStatic'}
    if (smoke.get('Profile') not in profiles or not smoke.get('FrozenView') or
            smoke.get('ObjectCount') != 2 or not smoke.get('MutableTexturePerSlot')):
        raise ValueError('Reference is only for the frozen two-cube fixture')
    scene = triangles()
    results = []
    for path in sorted(args.folder.glob('present-*.bgra')):
        match = re.fullmatch(r'present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm.bgra',path.name)
        if not match:
            raise ValueError(f'Unsupported capture: {path}')
        width,height = map(int,match.groups())
        pixels = path.read_bytes()
        if len(pixels) != width*height*4:
            raise ValueError(f'Truncated capture: {path}')
        checked = [0,0]
        mismatches = []
        for y in range(11,height,23):
            for x in range(11,width,23):
                expected = interior_reference((x+.5)*1920/width,(y+.5)*1080/height,scene,
                                              args.affine_uv_control,args.shared_texture_control)
                if expected is None:
                    continue
                obj,color = expected
                checked[obj] += 1
                actual = pixels[(y*width+x)*4:(y*width+x)*4+4]
                if actual != color:
                    mismatches.append({'xy':[x,y],'object':obj,'expected':list(color),'actual':list(actual)})
        results.append({'Capture':str(path.resolve()),'Sha256':hashlib.sha256(pixels).hexdigest(),
                        'SamplesPerObject':checked,'MismatchingSamples':len(mismatches),
                        'MismatchExamples':mismatches[:12]})
    passed = bool(results) and all(min(r['SamplesPerObject']) >= 50 and
                                   r['MismatchingSamples'] == 0 for r in results)
    report = {'SparseProjectionDepthUvPassed':passed,'Captures':results,
              'AffineUvNegativeControl':args.affine_uv_control,
              'SharedTextureNegativeControl':args.shared_texture_control,
              'Scope':'frozen fixture interior samples only; background and raster edges excluded',
              'WholeImageCorrectnessVerified':False}
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({'Passed':passed,'Captures':len(results),
                      'SamplesPerCapture':[r['SamplesPerObject'] for r in results],
                      'MismatchesPerCapture':[r['MismatchingSamples'] for r in results]}))
    if not passed:
        raise SystemExit('Independent cube projection/depth/UV reference failed')


if __name__ == '__main__':
    main()
