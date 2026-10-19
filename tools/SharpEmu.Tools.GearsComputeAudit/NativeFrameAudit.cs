// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

internal static class NativeFrameAudit
{
    public static void Run(string root, bool checkPixels = false)
    {
        var directories = Directory.GetDirectories(root, "draw-*").OrderBy(d => d, StringComparer.Ordinal).ToArray();
        if (directories.Length != 4) throw new InvalidOperationException("Exactly four native frame draws required");
        uint[] counts = [3, 1200, 600, 600];
        ulong? firstSequence = null;
        var reports = new List<object>();
        var referenceWords = new List<uint>(); var referenceIndices = new List<uint>();
        JsonElement? capturedRender = null;
        foreach (var (directory, ordinal) in directories.Select((d, i) => (d, i)))
        {
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(directory, "hashes.json")))!;
            foreach (var name in new[] { "gs.replay.json", "ps.replay.json", "initial.json", "actual.json", "indices.json", "capture.json" })
                if (!hashes.TryGetValue(name, out var hash) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name)))) != hash)
                    throw new InvalidOperationException("Native capture integrity mismatch: " + name);
            using var envelope = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "gs.replay.json")));
            using var psEnvelope = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "ps.replay.json")));
            var snapshot = envelope.RootElement.GetProperty("Snapshot").Deserialize<Gen5ShaderReplaySnapshot>()!;
            var pixel = psEnvelope.RootElement.GetProperty("Snapshot").Deserialize<Gen5ShaderReplaySnapshot>()!;
            VerifyProgram(snapshot, 384, "79C8446ECA71AA98644D0C3D8B737875884F101E34EB308EC029CA1FCCB03492");
            VerifyProgram(pixel, 160, "6C12F9DFD892AF78FF5DAE047EEFB02B4ED27C93FD20A87BA763ACC5F3BD2E6C");
            var context = envelope.RootElement.GetProperty("DrawContext").Deserialize<Gen5ShaderReplayDrawContext>()!;
            if (envelope.RootElement.GetProperty("DrawContext").GetRawText() != psEnvelope.RootElement.GetProperty("DrawContext").GetRawText())
                throw new InvalidOperationException("Native stage draw identities differ");
            firstSequence ??= context.DrawSequence;
            if (firstSequence == 0 || (firstSequence - 1) % 4 != 0 || context.DrawSequence != firstSequence + (ulong)ordinal || context.DrawCount != counts[ordinal])
                throw new InvalidOperationException("Not one consecutive original Gears frame");
            using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "capture.json")));
            if (checkPixels)
            {
                var renderFile = Path.Combine(directory, "render.json");
                if (!hashes.TryGetValue("render.json", out var renderHash) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(renderFile))) != renderHash)
                    throw new InvalidOperationException("Native render-state integrity mismatch");
                using var render = JsonDocument.Parse(File.ReadAllText(renderFile));
                if (capturedRender is { } previous && previous.GetRawText() != render.RootElement.GetRawText())
                    throw new InvalidOperationException("Native per-draw render state differs");
                capturedRender = render.RootElement.Clone();
            }
            if (!capture.RootElement.GetProperty("GpuReadbackCompleted").GetBoolean() ||
                capture.RootElement.GetProperty("DrawSequence").GetUInt64() != context.DrawSequence ||
                capture.RootElement.GetProperty("FrameOrdinal").GetUInt64() != (context.DrawSequence - 1) / 4)
                throw new InvalidOperationException("Native completion provenance differs");
            var batch = MeshReplayReference.Partition(context);
            var slots = batch.OutputSlotCapacity;
            var primitiveFirst = checked((int)slots * 12);
            var descriptorFirst = checked((int)slots * 17);
            var sourceFirst = descriptorFirst + batch.GroupCount * 4;
            uint[] Read(string name) => JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(directory, name)))!;
            var initial = Read("initial.json"); var actual = Read("actual.json"); var indices = Read("indices.json");
            var canonical = Enumerable.Repeat(0xCAFEBABEu, sourceFirst + batch.SourceWords.Length + 16).ToArray();
            batch.Descriptors.Span.CopyTo(canonical.AsSpan(descriptorFirst));
            batch.SourceWords.Span.CopyTo(canonical.AsSpan(sourceFirst));
            if (!initial.SequenceEqual(canonical)) throw new InvalidOperationException("Noncanonical native producer inputs/guards");
            var (expected, floatWords) = MeshReplayReference.Evaluate(snapshot, context, canonical, primitiveFirst);
            var floating = floatWords.ToHashSet();
            int Mismatches(uint[] words)
            {
                if (words.Length != expected.Length) return Math.Max(words.Length, expected.Length);
                var failures = 0;
                for (var i = 0; i < words.Length; i++)
                {
                    if (words[i] == expected[i]) continue;
                    var a = BitConverter.UInt32BitsToSingle(words[i]); var e = BitConverter.UInt32BitsToSingle(expected[i]);
                    if (floating.Contains(i) && float.IsFinite(a) && float.IsFinite(e) && Math.Abs((double)a-e) <= 0.00002 * Math.Max(1, Math.Abs((double)e))) continue;
                    failures++;
                }
                return failures;
            }
            var failures = Mismatches(actual);
            if (failures != 0) throw new InvalidOperationException($"Native GLSL export reference differs at {context.DrawSequence}: {failures} words");
            var expectedIndices = Enumerable.Range(0, (int)context.DrawCount).Select(i => (uint)(i / 126 * 128 + i % 126)).ToArray();
            if (!indices.SequenceEqual(expectedIndices)) throw new InvalidOperationException("Native consumed connectivity differs from original triangle list");
            if (ordinal != 0)
            {
                var outputBase = (uint)(referenceWords.Count / 12);
                referenceWords.AddRange(expected.Take((int)slots * 12));
                referenceIndices.AddRange(expectedIndices.Select(i => i + outputBase));
            }
            var wrongPosition = actual.ToArray();
            wrongPosition[0] = BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(wrongPosition[0]) + 1f);
            var stalePrimitive = actual.ToArray(); stalePrimitive[primitiveFirst + 4] = 2;
            if (Mismatches(wrongPosition) == 0 || Mismatches(stalePrimitive) == 0)
                throw new InvalidOperationException("Numerical/primitive negative control unexpectedly accepted");
            reports.Add(new { Sequence = context.DrawSequence, context.DrawCount, ComparedWords = actual.Length,
                FloatWords = floatWords.Length, IndexCount = indices.Length, Mismatches = failures,
                WrongPositionRejected = true, StalePrimitiveRejected = true });
        }
        if (checkPixels) CompareNativePixels(root, firstSequence!.Value, capturedRender!.Value, referenceWords, referenceIndices);
        File.WriteAllText(Path.Combine(root, "native-numerical-report.json"), JsonSerializer.Serialize(new {
            Scope = checkPixels ? "Completed live native Gears GLSL exports plus exact-ticket native interior lighting pixels; edge/rasterization conformance excluded" :
                "Completed live native Gears GS exports versus independent double-precision GLSL equations and triangle-list connectivity; no re-dispatch or numerical screenshot validation",
            FrameOrdinal = (firstSequence!.Value-1)/4, Draws = reports, NumericalExportsVerified = true,
            ConsumedIndicesVerified = true, NativeImagePixelsVerified = false,
            NativeInteriorLightingVerified = checkPixels,
            PresentedCaptureHashes = checkPixels ? new[] { "frame-flip.json", "native-present-info.json", "lighting-reference-report.json",
                Path.GetFileName(Directory.GetFiles(root, "native-present-*.bgra").Single()) }
                .ToDictionary(name => name, name => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name))))) : null
        }));
        Console.WriteLine($"PASS native frame {(firstSequence!.Value-1)/4}: four completed original GS outputs and consumed indices match independent reference");
    }

    private static void CompareNativePixels(string root, ulong firstSequence, JsonElement render, List<uint> words, List<uint> indices)
    {
        using var flip = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "frame-flip.json")));
        using var present = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "native-present-info.json")));
        var target = render.GetProperty("Target"); var state = render.GetProperty("RenderState");
        var info = present.RootElement; var identity = flip.RootElement;
        if (identity.GetProperty("LastDrawSequence").GetUInt64() != firstSequence + 3 ||
            identity.GetProperty("FrameOrdinal").GetUInt64() != (firstSequence-1)/4 ||
            identity.GetProperty("Address").GetUInt64() != target.GetProperty("Address").GetUInt64() ||
            identity.GetProperty("FlipArg").GetInt64() != info.GetProperty("FlipArg").GetInt64() ||
            info.GetProperty("GuestImageAddress").GetUInt64() != target.GetProperty("Address").GetUInt64() ||
            !info.GetProperty("Presented").GetBoolean()) throw new InvalidOperationException("Native presented-frame identity differs");
        var targetWidth = target.GetProperty("Width").GetDouble(); var targetHeight = target.GetProperty("Height").GetDouble();
        if (targetWidth != info.GetProperty("Width").GetDouble() || targetHeight != info.GetProperty("Height").GetDouble() ||
            target.GetProperty("Format").GetUInt32() != 10 || target.GetProperty("NumberType").GetUInt32() != 0 ||
            info.GetProperty("Format").GetString() != "B8G8R8A8Unorm" ||
            state.GetProperty("Scissor").ValueKind != JsonValueKind.Null ||
            state.GetProperty("Blends").EnumerateArray().Any(b => b.GetProperty("Enable").GetBoolean() || b.GetProperty("WriteMask").GetUInt32() != 15) ||
            state.GetProperty("Raster").EnumerateObject().Any(v => v.Value.GetBoolean()))
            throw new InvalidOperationException("Native pixel reference requires the captured unblended, uncullled, full-target UNORM subset");
        var depth = state.GetProperty("Depth"); var view = state.GetProperty("Viewport"); var blit = info.GetProperty("Blit");
        if (!depth.GetProperty("TestEnable").GetBoolean() || !depth.GetProperty("WriteEnable").GetBoolean() ||
            depth.GetProperty("CompareOp").GetUInt32() != 3 || depth.GetProperty("ClearEnable").GetBoolean() ||
            depth.GetProperty("StencilTestEnable").GetBoolean() || view.GetProperty("MinDepth").GetDouble() != 0 || view.GetProperty("MaxDepth").GetDouble() != 1)
            throw new InvalidOperationException("Unsupported native depth/reference state");
        var raster = new NativeRaster(targetWidth, targetHeight, view.GetProperty("X").GetDouble(), view.GetProperty("Y").GetDouble(),
            view.GetProperty("Width").GetDouble(), view.GetProperty("Height").GetDouble(), blit.GetProperty("X").GetDouble(),
            blit.GetProperty("Y").GetDouble(), blit.GetProperty("Width").GetDouble(), blit.GetProperty("Height").GetDouble());
        var inputs = Path.Combine(root, "pixel-reference"); Directory.CreateDirectory(inputs);
        File.WriteAllText(Path.Combine(inputs, "actual.json"), JsonSerializer.Serialize(words));
        File.WriteAllText(Path.Combine(inputs, "remapped-indices.json"), JsonSerializer.Serialize(indices));
        var image = info.GetProperty("Path").GetString()!;
        var expectedImage = Path.Combine(Path.GetFullPath(root),
            $"native-present-{info.GetProperty("OutputWidth").GetUInt32()}x{info.GetProperty("OutputHeight").GetUInt32()}-B8G8R8A8Unorm.bgra");
        if (!Path.GetFullPath(image).Equals(expectedImage, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native screenshot is not the selected audit artifact");
        LightingReference.Compare(inputs, image, raster);
        using var lighting = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "lighting-reference-report.json")));
        if (!lighting.RootElement.GetProperty("InteriorLightingMatches").GetBoolean()) throw new InvalidOperationException("Native presented interior pixels differ from independent reference");
        var bad = File.ReadAllBytes(image); var sample = lighting.RootElement.GetProperty("FirstCheckedPixel").GetInt32();
        for (var c = 0; c < 3; c++) bad[sample * 4 + c] = (byte)(255 - bad[sample * 4 + c]);
        var control = Path.Combine(root, "pixel-negative"); Directory.CreateDirectory(control);
        var badFile = Path.Combine(control, Path.GetFileName(image)); File.WriteAllBytes(badFile, bad);
        LightingReference.Compare(inputs, badFile, raster);
        using var rejected = JsonDocument.Parse(File.ReadAllText(Path.Combine(control, "lighting-reference-report.json")));
        if (rejected.RootElement.GetProperty("InteriorLightingMatches").GetBoolean()) throw new InvalidOperationException("Corrupt native image negative control passed");
    }

    private static void VerifyProgram(Gen5ShaderReplaySnapshot snapshot, int length, string expectedHash)
    {
        if (snapshot.Schema != 1) throw new InvalidOperationException("Unsupported native snapshot");
        var bytes = new byte[length]; var covered = new bool[length/4];
        foreach (var instruction in snapshot.Instructions)
            for (var word = 0; word < instruction.Words.Length; word++)
            {
                var offset = (ulong)instruction.Pc + (uint)word * 4;
                if (offset % 4 != 0 || offset + 4 > (ulong)length) throw new InvalidOperationException("Unexpected native program extent");
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)offset), instruction.Words[word]);
                covered[(int)offset/4] = true;
            }
        if (covered.Any(v => !v) || Convert.ToHexString(SHA256.HashData(bytes)) != expectedHash)
            throw new InvalidOperationException("Native program is not the pinned original shader");
    }
}
