// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;

namespace SharpEmu.Libs.VideoOut;

// Opt-in, pinned SDL source-profile diagnostics. Called on the submitting guest
// thread, after rendering, rather than asynchronously reading different frames.
// Other guest threads are not stopped; this is not a process-wide atomic snapshot.
internal static class SourceSurfaceFrameDiagnostics
{
    internal static void Capture(CpuContext ctx, string tiledPath)
    {
        var setting = Environment.GetEnvironmentVariable("SHARPEMU_SOURCE_SURFACE_SLOTS");
        if (string.IsNullOrWhiteSpace(setting)) return;
        var slots = setting.Split(';');
        if (slots.Length != 4) return;
        string[] names = ["screenBuffer", "screen", "textureSurface", "windowSurface"];
        var reports = new List<object>();
        for (var i = 0; i < slots.Length; i++)
        {
            if (!ulong.TryParse(slots[i], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var slot) ||
                !KernelMemoryCompatExports.TryReadGuestOrLibcHeapUInt64(ctx, slot, out var surface) || surface == 0) continue;
            byte[]? texture = null;
            if (i == 2)
            {
                texture = Read(ctx, surface, 120);
                if (texture is null) continue;
                surface = U64(texture, 112); // SW_LockTexture: driverdata @ 0x70.
            }
            else if (i == 3)
            {
                if (!KernelMemoryCompatExports.TryReadGuestOrLibcHeapUInt64(ctx, surface + 144, out surface)) continue;
            }
            var header = Read(ctx, surface, 96);
            if (header is null) continue;
            var width = I32(header, 16); var height = I32(header, 20); var pitch = I32(header, 24);
            var pixels = U64(header, 32);
            if (width is < 1 or > 3840 || height is < 200 or > 2160 || pitch < width || pitch > 16384 || pixels == 0) continue;
            var offset = (ulong)((height - 200) * pitch);
            if (pixels > ulong.MaxValue - offset) continue;
            var bytes = Read(ctx, pixels + offset, 200 * pitch);
            if (bytes is null) continue;
            var path = Path.ChangeExtension(tiledPath, $"{names[i]}.bin");
            File.WriteAllBytes(path, bytes);
            var format = Read(ctx, U64(header, 8), 48);
            var map = Read(ctx, U64(header, 80), 120);
            reports.Add(new {
                name = names[i], width, height, pitch, first_row = height - 200, file = Path.GetFileName(path),
                sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                format = format is null ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(format),
                blit_flags = map is null ? 0 : I32(map, 104), colorkey = map is null ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(108)),
                blit_function = map is null ? 0UL : U64(map, 24),
                texture_blend = texture is null ? -1 : I32(texture, 28),
                texture_scale = texture is null ? -1 : I32(texture, 32),
                texture_native = texture is null ? 0UL : U64(texture, 48)
            });
        }
        File.WriteAllText(Path.ChangeExtension(tiledPath, "surfaces.json"),
            JsonSerializer.Serialize(new { submitting_thread = Environment.CurrentManagedThreadId, process_atomic = false, surfaces = reports }));
    }

    private static byte[]? Read(CpuContext ctx, ulong address, int length)
    {
        if (address == 0 || length is < 1 or > 4 * 1024 * 1024 || address > ulong.MaxValue - (ulong)length) return null;
        var bytes = new byte[length];
        return KernelMemoryCompatExports.TryReadGuestOrLibcHeap(ctx, address, bytes) ? bytes : null;
    }
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
    private static int I32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
}
