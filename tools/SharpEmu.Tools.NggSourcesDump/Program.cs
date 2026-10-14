// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] is not "--omit-inputs" and not "--truncate"))
    throw new ArgumentException("usage: NggSourcesDump <directory> [--omit-inputs|--truncate]");
var directory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(directory);
var omitted = args.Contains("--omit-inputs");
var truncated = args.Contains("--truncate");
var counts = new Gen5NggMergedGroup(4, 1, 32, 64, 17);
var sources = Gen5NggPassthroughSources.Encode(counts,
    [new(42, 7), new(10, 8), new(900, 9), new(3, 10)], [new(1234, 0, 1, 3)]);
const uint sentinel = 0xCAFEBABE;
// Output deliberately has room for both dispatched groups. Source loading
// must reject group one; a smaller output capacity would hide that mistake.
const int firstWord = 128 * 12;
const int completeWords = firstWord + 64 * 9 + 16;
var actualWords = truncated ? firstWord + 64 * 9 - 1 : completeWords;
var initial = Enumerable.Repeat(sentinel, actualWords).ToArray();
Array.Copy(sources, 0, initial, firstWord, Math.Min(sources.Length, actualWords - firstWord));
var expected = initial.ToArray();
if (!truncated)
    for (var local = 0; local < 64; local++)
    {
        // Independent source-read reference, including caller IDs different
        // from local invocation IDs. Inspect all nine initialized VGPRs.
        uint vertex = local switch { 0 => 42, 1 => 10, 2 => 900, 3 => 3, _ => 0 };
        uint instance = local < 4 ? (uint)local + 7 : 0;
        uint[] values = [local == 0 ? 0x00300400u : 0, 0, local == 0 ? 1234u : 0, 0,
            0, vertex, 0, 0, instance, instance, instance, instance];
        Array.Copy(values, 0, expected, local * 12, 12);
    }
// Original decoder, three EXP reads of v0..v8, then a real workgroup barrier.
uint[] words = [0xF80000CF, 0x03020100, 0xF800020F, 0x07060504,
    0xF8000A1F, 0x08080808, 0xBF8A0000, 0xBF810000];
var memory = new ShaderMemory(words);
if (!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(memory, Generation.Gen5), ShaderMemory.Address,
    out var program, out var decodeError)) throw new InvalidOperationException(decodeError);
var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[completeWords * 4], completeWords * 4, false) { Writable = true };
if (!Gen5SpirvTranslator.TryCompileComputeShader(new(program!, [], null),
    new(new uint[256], new uint[256], [], [binding]), 64, 1, 1, out var shader, out var error,
    exportOutput: new(0, 128, 2), nggCountInputs: counts,
    nggSourceInputs: omitted ? null : new(0, firstWord))) throw new InvalidOperationException(error);
File.WriteAllBytes(Path.Combine(directory, "sources.spv"), shader.Spirv);
File.WriteAllBytes(Path.Combine(directory, "guest-shader.bin"), memory.Bytes);
File.WriteAllText(Path.Combine(directory, "initial.json"), JsonSerializer.Serialize(initial));
File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected));
File.WriteAllText(Path.Combine(directory, "generation-report.json"), JsonSerializer.Serialize(new {
    Scope = "Independent explicit single-subgroup passthrough source reads and barrier; not Gears",
    GuestDecoded = true, GpuExecuted = false, Omitted = omitted, Truncated = truncated, ActualWords = actualWords
}));
Console.WriteLine(directory);

internal sealed class ShaderMemory(uint[] words) : ICpuMemory
{
    public const ulong Address = 0x100000;
    public byte[] Bytes { get; } = Encode(words);
    private static byte[] Encode(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        return bytes;
    }
    public bool TryRead(ulong address, Span<byte> destination)
    {
        if (address < Address || address - Address > (ulong)Bytes.Length ||
            (ulong)destination.Length > (ulong)Bytes.Length - (address - Address)) return false;
        Bytes.AsSpan((int)(address - Address), destination.Length).CopyTo(destination); return true;
    }
    public bool TryWrite(ulong address, ReadOnlySpan<byte> source) => false;
}
