# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Inspect the AudioOut stereo-s16le diagnostic tap, without playing audio."""
import argparse
import array
import hashlib
import json
import math
from pathlib import Path
import sys
import wave


def analyze(path: Path, rate: int) -> dict:
    if path.stat().st_size % 4:
        raise ValueError("PCM ends in an incomplete stereo frame")
    frames = nonzero = clipped = sample_count = square_sum = peak = 0
    windows = []
    digest = hashlib.sha256()
    with path.open("rb") as source, wave.open(str(path.with_suffix(".wav")), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(rate)
        while block := source.read(rate * 4):
            if len(block) % 4:
                raise ValueError("PCM ends in an incomplete stereo frame")
            digest.update(block)
            wav.writeframesraw(block)
            samples = array.array("h", block)
            if sys.byteorder != "little":
                samples.byteswap()
            block_peak = max(abs(sample) for sample in samples)
            block_nonzero = sum(sample != 0 for sample in samples)
            windows.append({"second": len(windows), "peak": block_peak / 32768,
                            "nonzero_samples": block_nonzero})
            peak = max(peak, block_peak)
            nonzero += block_nonzero
            clipped += sum(sample in (-32768, 32767) for sample in samples)
            square_sum += sum(sample * sample for sample in samples)
            sample_count += len(samples)
            frames += len(block) // 4
    return {"source": str(path.resolve()), "sha256": digest.hexdigest(),
            "format": "stereo-s16le", "sample_rate": rate, "frames": frames,
            "duration_seconds": frames / rate, "peak": peak / 32768,
            "rms": math.sqrt(square_sum / sample_count) / 32768 if sample_count else 0,
            "nonzero_samples": nonzero, "samples": sample_count,
            "clipped_samples": clipped, "windows": windows,
            "audible_device_output_verified": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pcm", type=Path)
    parser.add_argument("--rate", type=int, required=True,
                        help="Use the actual rate recorded by AudioOutOpen, not an assumed rate")
    args = parser.parse_args()
    if not 8000 <= args.rate <= 192000:
        parser.error("rate must be between 8000 and 192000")
    result = analyze(args.pcm, args.rate)
    report = args.pcm.with_suffix(".analysis.json")
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in result.items() if key != "windows"}, indent=2))
    print(f"Report: {report}")
