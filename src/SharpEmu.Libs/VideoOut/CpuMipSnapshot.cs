// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Gpu;

namespace SharpEmu.Libs.VideoOut;

/// <summary>Exact comparison of owned CPU snapshots; no write-tracker or hash assumptions.</summary>
internal static class CpuMipSnapshot
{
    internal static bool Matches(GuestDrawTexture left, GuestDrawTexture right)
    {
        if (left.AdditionalMips is not { Length: > 0 } a ||
            right.AdditionalMips is not { Length: > 0 } b || a.Length != b.Length)
        {
            return false;
        }

        // Record equality covers every descriptor/view/sampler field. Arrays are
        // compared by content below, and the tracker generation is not content.
        if ((left with { RgbaPixels = Array.Empty<byte>(), AdditionalMips = null, WriteGeneration = -1 }) !=
            (right with { RgbaPixels = Array.Empty<byte>(), AdditionalMips = null, WriteGeneration = -1 }) ||
            !left.RgbaPixels.AsSpan().SequenceEqual(right.RgbaPixels))
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].Width != b[i].Width || a[i].Height != b[i].Height ||
                a[i].Pitch != b[i].Pitch || !a[i].Pixels.AsSpan().SequenceEqual(b[i].Pixels))
            {
                return false;
            }
        }

        return true;
    }

    internal static long ByteCount(GuestDrawTexture texture) =>
        texture.RgbaPixels.LongLength +
        (texture.AdditionalMips?.Sum(mip => mip.Pixels.LongLength) ?? 0);
}
