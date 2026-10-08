// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Altered C# axis decomposition of the SDL PS5 tile-address equation.
// Original: Copyright (C) 2026 John Törnblom <john.tornblom@gmail.com>.
// This software is provided 'as-is', without any express or implied warranty.
// In no event will the authors be held liable for any damages arising from
// the use of this software.
// Permission is granted to anyone to use this software for any purpose,
// including commercial applications, and to alter it and redistribute it
// freely, subject to the following restrictions:
// 1. The origin of this software must not be misrepresented; you must not
// claim that you wrote the original software. If you use this software in a
// product, an acknowledgment in the product documentation would be
// appreciated but is not required.
// 2. Altered source versions must be plainly marked as such, and must not be
// misrepresented as being the original software.
// 3. This notice may not be removed or altered from any source distribution.
using SharpEmu.HLE;

namespace SharpEmu.Libs.VideoOut;

internal static class VideoOutCpuDisplayFrames
{
    // Single-sample 32-bit PS5 display layout observed in the open SDL backend.
    // Independent axis form of SDL_ps5tilemap.c's PS5_TilePixel equation, pinned
    // at ee4c47dc0d617b3bc8f35108f9956baf228a1322 (ps5-payload-dev/SDL).
    internal static uint PixelOffset(uint x, uint y, uint pitch)
    {
        var withinX = x & 511;
        var withinY = y & 127;
        var xTerm = (withinX & 3) | ((withinX & 4) << 3) | ((withinX & 8) << 3) |
                    ((withinX & 16) << 3) | ((withinX & 16) << 7) | ((withinX & 32) << 4) |
                    ((withinX & 64) << 2) | ((withinX & 64) << 7) |
                    ((withinX & 128) << 7) | ((withinX & 256) << 7);
        var yTerm = ((withinY & 7) << 2) | ((withinY & 8) << 3) | ((withinY & 8) << 7) |
                    ((withinY & 16) << 3) | ((withinY & 32) << 3) |
                    ((withinY & 64) << 3) | ((withinY & 64) << 6);
        return (x / 512) * 65536 + (y / 128) * (128 * pitch) + (xTerm ^ yTerm);
    }

    internal static bool TryReadTiled(CpuContext ctx, VideoOutExports.DisplayBufferInfo info, out byte[] bgra)
    {
        bgra = [];
        var pitch = info.PitchInPixel == 0 ? info.Width : info.PitchInPixel;
        if (ctx.TargetGeneration != Generation.Gen5 || info.TilingMode != 0 ||
            info.PixelFormat != 0x8000000022000000UL ||
            info.Width == 0 || info.Height == 0 || info.Width > 8192 || info.Height > 8192 ||
            pitch < info.Width || pitch > 8192) return false;
        var count = ((ulong)(info.Height - 1) / 128 * (128 * pitch) +
                     (ulong)(pitch - 1) / 512 * 65536 + 65536) * 4;
        if (count > 256UL * 1024 * 1024 || info.Address > ulong.MaxValue - count) return false;
        var tiled = new byte[(int)count];
        if (!ctx.Memory.TryRead(info.Address, tiled)) return false;
        var pixels = new byte[checked((int)(info.Width * info.Height * 4))];
        for (uint y = 0; y < info.Height; y++)
        for (uint x = 0; x < info.Width; x++)
        {
            var source = checked((int)PixelOffset(x, y, pitch) * 4);
            var target = checked((int)(y * info.Width + x) * 4);
            if (source > tiled.Length - 4) return false;
            pixels[target] = tiled[source + 2];
            pixels[target + 1] = tiled[source + 1];
            pixels[target + 2] = tiled[source];
            pixels[target + 3] = 255;
        }
        bgra = pixels;
        return true;
    }
}
