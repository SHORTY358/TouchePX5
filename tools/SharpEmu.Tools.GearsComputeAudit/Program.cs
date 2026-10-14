// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

if (args.Length != 3) throw new ArgumentException("usage: GearsComputeAudit <replay.json> <original-gears-gs.bin> <output-directory>");
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
// Empty explicit subgroup for compiler coverage only. No guessed draw count,
// source IDs, matrix constants or host-produced scene geometry are executed.
var batch = Gen5NggPassthroughBatch.Create([(new(0, 0, 32, 128, 0), [], [])]);
const int primitiveFirst = 128 * 12;
const int descriptorFirst = primitiveFirst + 128 * 5;
const int sourceFirst = descriptorFirst + 4;
var auxiliary = new byte[(sourceFirst + 128 * 9 + 16) * 4];
for (var index = 0; index < batch.Descriptors.Length; index++)
    BinaryPrimitives.WriteUInt32LittleEndian(auxiliary.AsSpan((descriptorFirst + index) * 4), batch.Descriptors.Span[index]);
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
    exportOutput: new(outputBinding, 128, 2), triangleOutput: new(outputBinding, primitiveFirst, 128, 0),
    nggBatchInputs: new(outputBinding, descriptorFirst, outputBinding, sourceFirst, 1, 128, 32));
if (success) File.WriteAllBytes(Path.Combine(directory, "original-gears-compute.spv"), shader.Spirv);
File.WriteAllText(Path.Combine(directory, "audit-report.json"), JsonSerializer.Serialize(new {
    Scope = "Original Gears stage/captured backend inputs; empty subgroup for compiler audit only; no GPU or scene execution",
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
