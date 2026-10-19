// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

internal static class NativeFrameAudit
{
    public static void Run(string root)
    {
        var directories = Directory.GetDirectories(root, "draw-*").OrderBy(d => d, StringComparer.Ordinal).ToArray();
        if (directories.Length != 4) throw new InvalidOperationException("Exactly four native frame draws required");
        uint[] counts = [3, 1200, 600, 600];
        ulong? firstSequence = null;
        var reports = new List<object>();
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
            var wrongPosition = actual.ToArray();
            wrongPosition[0] = BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(wrongPosition[0]) + 1f);
            var stalePrimitive = actual.ToArray(); stalePrimitive[primitiveFirst + 4] = 2;
            if (Mismatches(wrongPosition) == 0 || Mismatches(stalePrimitive) == 0)
                throw new InvalidOperationException("Numerical/primitive negative control unexpectedly accepted");
            reports.Add(new { Sequence = context.DrawSequence, context.DrawCount, ComparedWords = actual.Length,
                FloatWords = floatWords.Length, IndexCount = indices.Length, Mismatches = failures,
                WrongPositionRejected = true, StalePrimitiveRejected = true });
        }
        File.WriteAllText(Path.Combine(root, "native-numerical-report.json"), JsonSerializer.Serialize(new {
            Scope = "Completed live native Gears GS exports versus independent double-precision GLSL equations and triangle-list connectivity; no re-dispatch or numerical screenshot validation",
            FrameOrdinal = (firstSequence!.Value-1)/4, Draws = reports, NumericalExportsVerified = true,
            ConsumedIndicesVerified = true, NativeImagePixelsVerified = false
        }));
        Console.WriteLine($"PASS native frame {(firstSequence!.Value-1)/4}: four completed original GS outputs and consumed indices match independent reference");
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
