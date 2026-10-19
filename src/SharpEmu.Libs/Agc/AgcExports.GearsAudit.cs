// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using SharpEmu.Libs.Gpu;
using SharpEmu.Libs.VideoOut;

namespace SharpEmu.Libs.Agc;

public static partial class AgcExports
{
    private static void CaptureCompletedGearsDraw(ulong sequence, uint vertexCount,
        Gen5ShaderReplaySnapshot export, Gen5ShaderReplaySnapshot pixel,
        byte[] initial, byte[] actual, byte[] indices, uint inputEnable,
        uint inputAddress, IReadOnlyList<uint> inputControl, GuestRenderState? renderState, GuestRenderTarget target)
    {
        var root = Environment.GetEnvironmentVariable("TOUCHEPX5_GEARS_NATIVE_AUDIT_DIR");
        if (string.IsNullOrWhiteSpace(root)) return;
        if (!uint.TryParse(Environment.GetEnvironmentVariable("TOUCHEPX5_GEARS_NATIVE_AUDIT_FRAME"), out var frame) || frame > 10000)
            throw new InvalidOperationException("Native Gears audit needs a frame ordinal in 0..10000");
        var first = (ulong)frame * 4 + 1;
        if (sequence < first || sequence >= first + 4) return;
        var directory = Path.Combine(root, $"draw-{sequence:D6}");
        if (Directory.Exists(directory)) throw new InvalidOperationException("Duplicate native Gears audit draw");
        Directory.CreateDirectory(directory);
        var context = new Gen5ShaderReplayDrawContext(vertexCount, 1, 4, 0, false, 0, 0, 0, null,
            "nonindexed", inputEnable, inputAddress, inputControl.ToArray(), sequence);
        uint[] Words(byte[] bytes)
        {
            if (bytes.Length % 4 != 0) throw new InvalidOperationException("Unaligned audit capture");
            var words = new uint[bytes.Length / 4];
            for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
            return words;
        }
        void Write(string name, object value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value));
        Write("gs.replay.json", new { Snapshot = export, DrawContext = context });
        Write("ps.replay.json", new { Snapshot = pixel, DrawContext = context });
        Write("initial.json", Words(initial));
        Write("actual.json", Words(actual));
        Write("indices.json", Words(indices));
        Write("render.json", new { RenderState = renderState, Target = target });
        Write("capture.json", new { Scope = "Original native pinned Gears inputs and completed GPU exports before graphics consumption; no independent verification implied",
            FrameOrdinal = frame, DrawSequence = sequence, DrawCount = vertexCount, GpuReadbackCompleted = true });
        var files = new[] { "gs.replay.json", "ps.replay.json", "initial.json", "actual.json", "indices.json", "capture.json", "render.json" };
        Write("hashes.json", files.ToDictionary(name => name,
            name => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name))))));
        Console.Error.WriteLine($"[LOADER][INFO] agc.experimental_gears_ngg native_audit seq={sequence} frame={frame} path={directory}");
    }

    private static void BindGearsAuditPresentation(ulong lastDraw, long flipArg, int handle, int index,
        GuestPresentationCompletion completion)
    {
        var root = Environment.GetEnvironmentVariable("TOUCHEPX5_GEARS_NATIVE_AUDIT_DIR");
        if (string.IsNullOrWhiteSpace(root) ||
            Environment.GetEnvironmentVariable("TOUCHEPX5_EXPERIMENTAL_GEARS_NGG") != "1" ||
            !uint.TryParse(Environment.GetEnvironmentVariable("TOUCHEPX5_GEARS_NATIVE_AUDIT_FRAME"), out var frame) ||
            frame > 10000 || lastDraw != ((ulong)frame + 1) * 4) return;
        if (!VideoOutExports.TryGetDisplayBufferInfo(handle, index, out var buffer))
            throw new InvalidOperationException("Selected audit flip has no display buffer");
        Directory.CreateDirectory(root);
        completion.DiagnosticCapturePrefix = Path.Combine(root, "native-present");
        File.WriteAllText(Path.Combine(root, "frame-flip.json"), JsonSerializer.Serialize(new {
            FrameOrdinal = frame, LastDrawSequence = lastDraw, FlipArg = flipArg,
            Address = buffer.Address, Width = buffer.Width, Height = buffer.Height }));
    }
}
