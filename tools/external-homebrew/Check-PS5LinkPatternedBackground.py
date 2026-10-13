# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Check logical background colors without calling the runtime tiling decoder.

Samples use fixed source-space cell centers in exterior strips beyond the
fixture's cube motion. They avoid presentation filtering at color boundaries.
This is a sparse fixture oracle, not a whole-image or console-accuracy claim.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path


def expected_bgra(x, y, phase):
    b = 32 + (x//64 % 8)*24
    g = 24 + (y//64 % 8)*24
    r = 40 + ((x//128 + y//128*3) % 8)*24
    channels = (b,g,r)
    return bytes((*channels[phase:],*channels[:phase],255))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    smoke = json.loads((args.folder/'report.json').read_text(encoding='utf-8-sig'))
    if not smoke.get('PatternedBackground'):
        raise ValueError('Expected the patterned-background native fixture')
    frozen = smoke['FrozenView']
    log = (args.folder/'stderr.log').read_text(encoding='utf-8-sig')
    source_probes = []
    for frame_word, p0_word, p1_word, phase_word in re.findall(
            r'pattern_source frame=([0-9A-F]{8}) x32y32=([0-9A-F]{8}) '
            r'x96y32=([0-9A-F]{8}) phase=([0-9A-F]{8})',log):
        frame,p0,p1,phase = (int(w,16) for w in (frame_word,p0_word,p1_word,phase_word))
        expected_phase = 0 if frozen else frame//150 % 3
        correct = phase == expected_phase and all(
            word.to_bytes(4,'little') == expected_bgra(x,32,expected_phase)
            for x,word in ((32,p0),(96,p1)))
        source_probes.append({'Frame':frame,'Phase':phase,'Values':[p0,p1],'Correct':correct})
    results = []
    for path in sorted(args.folder.glob('present-*.bgra')):
        match = re.fullmatch(r'present-\d+-(\d+)x(\d+)-B8G8R8A8Unorm.bgra',path.name)
        if not match:
            raise ValueError(f'Unsupported capture format: {path.name}')
        width,height = map(int,match.groups())
        pixels = path.read_bytes()
        if len(pixels) != width*height*4:
            raise ValueError(f'Truncated capture: {path}')
        matches = [0,0,0]
        count = 0
        examples = []
        for sy in range(32,1080,64):
            for sx in range(32,1920,64):
                # Exclude the entire swept cube region before inspecting pixels.
                if 320 <= sx < 1600 and 64 <= sy < 1024:
                    continue
                x,y = int(sx*width/1920),int(sy*height/1080)
                actual = pixels[(y*width+x)*4:(y*width+x)*4+4]
                count += 1
                for phase in range(3):
                    matches[phase] += actual == expected_bgra(sx,sy,phase)
                if len(examples) < 6 and actual not in [expected_bgra(sx,sy,p) for p in range(3)]:
                    examples.append({'SourceXY':[sx,sy],'ActualBgra':list(actual)})
        allowed_phases = (0,) if frozen else range(3)
        matching_phase = next((p for p in allowed_phases if matches[p] == count),None)
        results.append({'Capture':str(path.resolve()),'Sha256':hashlib.sha256(pixels).hexdigest(),
                        'CheckedSamples':count,'MatchesPerPhase':matches,
                        'MatchingPhase':matching_phase,'MismatchExamples':examples})
    phases = [r['MatchingPhase'] for r in results]
    distinct_phases = [p for i,p in enumerate(phases) if i == 0 or p != phases[i-1]]
    source_ok = [p['Frame'] for p in source_probes] == [0,149,299,449,599] and all(
        p['Correct'] for p in source_probes)
    passed = source_ok and bool(results) and all(r['CheckedSamples'] >= 150 and
                                   r['MatchingPhase'] is not None for r in results)
    if not frozen:
        passed = passed and distinct_phases == [0,1,2,0]
        passed = passed and all(b == a or b == (a+1)%3 for a,b in zip(phases,phases[1:]))
    report = {'PatternedBackgroundPassed':passed,'FirstCaptureComplete':bool(results) and phases[0] == 0,
              'NativeCpuSourceProbesPassed':source_ok,'NativeCpuSourceProbes':source_probes,
              'FrozenView':frozen,'PhaseSequence':phases,'Captures':results,
              'CompletedColorCycle':distinct_phases == [0,1,2,0],
              'Scope':'fixed exterior source-cell centers; excludes cube region and pixel boundaries',
              'WholeImageCorrectnessVerified':False}
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({'Passed':passed,'CpuSourcePassed':source_ok,'Phases':phases,
                      'SampleCounts':[r['CheckedSamples'] for r in results],
                      'Matches':[r['MatchesPerPhase'] for r in results]}))
    if not passed:
        raise SystemExit('Patterned background reference failed')


if __name__ == '__main__':
    main()
