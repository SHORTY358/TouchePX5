// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--negative-capacity"))
    throw new ArgumentException("usage: ComputeExportDump <output-directory> [--negative-capacity]");
var negativeCapacity = args.Length == 2;
var capacity = negativeCapacity ? 4u : 3u;
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
var program = new Gen5ShaderProgram(0,
 [Export(0, 12, 15), Exec(8, 128), Export(12, 32, 15), Exec(20, 193),
  Export(24, 32, 5), Export(32, 12, 0),
  new Gen5ShaderInstruction(40, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)]);
var expected = Enumerable.Repeat(sentinel, 40).ToArray();
for (uint record = 0; record < 3; record++)
{
    var offset = record * 8;
    expected[offset] = record; // exact compute local source invocation ID
    expected[offset + 1] = BitConverter.SingleToUInt32Bits(2);
    expected[offset + 2] = BitConverter.SingleToUInt32Bits(3);
    expected[offset + 3] = BitConverter.SingleToUInt32Bits(4);
    expected[offset + 4] = BitConverter.SingleToUInt32Bits(10);
    expected[offset + 6] = BitConverter.SingleToUInt32Bits(30);
}
var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[160], 160, false) { Writable = true };
var state = new Gen5ShaderState(program, [], null);
var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], [binding]);
if (!Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
    out var shader, out var error, exportOutput: new(0, capacity, 1))) throw new InvalidOperationException(error);
File.WriteAllBytes(Path.Combine(directory, "exports.spv"), shader.Spirv);
File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected));
File.WriteAllText(Path.Combine(directory, "shader-report.json"), JsonSerializer.Serialize(new {
    Scope="Actual translator compute EXP transport; synthetic IR, not original Gears shader",
    Sha256=Convert.ToHexString(SHA256.HashData(shader.Spirv)), RecordCapacity=capacity, ExpectedRecordCapacity=3,
    NegativeCapacityControl=negativeCapacity, RecordWords=8,
    LocalSize=64, DispatchGroups=2, GuardWords=16, DisabledParameterChannels=new[]{1,3}, GpuExecuted=false
}));
Console.WriteLine($"Generated actual compute export shader: {directory}");
