// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--omit-count-inputs"))
    throw new ArgumentException("usage: NggCountsDump <directory> [--omit-count-inputs]");
var directory = Path.GetFullPath(args[0]);
var negative = args.Length == 2;
uint[] words = [0x7E080202, 0x7E0A0203, 0x7E0C0208, 0x7E0E0300,
    0xF8000A0F, 0x07060504, 0xBF810000]; // v4=s2,v5=s3,v6=s8,v7=v0; EXP param0
var memory = new ShaderMemory(words);
if (!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(memory, Generation.Gen5), ShaderMemory.Address,
    out var program, out var decodeError)) throw new InvalidOperationException(decodeError);
var cases = new List<(uint Wave, uint Threads, uint Vertices, uint Primitives, uint Ordered)>();
if (negative) cases.Add((32, 64, 33, 13, 17));
else
{
    foreach (uint vertices in new uint[] { 0, 13, 32, 33, 63, 64 }) cases.Add((32, 64, vertices, 13, 17));
    cases.Add((64, 128, 67, 65, 4095));
    cases.Add((32, 512, 511, 33, 4095));
}
foreach (var item in cases)
{
    var output = Path.Combine(directory, $"wave{item.Wave}-threads{item.Threads}-vertices{item.Vertices}");
    Directory.CreateDirectory(output);
    const uint sentinel = 0xCAFEBABE;
    const uint userData = 0x13579BDF;
    var expected = Enumerable.Repeat(sentinel, checked((int)item.Threads * 8 + 16)).ToArray();
    for (uint local = 0; local < item.Threads; local++)
    {
        var wave = local / item.Wave;
        var first = wave * item.Wave;
        // Independent reference from the pinned source's field layout; never
        // use the new packer or translator to generate expected words.
        var vertices = (uint)Math.Clamp((long)item.Vertices - first, 0, item.Wave);
        var primitives = (uint)Math.Clamp((long)item.Primitives - first, 0, item.Wave);
        var offset = local * 8 + 4;
        expected[offset] = (item.Primitives << 22) | (item.Vertices << 12) | item.Ordered;
        expected[offset + 1] = (wave << 24) | (primitives << 8) | vertices;
        expected[offset + 2] = userData;
        expected[offset + 3] = local;
    }
    var scalar = new uint[256];
    scalar[2] = 0xBAD00002; scalar[3] = 0xBAD00003; scalar[8] = userData;
    var bytes = expected.Length * 4;
    var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[bytes], bytes, false) { Writable = true };
    var state = new Gen5ShaderState(program!, [], null);
    var evaluation = new Gen5ShaderEvaluation(scalar, new uint[256], [], [binding]);
    var counts = new Gen5NggMergedGroup(item.Vertices, item.Primitives, item.Wave, item.Threads, item.Ordered);
    if (!Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, item.Threads, 1, 1,
        out var shader, out var error, waveLaneCount: item.Wave, exportOutput: new(0, item.Threads, 1),
        nggCountInputs: negative ? null : counts)) throw new InvalidOperationException(error);
    File.WriteAllBytes(Path.Combine(output, "counts.spv"), shader.Spirv);
    File.WriteAllBytes(Path.Combine(output, "guest-shader.bin"), memory.Bytes);
    File.WriteAllText(Path.Combine(output, "expected.json"), JsonSerializer.Serialize(expected));
    File.WriteAllText(Path.Combine(output, "generation-report.json"), JsonSerializer.Serialize(new {
        Scope = "Independent hand-assembled count reads through guest decode and real translator; not Gears",
        GuestDecoded = true, OmittedCountInputs = negative, GpuExecuted = false,
        item.Wave, item.Threads, item.Vertices, item.Primitives, item.Ordered,
        SpirvSha256 = Convert.ToHexString(SHA256.HashData(shader.Spirv))
    }));
    Console.WriteLine(output);
}

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
