// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

if (args.Length == 3 && args[0] == "--lighting-reference")
{
    LightingReference.Compare(args[1], args[2]);
    return;
}

if (args.Length == 4 && args[0] == "--graphics")
{
    GraphicsReplay.Run(args[1], args[2], args[3]);
    return;
}

if (args.Length == 3 && args[0] == "--consume-mesh")
{
    MeshReplayReference.Consume(args[1], args[2]);
    return;
}

if (args.Length is < 3 or > 4 || (args.Length == 4 && args[3] is not "--nonempty" and not "--mesh"))
    throw new ArgumentException("usage: GearsComputeAudit <replay.json> <original-gears-gs.bin> <output-directory> [--nonempty|--mesh]");
var nonempty = args.Length == 4;
var mesh = args.Length == 4 && args[3] == "--mesh";
using var envelope = JsonDocument.Parse(File.ReadAllText(args[0]));
var snapshot = envelope.RootElement.GetProperty("Snapshot").Deserialize<Gen5ShaderReplaySnapshot>()!;
if (snapshot.Schema != 1) throw new InvalidOperationException("Unsupported replay schema");
var original = File.ReadAllBytes(args[1]);
var shaderHash = Convert.ToHexString(SHA256.HashData(original));
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "gears_lit.manifest.json")));
var expectedHash = manifest.RootElement.GetProperty("stages").GetProperty("pre_raster_gs").GetProperty("sha256").GetString();
if (!shaderHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Original Gears stage hash mismatch");
foreach (var instruction in snapshot.Instructions)
    for (var index = 0; index < instruction.Words.Length; index++)
    {
        var offset = checked((int)instruction.Pc + index * 4);
        if (offset < 0 || offset + 4 > original.Length || BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(offset)) != instruction.Words[index])
            throw new InvalidOperationException($"Captured instruction differs from original stage at {offset:X}");
    }
if (!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ShaderMemory(snapshot.ShaderAddress, original), Generation.Gen5),
    snapshot.ShaderAddress, out var program, out var decodeError)) throw new InvalidOperationException(decodeError);
if (program!.Instructions.Count != snapshot.Instructions.Length) throw new InvalidOperationException("Decoded coverage differs from captured program");
var directory = Path.GetFullPath(args[2]);
Directory.CreateDirectory(directory);
// Default is empty compiler coverage; nonempty modes require captured draw state.
Gen5ShaderReplayDrawContext? draw = null;
if (nonempty)
{
    draw = envelope.RootElement.GetProperty("DrawContext").Deserialize<Gen5ShaderReplayDrawContext>();
    // This bounded first-draw audit has one explicit triangle, not a general scheduler.
    if (draw is null || (!mesh && draw.DrawCount != 3) || draw.InstanceCount != 1 || draw.PrimitiveType != 4 ||
        draw.Indexed || draw.BaseVertex != 0 || draw.IndexCaptureStatus != "not-indexed")
        throw new InvalidOperationException("Nonempty audit requires captured nonindexed triangle-list draw state");
}
var batch = mesh ? MeshReplayReference.Partition(draw!) : nonempty
    ? Gen5NggPassthroughBatch.Create([(new(3, 1, 32, 128, 0),
        new Gen5NggSourceVertex[] { new(0, 0), new(1, 0), new(2, 0) },
        new Gen5NggSourceTriangle[] { new(0, 0, 1, 2) })])
    : Gen5NggPassthroughBatch.Create([(new(0, 0, 32, 128, 0), [], [])]);
var slots = batch.OutputSlotCapacity;
var primitiveFirst = checked((int)slots * 12);
var descriptorFirst = checked(primitiveFirst + (int)slots * 5);
var sourceFirst = checked(descriptorFirst + batch.GroupCount * 4);
var auxiliary = new byte[(sourceFirst + (int)slots * 9 + 16) * 4];
for (var index = 0; index < auxiliary.Length / 4; index++)
    BinaryPrimitives.WriteUInt32LittleEndian(auxiliary.AsSpan(index * 4), 0xCAFEBABE);
for (var index = 0; index < batch.Descriptors.Length; index++)
    BinaryPrimitives.WriteUInt32LittleEndian(auxiliary.AsSpan((descriptorFirst + index) * 4), batch.Descriptors.Span[index]);
for (var index = 0; index < batch.SourceWords.Length; index++)
    BinaryPrimitives.WriteUInt32LittleEndian(auxiliary.AsSpan((sourceFirst + index) * 4), batch.SourceWords.Span[index]);
var bindings = snapshot.GlobalMemoryBindings.ToList();
// Graphics attribute views must become actual read-only storage bindings for
// compute fetches. Keep original bytes, addresses, strides and instruction
// identity; never evaluate vertex values on CPU or rewrite the guest shader.
foreach (var stream in snapshot.VertexInputs)
{
    var pcs = new[] { stream.Pc }.Concat(stream.AliasPcs ?? []).Distinct().ToArray();
    foreach (var pc in pcs)
    {
        var instruction = program.Instructions.Single(i => i.Pc == pc);
        if (instruction.Control is not Gen5BufferMemoryControl control)
            throw new InvalidOperationException($"Captured vertex view is not a buffer fetch at {pc:X}");
        bindings.Add(new(control.ScalarResource, stream.BaseAddress, [pc], stream.Data.ToArray(), stream.DataLength, false) {
            Writable = false, WriteBackToGuest = false
        });
    }
}
var outputBinding = bindings.Count;
bindings.Add(new(0, 0x300000000, [], auxiliary, auxiliary.Length, false) { Writable = true, WriteBackToGuest = false });
var state = new Gen5ShaderState(program, snapshot.UserData, snapshot.Metadata, snapshot.ComputeSystemRegisters, snapshot.UserDataScalarRegisterBase);
var evaluation = new Gen5ShaderEvaluation(snapshot.InitialScalarRegisters, snapshot.ScalarRegisters, snapshot.ImageBindings,
    bindings, snapshot.EvaluationComputeSystemRegisters, snapshot.RuntimeScalarRegisters?.ToHashSet(), snapshot.VertexInputs);
var success = Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 128, 1, 1, out var shader, out var error,
    exportOutput: new(outputBinding, slots, 2), triangleOutput: new(outputBinding, (uint)primitiveFirst, slots, 0),
    nggBatchInputs: new(outputBinding, (uint)descriptorFirst, outputBinding, (uint)sourceFirst, (uint)batch.GroupCount, 128, 32));
if (success) File.WriteAllBytes(Path.Combine(directory, "original-gears-compute.spv"), shader.Spirv);
if (success && nonempty)
{
    uint[] Words(byte[] bytes, int length)
    {
        if (length % 4 != 0) throw new InvalidOperationException("Captured binding is not word aligned");
        var words = new uint[length / 4];
        for (var index = 0; index < words.Length; index++)
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4));
        return words;
    }
    var floatWords = Array.Empty<int>();
    uint[]? meshExpected = null;
    if (mesh) (meshExpected, floatWords) = MeshReplayReference.Evaluate(snapshot, draw!, Words(auxiliary, auxiliary.Length), primitiveFirst);
    File.WriteAllText(Path.Combine(directory, "storage-fixture.json"), JsonSerializer.Serialize(new {
        Buffers = bindings.Select(b => Words(b.Data, b.DataLength)).ToArray(),
        OutputBinding = outputBinding, GroupsX = (uint)batch.GroupCount,
        FloatWords = floatWords, FloatTolerance = mesh ? 0.00002 : 0
    }));
    File.WriteAllText(Path.Combine(directory, "initial.json"), JsonSerializer.Serialize(Words(auxiliary, auxiliary.Length)));
    // Unchanged-output negative control, explicitly not a numerical shader oracle.
    File.WriteAllText(Path.Combine(directory, "unchanged-control.json"), JsonSerializer.Serialize(Words(auxiliary, auxiliary.Length)));
    // Independent reference for the pinned native clear draw: identity MVP/quaternion,
    // original fullscreen triangle, +Z normals and black material. Do not derive
    // expected values from the emitted SPIR-V or GPU readback.
    if (!mesh)
    {
    var one = BitConverter.SingleToUInt32Bits(1f);
    if (snapshot.UserData.Length < 25 ||
        !snapshot.UserData.Skip(1).Take(16).SequenceEqual(Enumerable.Range(0, 16).Select(i => i % 5 == 0 ? one : 0u)) ||
        !snapshot.UserData.Skip(17).Take(4).SequenceEqual(new uint[] { 0, 0, 0, one }) ||
        !snapshot.UserData.Skip(21).Take(4).SequenceEqual(new uint[] { 0, 0, 0, one }))
        throw new InvalidOperationException("Numerical oracle supports only the captured identity/black clear draw");
    float[][] clearPositions = [[-1, -1, 1], [3, -1, 1], [-1, 3, 1]];
    var stream = snapshot.VertexInputs.Single(v => v.Pc == 0x78);
    if (stream.Stride != 24 || stream.OffsetBytes != 0 || stream.DataLength != 72)
        throw new InvalidOperationException("Unexpected clear vertex extent");
    for (var vertex = 0; vertex < 3; vertex++)
        for (var component = 0; component < 6; component++)
        {
            var value = component < 3 ? clearPositions[vertex][component] : component == 5 ? 1f : 0f;
            if (BinaryPrimitives.ReadUInt32LittleEndian(stream.Data.AsSpan(vertex * 24 + component * 4)) != BitConverter.SingleToUInt32Bits(value))
                throw new InvalidOperationException("Captured geometry differs from native clear-draw reference");
        }
    var expected = Words(auxiliary, auxiliary.Length);
    for (var vertex = 0; vertex < 3; vertex++)
    {
        uint[] exports = [BitConverter.SingleToUInt32Bits(clearPositions[vertex][0]),
            BitConverter.SingleToUInt32Bits(clearPositions[vertex][1]), one, one,
            0, 0, one, 0, 0, 0];
        Array.Copy(exports, 0, expected, vertex * 12, exports.Length);
    }
    uint[] triangle = [0x00200400, 0, 1, 2, 1];
    Array.Copy(triangle, 0, expected, primitiveFirst, triangle.Length);
    File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected));
    }
    else File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(meshExpected));
    File.WriteAllText(Path.Combine(directory, "limits.json"), JsonSerializer.Serialize(new uint[] { slots, 1, 1 }));
}
File.WriteAllText(Path.Combine(directory, "audit-report.json"), JsonSerializer.Serialize(new {
    Scope = mesh ? "Original Gears captured mesh; explicit audit partitions, independent GLSL numerical reference; no hardware scheduling or rendering implied" : nonempty ? "Original Gears first triangle inputs; nonempty compute fixture only; unchanged-control is not a numerical oracle; GPU execution not implied"
        : "Original Gears stage/captured backend inputs; empty subgroup for compiler audit only; no GPU or scene execution",
    DrawContext = draw,
    GroupCount = batch.GroupCount, OutputSlots = slots,
    OriginalShaderSha256 = shaderHash, ReplaySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),
    Instructions = program.Instructions.Count, Globals = snapshot.GlobalMemoryBindings.Length, VertexInputs = snapshot.VertexInputs.Length,
    ReadOnlyVertexStorageViews = bindings.Count - snapshot.GlobalMemoryBindings.Length - 1,
    CompilerAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gen5SpirvTranslator).Assembly.Location))),
    CompilerPassed = success, CompilerError = error, GpuExecuted = false, RenderingVerified = false
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(success ? "Compiler coverage passed; no GPU execution" : $"Original shader compiler blocker: {error}");
Environment.ExitCode = success ? 0 : 2;

internal sealed class ShaderMemory(ulong address, byte[] bytes) : ICpuMemory
{
    public bool TryRead(ulong location, Span<byte> destination)
    {
        if (location < address || location - address > (ulong)bytes.Length ||
            (ulong)destination.Length > (ulong)bytes.Length - (location - address)) return false;
        bytes.AsSpan((int)(location - address), destination.Length).CopyTo(destination); return true;
    }
    public bool TryWrite(ulong location, ReadOnlySpan<byte> source) => false;
}
