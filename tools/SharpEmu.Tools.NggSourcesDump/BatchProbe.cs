// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

internal static class BatchProbe
{
    public static void Generate(string directory, string variant)
    {
        Directory.CreateDirectory(directory);
        var batch = Gen5NggPassthroughBatch.Create([
            (new(4, 1, 32, 64, 17), [new(42, 7), new(10, 8), new(900, 9), new(3, 10)], [new(1234, 0, 1, 3)]),
            (new(2, 1, 32, 64, 18), [new(100, 3), new(200, 3)], [new(5678, 0, 1, 1)])
        ]);
        const int primitiveFirst = 128 * 8;
        const int descriptorFirst = primitiveFirst + 128 * 5;
        const int sourceFirst = descriptorFirst + 8;
        const int completeWords = sourceFirst + 128 * 9 + 16;
        var truncated = variant == "--batch-truncate";
        var bad = variant == "--batch-bad-descriptor";
        var badOutput = variant == "--batch-bad-output";
        var badCount = variant == "--batch-bad-count";
        var partialLimit = variant == "--batch-partial-limit";
        var length = truncated ? sourceFirst + 128 * 9 - 1 : completeWords;
        var initial = Enumerable.Repeat(0xCAFEBABEu, length).ToArray();
        batch.Descriptors.Span.CopyTo(initial.AsSpan(descriptorFirst));
        batch.SourceWords.Span[..Math.Min(batch.SourceWords.Length, length - sourceFirst)].CopyTo(initial.AsSpan(sourceFirst));
        if (bad) initial[descriptorFirst + 5]++; // group one's source offset borrows a neighboring word
        if (badOutput) initial[descriptorFirst + 6] = 0; // group one borrows group zero's output
        if (badCount) initial[descriptorFirst + 4] = 0x00441012; // 65 vertices exceed 64 local threads
        var expected = initial.ToArray();
        for (var group = 0; group < 2; group++)
        {
            if (group == 1 && (bad || truncated || badOutput || badCount || partialLimit)) continue;
            for (var local = 0; local < 64; local++)
            {
                uint packed = local == 0 ? (group == 0 ? 0x00300400u : 0x00100400u) : 0;
                uint primitive = local == 0 ? (group == 0 ? 1234u : 5678u) : 0;
                uint vertex = group == 0 ? local switch { 0 => 42u, 1 => 10u, 2 => 900u, 3 => 3u, _ => 0u }
                    : local switch { 0 => 100u, 1 => 200u, _ => 0u };
                uint instance = group == 0 ? (local < 4 ? (uint)local + 7 : 0) : (local < 2 ? 3u : 0);
                uint info = group == 0 ? 0x00404011u : 0x00402012u;
                uint wave = local < 32 ? (group == 0 ? 0x104u : 0x102u) : 0x01000000u;
                var record = group * 64 + local;
                uint[] values = [packed, 0, primitive, 0, vertex, instance, info, wave];
                Array.Copy(values, 0, expected, record * 8, 8);
                // In this audit shader every lane intentionally exports, even
                // idle degenerate records. A real guest predicates its exports.
                uint[] triangle = [packed, 0, local == 0 ? 1u : 0, local == 0 ? (group == 0 ? 3u : 1u) : 0, 1];
                Array.Copy(triangle, 0, expected, primitiveFirst + record * 5, 5);
            }
        }
        uint[] words = [0x7E080305, 0x7E0C0202, 0x7E0E0203, 0xF80000CF, 0x03020100,
            0xF800020F, 0x07060804, 0xBE820380, 0xF8000941, 0x00000000, 0xBF8A0000, 0xBF810000];
        var memory = new ShaderMemory(words);
        if (!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(memory, Generation.Gen5), ShaderMemory.Address,
            out var program, out var decodeError)) throw new InvalidOperationException(decodeError);
        var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[completeWords * 4], completeWords * 4, false) { Writable = true };
        if (!Gen5SpirvTranslator.TryCompileComputeShader(new(program!, [], null),
            new(new uint[256], new uint[256], [], [binding]), 64, 1, 1, out var shader, out var error,
            exportOutput: new(0, 128, 1), triangleOutput: new(0, primitiveFirst, 128, 0),
            nggBatchInputs: new(0, descriptorFirst, 0, sourceFirst, 2, 64, 32))) throw new InvalidOperationException(error);
        File.WriteAllBytes(Path.Combine(directory, "sources.spv"), shader.Spirv);
        File.WriteAllBytes(Path.Combine(directory, "guest-shader.bin"), memory.Bytes);
        File.WriteAllText(Path.Combine(directory, "initial.json"), JsonSerializer.Serialize(initial));
        File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected));
        File.WriteAllText(Path.Combine(directory, "limits.json"), JsonSerializer.Serialize(new uint[] { partialLimit ? 127u : uint.MaxValue, 1, 1 }));
        File.WriteAllText(Path.Combine(directory, "generation-report.json"), JsonSerializer.Serialize(new {
            Scope = "Independent two-group descriptor/count/source/export audit; no graphics draw or Gears",
            Variant = variant, GpuExecuted = false, GuestDecoded = true, ShaderClobbersS2BeforePrimitiveExport = true
        }));
        Console.WriteLine(directory);
    }
}
