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
using System.Buffers;
using SharpEmu.HLE;

namespace SharpEmu.Libs.VideoOut;

internal static class VideoOutCpuDisplayFrames
{
    private static int rawSnapshotCount;
    private static readonly uint[] Columns = Enumerable.Range(0, 512).Select(x => PixelOffset((uint)x, 0, 512)).ToArray();
    private static readonly uint[] Rows = Enumerable.Range(0, 128).Select(y => PixelOffset(0, (uint)y, 512)).ToArray();
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

    internal static bool TryReadTiled(CpuContext ctx, VideoOutExports.DisplayBufferInfo info, out byte[] bgra, string? rawSnapshotDirectory = null, string? rawSnapshotMetadata = null)
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
        // Pool ordinary display-sized scratch storage, never retain a huge
        // diagnostic allocation. Only the fully read span is used below.
        var pooled = count <= 16UL * 1024 * 1024;
        var tiled = pooled ? ArrayPool<byte>.Shared.Rent((int)count) : new byte[(int)count];
        try
        {
            if (!ctx.Memory.TryRead(info.Address, tiled.AsSpan(0, (int)count))) return false;
            if (!string.IsNullOrWhiteSpace(rawSnapshotDirectory))
            {
                var snapshot = Interlocked.Increment(ref rawSnapshotCount);
                // Opt-in forensic capture: at most 16 ordinary (<=16 MiB)
                // sources per process; never turn this into an unbounded dump.
                if (snapshot <= 16 && count <= 16UL * 1024 * 1024)
                {
                    try
                    {
                        Directory.CreateDirectory(rawSnapshotDirectory);
                        var path = Path.Combine(rawSnapshotDirectory, $"tiled-source-{snapshot:D2}-0x{info.Address:X}.bin");
                        using (var stream = File.Create(path)) stream.Write(tiled.AsSpan(0, (int)count));
                        if (rawSnapshotMetadata is not null)
                            File.WriteAllText(Path.ChangeExtension(path, ".json"), rawSnapshotMetadata);
                    }
                    catch (IOException error) { Console.Error.WriteLine($"[LOADER][WARN] Tiled source snapshot: {error.Message}"); }
                    catch (UnauthorizedAccessException error) { Console.Error.WriteLine($"[LOADER][WARN] Tiled source snapshot: {error.Message}"); }
                }
            }
            var pixels = new byte[checked((int)(info.Width * info.Height * 4))];
            var columns = new uint[info.Width];
            for (uint x = 0; x < info.Width; x++) columns[x] = (x / 512) * 65536 + Columns[x & 511];
            var target = 0;
            for (uint y = 0; y < info.Height; y++)
            {
                var band = (y / 128) * (128 * pitch);
                var row = Rows[y & 127];
                for (uint x = 0; x < info.Width; x++)
                {
                    var source = checked((int)(band + (columns[x] ^ row)) * 4);
                    if (source > (int)count - 4) return false;
                    pixels[target++] = tiled[source + 2];
                    pixels[target++] = tiled[source + 1];
                    pixels[target++] = tiled[source];
                    pixels[target++] = 255;
                }
            }
            bgra = pixels;
            return true;
        }
        finally { if (pooled) ArrayPool<byte>.Shared.Return(tiled); }
    }
}
