// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.Libs.Gpu;
using SharpEmu.Libs.Gpu.Vulkan;
using SharpEmu.Libs.Kernel;
using SharpEmu.Libs.VideoOut;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

namespace SharpEmu.Libs.Agc;

public static partial class AgcExports
{
    private sealed class GearsScratch { public ulong Address; public bool InUse; }
    private static readonly ConditionalWeakTable<ICpuMemory, List<GearsScratch>> GearsScratchPools = new();
    private sealed class GearsComputeCache
    {
        public readonly Dictionary<string, byte[]> Programs = new(StringComparer.Ordinal);
        public int Bytes;
    }
    private static readonly ConditionalWeakTable<ICpuMemory, GearsComputeCache> GearsComputeCaches = new();

    private static bool IsPinnedGearsProgram(Gen5ShaderProgram program, int length, string hash)
    {
        var bytes = new byte[length]; var covered = new bool[length / 4];
        foreach (var instruction in program.Instructions)
            for (var word = 0; word < instruction.Words.Count; word++)
            {
                var offset = (ulong)instruction.Pc + (uint)word * 4;
                if (offset % 4 != 0 || offset + 4 > (ulong)length) return false;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)offset), instruction.Words[word]);
                covered[(int)offset / 4] = true;
            }
        return covered.All(v => v) && Convert.ToHexString(SHA256.HashData(bytes)) == hash;
    }

    private static bool TryScheduleGearsNgg(CpuContext ctx, SubmittedDcbState state,
        Gen5ShaderState exportState, Gen5ShaderEvaluation exportEvaluation,
        Gen5ShaderState pixelState, Gen5ShaderEvaluation pixelEvaluation,
        RenderTargetDescriptor[] targets, uint colorMasks, uint vertexCount, bool indexed,
        uint inputEnable, uint inputAddress, IReadOnlyList<uint> inputControl, out string error)
    {
        error = string.Empty;
        var gpu = GuestGpu.Current;
        if (gpu is not VulkanGuestGpuBackend || indexed || state.InstanceCount != 1 || GetBaseVertex(state) != 0 ||
            vertexCount == 0 || vertexCount > 2394 || vertexCount % 3 != 0 || targets.Length != 1 ||
            targets[0].Slot != 0 || targets[0].Format != 10 || targets[0].NumberType != 0 ||
            !state.UcRegisters.TryGetValue(VgtPrimitiveType, out var topology) || topology != 4 ||
            exportState.UserDataScalarRegisterBase != 8 || pixelEvaluation.ImageBindings.Count != 0 ||
            pixelEvaluation.GlobalMemoryBindings.Count != 0 || exportEvaluation.ImageBindings.Count != 0 ||
            exportEvaluation.GlobalMemoryBindings.Any(b => b.Writable) ||
            !IsPinnedGearsProgram(exportState.Program, 384, "79C8446ECA71AA98644D0C3D8B737875884F101E34EB308EC029CA1FCCB03492") ||
            !IsPinnedGearsProgram(pixelState.Program, 160, "6C12F9DFD892AF78FF5DAE047EEFB02B4ED27C93FD20A87BA763ACC5F3BD2E6C")) return false;

        var snapshot = Gen5ShaderReplaySnapshot.Create(exportState, exportEvaluation);
        var pixelSnapshot = Gen5ShaderReplaySnapshot.Create(pixelState, pixelEvaluation);
        var batch = Gen5NggPassthroughBatch.CreateNonindexedTriangleList(vertexCount);
        var slots = batch.OutputSlotCapacity;
        var primitiveFirst = checked((int)slots * 12);
        var descriptorFirst = checked(primitiveFirst + (int)slots * 5);
        var sourceFirst = descriptorFirst + batch.GroupCount * 4;
        var auxiliary = new byte[(sourceFirst + batch.SourceWords.Length + 16) * 4];
        for (var word = 0; word < auxiliary.Length / 4; word++) BinaryPrimitives.WriteUInt32LittleEndian(auxiliary.AsSpan(word * 4), 0xCAFEBABE);
        for (var word = 0; word < batch.Descriptors.Length; word++) BinaryPrimitives.WriteUInt32LittleEndian(auxiliary.AsSpan((descriptorFirst + word) * 4), batch.Descriptors.Span[word]);
        for (var word = 0; word < batch.SourceWords.Length; word++) BinaryPrimitives.WriteUInt32LittleEndian(auxiliary.AsSpan((sourceFirst + word) * 4), batch.SourceWords.Span[word]);
        var pool = GearsScratchPools.GetValue(ctx.Memory, _ => new());
        GearsScratch? scratch;
        lock (pool)
        {
            scratch = pool.FirstOrDefault(s => !s.InUse);
            if (scratch is null && pool.Count < 8 && KernelMemoryCompatExports.TryAllocateHleData(ctx, 0x40000, 256, out var address))
            { scratch = new() { Address = address }; pool.Add(scratch); }
            if (scratch is null) { error = "gears-ngg bounded scratch pool unavailable"; return false; }
            scratch.InUse = true;
        }
        var submitted = false;
        try
        {
            if (!ctx.Memory.TryWrite(scratch.Address, auxiliary)) { error = "gears-ngg scratch initialization failed"; return false; }
            var bindings = snapshot.GlobalMemoryBindings.ToList();
            foreach (var stream in snapshot.VertexInputs)
                foreach (var pc in new[] { stream.Pc }.Concat(stream.AliasPcs ?? []).Distinct())
                {
                    var instruction = exportState.Program.Instructions.Single(i => i.Pc == pc);
                    if (instruction.Control is not Gen5BufferMemoryControl control) { error = "gears-ngg unexpected input fetch"; return false; }
                    bindings.Add(new(control.ScalarResource, stream.BaseAddress, [pc], stream.Data, stream.DataLength, false) { Writable = false, WriteBackToGuest = false });
                }
            var outputBinding = bindings.Count;
            bindings.Add(new(0, scratch.Address, [], auxiliary, auxiliary.Length, false) { Writable = true, WriteBackToGuest = true });
            var evaluation = new Gen5ShaderEvaluation(snapshot.InitialScalarRegisters, snapshot.ScalarRegisters, snapshot.ImageBindings,
                bindings, snapshot.EvaluationComputeSystemRegisters, snapshot.RuntimeScalarRegisters?.ToHashSet(), snapshot.VertexInputs);
            var runtimeScalars = Environment.GetEnvironmentVariable("TOUCHEPX5_GEARS_RUNTIME_SCALARS") == "1";
            // This key is deliberately conservative: retain full binding addresses,
            // byte contents and layouts. Only initial SGPR VALUES are excluded,
            // because the runtime module loads those from the per-dispatch buffer.
            // The program bytes are already pinned above; store no borrowed
            // evaluation arrays, guest contexts or GPU handles in the cache.
            var cacheKey = runtimeScalars ? JsonSerializer.Serialize(new
            {
                vertexCount, _storageBufferOffsetAlignment, exportState.Program.Address,
                exportState.ComputeSystemRegisters, EvaluationSystem = snapshot.EvaluationComputeSystemRegisters,
                InitialCount = snapshot.InitialScalarRegisters.Length,
                Bindings = bindings.Select(b => new { b.ScalarAddress, b.BaseAddress, b.DataLength, b.Writable,
                    b.WriteBackToGuest, b.InstructionPcs, Hash = Convert.ToHexString(SHA256.HashData(b.Data.AsSpan(0,b.DataLength))) }),
                Views = snapshot.VertexInputs.Select(v => new { v.Pc, v.Location, v.ComponentCount, v.DataFormat,
                    v.NumberFormat, v.BaseAddress, v.Stride, v.OffsetBytes, v.PerInstance, v.AliasPcs })
            }) : null;
            var cache = GearsComputeCaches.GetValue(ctx.Memory, _ => new());
            byte[]? computeSpirv = null;
            if (cacheKey is not null) lock (cache) cache.Programs.TryGetValue(cacheKey, out computeSpirv);
            var cacheHit = computeSpirv is not null;
            bool CompileCurrent(out Gen5SpirvShader compiled, out string reason) =>
                Gen5SpirvTranslator.TryCompileComputeShader(exportState, evaluation, 128, 1, 1, out compiled, out reason,
                totalGlobalBufferCount: bindings.Count + (runtimeScalars ? 1 : 0),
                initialScalarBufferIndex: runtimeScalars ? bindings.Count : -1,
                exportOutput: new(outputBinding, slots, 2), triangleOutput: new(outputBinding, (uint)primitiveFirst, slots, 0),
                nggBatchInputs: new(outputBinding, (uint)descriptorFirst, outputBinding, (uint)sourceFirst, (uint)batch.GroupCount, 128, 32),
                storageBufferOffsetAlignment: _storageBufferOffsetAlignment);
            if (computeSpirv is null)
            {
                if (!CompileCurrent(out var compute, out error)) return false;
                computeSpirv = compute.Spirv;
                if (cacheKey is not null) lock (cache)
                {
                    if (cache.Programs.Count < 32 && computeSpirv.Length <= 8 * 1024 * 1024 - cache.Bytes && !cache.Programs.ContainsKey(cacheKey))
                    { cache.Programs.Add(cacheKey, computeSpirv); cache.Bytes += computeSpirv.Length; }
                }
            }
            else if (Environment.GetEnvironmentVariable("TOUCHEPX5_GEARS_VERIFY_TRANSLATION_CACHE") == "1")
            {
                if (!CompileCurrent(out var fresh, out error)) return false;
                if (!computeSpirv.AsSpan().SequenceEqual(fresh.Spirv))
                { error = "gears-ngg cached translation differs from fresh runtime translation"; return false; }
                Console.Error.WriteLine("[LOADER][INFO] agc.experimental_gears_ngg translation_cache_verified");
            }
            if (!gpu.TryCompilePixelShader(pixelState, pixelEvaluation, [new(0, 0, Gen5PixelOutputKind.Float)], out var pixel, out error,
                totalGlobalBufferCount: 1, scalarRegisterBufferIndex: 0, pixelInputEnable: inputEnable,
                pixelInputAddress: inputAddress, pixelInputCntl: inputControl.ToArray())) return false;
            var vertices = new byte[checked((int)slots * 48)];
            var bridgeInstructions = new List<Gen5ShaderInstruction>(); var views = new List<Gen5VertexInputBinding>();
            for (uint location = 0; location < 3; location++)
            {
                bridgeInstructions.Add(new(location * 4, Gen5ShaderEncoding.Mubuf, "BufferLoadFormatXyzw", [], [], [],
                    new Gen5BufferMemoryControl(4, 0, location * 4, 0, 0, true, false, false, false)));
                views.Add(new(location * 4, location, 4, 14, 7, scratch.Address, 48, location * 16, vertices, vertices.Length, false));
            }
            void Export(uint pc, uint target, uint first, uint mask) => bridgeInstructions.Add(new(pc, Gen5ShaderEncoding.Exp, "Exp", [],
                [Gen5Operand.Vector(first), Gen5Operand.Vector(first+1), Gen5Operand.Vector(first+2), Gen5Operand.Vector(first+3)], [], new Gen5ExportControl(target, mask, false, false, false)));
            Export(12, 12, 0, 15); Export(16, 32, 4, 15); Export(20, 33, 8, 3);
            bridgeInstructions.Add(new(24, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null));
            if (!gpu.TryCompileVertexShader(new(new(0, bridgeInstructions), [], null),
                new(new uint[256], new uint[256], [], [], VertexInputs: views), out var bridge, out error,
                totalGlobalBufferCount: 1, requiredVertexOutputCount: 2)) return false;
            var target = targets[0];
            var guestTargets = new GuestRenderTarget[] { new(target.Address, target.Width, target.Height, target.Format, target.NumberType,
                MipLevels: target.MipLevel+1, BaseArrayLayer: target.BaseArrayLayer, LayerCount: target.LayerCount,
                MipLevel: target.MipLevel, CompSwap: target.CompSwap, TileMode: target.TileMode) };
            var renderState = CreateRenderState(state.CxRegisters, targets, colorMasks);
            var depth = DecodeDepthTarget(state.CxRegisters);
            if (depth is not null && depth.Address != target.Address) VulkanVideoPresenter.DeclareFloatDepthBuffer(depth);
            var scalarBytes = new byte[256 * 4];
            for (var word = 0; word < pixelSnapshot.InitialScalarRegisters.Length; word++) BinaryPrimitives.WriteUInt32LittleEndian(scalarBytes.AsSpan(word * 4), pixelSnapshot.InitialScalarRegisters[word]);
            if (!ctx.Memory.TryWrite(scratch.Address + 0x3FC00, scalarBytes)) { error = "gears-ngg scalar initialization failed"; return false; }
            var drawSequence = state.ReplayDrawSequence;
            var computeBuffers = bindings.Select(b => new GuestMemoryBuffer(b.BaseAddress, b.Data, b.DataLength, false, b.Writable, b.WriteBackToGuest)).ToList();
            if (runtimeScalars)
            {
                var runtimeBytes = PackRuntimeScalarStateUnpooled(snapshot.InitialScalarRegisters, bindings);
                computeBuffers.Add(new(0, runtimeBytes, runtimeBytes.Length, false));
            }
            var computeSequence = gpu.SubmitComputeDispatch(exportState.Program.Address, new VulkanCompiledGuestShader(computeSpirv), [],
                computeBuffers,
                (uint)batch.GroupCount, 1, 1, 0, 0, 0, 128, 1, 1, false, true, slots, 1, 1);
            if (computeSequence <= 0) { error = "gears-ngg compute enqueue failed"; return false; }
            submitted = true; // From here, never recycle scratch on enqueue failure.
            var readback = gpu.SubmitOrderedGuestBufferReadback(scratch.Address, (ulong)auxiliary.Length, "gears-ngg-native-export-readback");
            if (readback <= computeSequence) { error = "gears-ngg readback enqueue failed"; return false; }
            var consume = VulkanVideoPresenter.SubmitOrderedDrawPreparation(() =>
            {
                try
                {
                    var result = new byte[auxiliary.Length];
                    if (!ctx.Memory.TryRead(scratch.Address, result)) throw new InvalidOperationException("completed NGG output unreadable");
                    result.AsSpan(0, vertices.Length).CopyTo(vertices);
                    var indices = new byte[checked((int)vertexCount * 4)]; var nextIndex = 0;
                    for (uint first = 0; first < vertexCount; first += 126)
                    {
                        var live = Math.Min(126u, vertexCount-first); var outputBase = first / 126 * 128;
                        for (uint triangle = 0; triangle < live / 3; triangle++)
                        {
                            var firstWord = primitiveFirst + (int)(outputBase+triangle) * 5;
                            var record = new uint[5];
                            for (var word = 0; word < 5; word++) record[word] = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan((firstWord+word)*4));
                            if (!Gen5NggTriangleRemap.TryRemap(record, live, outputBase, slots, out var remapped)) throw new InvalidOperationException("NGG primitive output not valid");
                            foreach (var id in new[] { remapped.Vertex0, remapped.Vertex1, remapped.Vertex2 })
                            { BinaryPrimitives.WriteUInt32LittleEndian(indices.AsSpan(nextIndex * 4), id); nextIndex++; }
                        }
                    }
                    gpu.SubmitOffscreenTranslatedDraw(pixel!, [], [new(scratch.Address+0x3FC00, scalarBytes, scalarBytes.Length, false)], 2,
                        guestTargets, bridge!, vertexCount, 1, 4, new(indices, indices.Length, true, false),
                        views.Select(v => new GuestVertexBuffer(v.Location,v.ComponentCount,v.DataFormat,v.NumberFormat,v.BaseAddress,v.Stride,v.OffsetBytes,vertices,vertices.Length,false)).ToArray(),
                        renderState, depth, pixelState.Program.Address);
                    Console.Error.WriteLine($"[LOADER][INFO] agc.experimental_gears_ngg consumed seq={drawSequence} vertices={vertexCount} triangles={vertexCount/3} target=0x{target.Address:X} size={target.Width}x{target.Height}");
                }
                catch (Exception exception) { Console.Error.WriteLine($"[LOADER][ERROR] agc.experimental_gears_ngg consume failed seq={drawSequence}: {exception.Message}"); }
                finally { lock (pool) scratch.InUse = false; }
            }, "gears-ngg-native-export-consume", guestTargets);
            if (consume <= readback) { error = "gears-ngg consume enqueue failed"; return false; }
            Console.Error.WriteLine($"[LOADER][INFO] agc.experimental_gears_ngg queued seq={drawSequence} compute={computeSequence} readback={readback} consume={consume} translation_cache={(cacheHit ? "hit" : "miss")}");
            return true;
        }
        finally { if (!submitted) lock (pool) scratch.InUse = false; }
    }
}
