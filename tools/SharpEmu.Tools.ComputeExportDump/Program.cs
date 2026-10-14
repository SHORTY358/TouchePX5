// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

var allowed = new[] { "--negative-capacity", "--decode-triangle", "--multi-group", "--triangle", "--null-triangle", "--invalid-triangle", "--gap-triangle" };
if (args.Length < 1 || args.Skip(1).Any(arg => !allowed.Contains(arg)) ||
    args.Skip(1).Distinct().Count() != args.Length - 1 || args.Skip(1).Count(arg => arg is not "--negative-capacity" and not "--decode-triangle" and not "--multi-group") > 1)
    throw new ArgumentException("usage: ComputeExportDump <directory> [--negative-capacity] [--decode-triangle] [--multi-group] [--triangle|--null-triangle|--invalid-triangle|--gap-triangle]");
var negativeCapacity = args.Contains("--negative-capacity");
var decodedTriangle = args.Contains("--decode-triangle");
var triangleMode = args.Skip(1).FirstOrDefault(arg => arg is not "--negative-capacity" and not "--decode-triangle" and not "--multi-group");
if (decodedTriangle && triangleMode is null) throw new ArgumentException("--decode-triangle requires a triangle variant");
var expectedCapacity = args.Contains("--multi-group") ? 67u : 3u;
var capacity = expectedCapacity + (negativeCapacity ? 1u : 0u);
var triangleFirstWord = expectedCapacity * 8;
var directory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(directory);
const uint sentinel = 0xCAFEBABE;
Gen5Operand Literal(float value) => new(Gen5OperandKind.LiteralConstant, BitConverter.SingleToUInt32Bits(value));
Gen5ShaderInstruction Export(uint pc, uint target, uint mask) => new(pc, Gen5ShaderEncoding.Exp, "Exp", [],
    target == 12 ? [Gen5Operand.Vector(0), Literal(2), Literal(3), Literal(4)]
                 : [Literal(10), Literal(20), Literal(30), Literal(40)], [],
    new Gen5ExportControl(target, mask, false, false, false));
Gen5ShaderInstruction Exec(uint pc, uint source) => new(pc, Gen5ShaderEncoding.Sop1, "SMovB64", [],
    [Gen5Operand.Source(source)], [Gen5Operand.Scalar(126), Gen5Operand.Scalar(127)], null);
var instructions = new List<Gen5ShaderInstruction> {
    Export(0, 12, 15), Exec(8, 128), Export(12, 32, 15), Exec(20, 193),
    Export(24, 32, 5), Export(32, 12, 0)
};
var packed = triangleMode switch { "--null-triangle" => 0x80300400u,
    "--gap-triangle" => 0x60380600u, _ => 0x00300400u }; // indices 0,1,3
if (triangleMode is not null)
{
    Gen5ShaderInstruction Triangle(uint pc, uint value, uint mask) => new(pc, Gen5ShaderEncoding.Exp, "Exp", [],
        [new(Gen5OperandKind.LiteralConstant, value), Gen5Operand.Vector(1), Gen5Operand.Vector(2), Gen5Operand.Vector(3)], [],
        new Gen5ExportControl(20, mask, false, true, false));
    instructions.AddRange([Triangle(40, packed, 1), Exec(48, 128), Triangle(52, 0xDEADBEEF, 1),
        Exec(60, 193), Triangle(64, 0xDEADBEEF, 0)]);
}
instructions.Add(new(72, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null));
var program = new Gen5ShaderProgram(0, instructions);
if (decodedTriangle)
{
    uint[] words = [0x7E0002FF, packed, 0x7E0202FF, 0xDEADBEEF,
        0xF8000941, 0x00000000, 0xBEFE0380, 0xF8000941, 0x01010101,
        0xBEFE03C1, 0xF8000940, 0x01010101, 0xBF810000];
    var memory = new ShaderMemory(words);
    if (!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(memory, Generation.Gen5), ShaderMemory.Address,
        out var decoded, out var decodeError)) throw new InvalidOperationException(decodeError);
    program = decoded!;
    File.WriteAllBytes(Path.Combine(directory, "guest-shader.bin"), memory.Bytes);
}
var expected = Enumerable.Repeat(sentinel, checked((int)(triangleFirstWord +
    (triangleMode is null ? 0 : expectedCapacity * 5) + 16))).ToArray();
for (uint record = 0; !decodedTriangle && record < expectedCapacity; record++)
{
    var offset = record * 8;
    expected[offset] = record % 64; // exact compute local source invocation ID
    expected[offset + 1] = BitConverter.SingleToUInt32Bits(2);
    expected[offset + 2] = BitConverter.SingleToUInt32Bits(3);
    expected[offset + 3] = BitConverter.SingleToUInt32Bits(4);
    expected[offset + 4] = BitConverter.SingleToUInt32Bits(10);
    expected[offset + 6] = BitConverter.SingleToUInt32Bits(30);
}
if (triangleMode is not null)
    for (uint record = 0; record < expectedCapacity; record++)
    {
        var offset = triangleFirstWord + record * 5;
        expected[offset] = packed;
        expected[offset + 1] = 0;
        expected[offset + 2] = 1;
        expected[offset + 3] = 3;
        expected[offset + 4] = triangleMode is "--null-triangle" or "--invalid-triangle" ? 0u : 1u;
    }
var bytes = expected.Length * 4;
var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[bytes], bytes, false) { Writable = true };
var state = new Gen5ShaderState(program, [], null);
var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], [binding]);
if (!Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
    out var shader, out var error, exportOutput: decodedTriangle ? null : new(0, triangleMode is null ? capacity : expectedCapacity, 1),
    triangleOutput: triangleMode is null ? null : new(0, triangleFirstWord, capacity, triangleMode == "--invalid-triangle" ? 3u : 4u)))
    throw new InvalidOperationException(error);
File.WriteAllBytes(Path.Combine(directory, "exports.spv"), shader.Spirv);
File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected));
File.WriteAllText(Path.Combine(directory, "shader-report.json"), JsonSerializer.Serialize(new {
    Scope=decodedTriangle ? "Hand-assembled guest words through actual decode and translator; not Gears"
        : "Actual translator compute EXP transport; synthetic IR, not original Gears shader",
    GuestDecoded=decodedTriangle,
    Sha256=Convert.ToHexString(SHA256.HashData(shader.Spirv)), RecordCapacity=capacity, ExpectedRecordCapacity=expectedCapacity,
    NegativeCapacityControl=negativeCapacity, RecordWords=8, TriangleMode=triangleMode,
    TriangleFirstWord=triangleFirstWord, TriangleRecordWords=5,
    TriangleLiveVertexCount=triangleMode == "--invalid-triangle" ? 3u : 4u,
    LocalSize=64, DispatchGroups=2, GuardWords=16, DisabledParameterChannels=new[]{1,3}, GpuExecuted=false
}));
Console.WriteLine($"Generated actual compute export shader: {directory}");

internal sealed class ShaderMemory(uint[] words) : ICpuMemory
{
    public const ulong Address = 0x100000;
    public byte[] Bytes { get; } = Encode(words);
    private static byte[] Encode(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        return bytes;
    }
    public bool TryRead(ulong address, Span<byte> destination)
    {
        if (address < Address || address - Address > (ulong)Bytes.Length ||
            (ulong)destination.Length > (ulong)Bytes.Length - (address - Address)) return false;
        Bytes.AsSpan((int)(address - Address), destination.Length).CopyTo(destination);
        return true;
    }
    public bool TryWrite(ulong address, ReadOnlySpan<byte> source) => false;
}
