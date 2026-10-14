// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.Libs.Gpu;
using SharpEmu.Libs.Gpu.Vulkan;
using SharpEmu.ShaderCompiler;

// Uses the emulator's normal queue, storage allocations, GPU completion and
// selective guest-memory writeback. Caller is the tool thread, not its consumer.
internal static class QueuedComputeReplay
{
    public static void Run(string replayFile, string computeDirectory, string outputDirectory, bool partialLimit)
    {
        Directory.CreateDirectory(outputDirectory);
        using var replay = JsonDocument.Parse(File.ReadAllText(replayFile));
        using var audit = JsonDocument.Parse(File.ReadAllText(Path.Combine(computeDirectory, "audit-report.json")));
        if (!audit.RootElement.GetProperty("CompilerPassed").GetBoolean() ||
            audit.RootElement.GetProperty("ReplaySha256").GetString() != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(replayFile))))
            throw new InvalidOperationException("Compiled fixture does not match captured inputs");
        var snapshot = replay.RootElement.GetProperty("Snapshot").Deserialize<Gen5ShaderReplaySnapshot>()!;
        var fixture = JsonSerializer.Deserialize<QueueFixture>(File.ReadAllText(Path.Combine(computeDirectory, "storage-fixture.json")))!;
        var slots = audit.RootElement.GetProperty("OutputSlots").GetUInt32();
        if (snapshot.GlobalMemoryBindings.Length != 1 || snapshot.VertexInputs.Length != 2 ||
            fixture.Buffers.Length != 4 || fixture.OutputBinding != 3 || fixture.GroupsX is 0 or > 19)
            throw new InvalidOperationException("Queued audit supports only the retained bounded Gears fixture layout");
        ulong[] addresses = [snapshot.GlobalMemoryBindings[0].BaseAddress,
            snapshot.VertexInputs[0].BaseAddress, snapshot.VertexInputs[1].BaseAddress, 0x300000000];
        var memory = new OwnedReplayMemory();
        var buffers = new List<GuestMemoryBuffer>();
        for (var binding = 0; binding < fixture.Buffers.Length; binding++)
        {
            var bytes = new byte[fixture.Buffers[binding].Length * 4];
            for (var word = 0; word < fixture.Buffers[binding].Length; word++)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(word * 4), fixture.Buffers[binding][word]);
            memory.Add(addresses[binding], bytes);
            buffers.Add(new(addresses[binding], bytes, bytes.Length, false,
                Writable: binding == fixture.OutputBinding, WriteBackToGuest: binding == fixture.OutputBinding));
        }
        var outputAddress = addresses[fixture.OutputBinding];
        var outputBytes = buffers[fixture.OutputBinding].Length;
        var expected = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(computeDirectory, "expected.json")))!;
        if (partialLimit)
        {
            // Full final subgroup must reject uniformly before its real barrier.
            // Keep all earlier subgroup outputs and all source/descriptor data.
            var lastBase = checked((int)(slots - 128));
            var primitiveFirst = checked((int)slots * 12);
            Array.Copy(fixture.Buffers[fixture.OutputBinding], lastBase * 12, expected, lastBase * 12, 128 * 12);
            Array.Copy(fixture.Buffers[fixture.OutputBinding], primitiveFirst + lastBase * 5,
                expected, primitiveFirst + lastBase * 5, 128 * 5);
        }
        var floatWords = fixture.FloatWords?.ToHashSet() ?? [];
        var gpu = GuestGpu.Current;
        gpu.AttachGuestMemory(memory);
        gpu.EnsureStarted(640, 360);
        using var queue = gpu.EnterGuestQueue("audit.gears.ngg", 1);
        var shader = new VulkanCompiledGuestShader(File.ReadAllBytes(Path.Combine(computeDirectory, "original-gears-compute.spv")));
        var dispatchSequence = gpu.SubmitComputeDispatch(snapshot.ShaderAddress, shader, [], buffers,
            fixture.GroupsX, 1, 1, 0, 0, 0, 128, 1, 1, false, true,
            threadCountX: partialLimit ? slots - 1 : slots, threadCountY: 1, threadCountZ: 1);
        var readbackSequence = gpu.SubmitOrderedGuestBufferReadback(outputAddress, (ulong)outputBytes, "gears-ngg-export-consume");
        if (dispatchSequence <= 0 || readbackSequence <= dispatchSequence || !gpu.WaitForGuestWork(readbackSequence, 20000))
            throw new InvalidOperationException("Queued compute producer/readback did not complete");
        var actualBytes = new byte[outputBytes];
        if (!memory.TryRead(outputAddress, actualBytes)) throw new InvalidOperationException("Completed guest output unreadable");
        var actual = new uint[actualBytes.Length / 4];
        var failures = 0;
        for (var word = 0; word < actual.Length; word++)
        {
            actual[word] = BinaryPrimitives.ReadUInt32LittleEndian(actualBytes.AsSpan(word * 4));
            if (actual[word] == expected[word]) continue;
            if (floatWords.Contains(word))
            {
                var a = BitConverter.UInt32BitsToSingle(actual[word]); var e = BitConverter.UInt32BitsToSingle(expected[word]);
                if (float.IsFinite(a) && float.IsFinite(e) && Math.Abs((double)a-e) <= fixture.FloatTolerance * Math.Max(1, Math.Abs((double)e))) continue;
            }
            failures++;
        }
        File.WriteAllText(Path.Combine(outputDirectory, "actual.json"), JsonSerializer.Serialize(actual));
        if (failures == 0 && !partialLimit) MeshReplayReference.Consume(replayFile, outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "queued-report.json"), JsonSerializer.Serialize(new {
            Scope = "Original captured Gears GS through emulator queue, real GPU completion and selective owned guest-memory writeback; not native game execution",
            DispatchSequence = dispatchSequence, ReadbackSequence = readbackSequence,
            PartialFinalGroupLimit = partialLimit, ComparedWords = actual.Length, Mismatches = failures,
            GuestOutputWriteCalls = memory.OutputWriteCalls, GpuCompletedAndReadBack = true,
            NumericalOutputsVerified = failures == 0 && memory.OutputWriteCalls > 0,
            ConnectivityRemapVerified = failures == 0 && !partialLimit,
            Hashes = new[] { "actual.json", "remapped-indices.json" }.Where(n => File.Exists(Path.Combine(outputDirectory,n)))
                .Select(n => new { File = n, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(outputDirectory,n)))) }).ToArray(),
            NativeGearsRenderingVerified = false,
            ReplaySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(replayFile))),
            ShaderSha256 = Convert.ToHexString(SHA256.HashData(shader.Spirv))
        }));
        if (failures != 0 || memory.OutputWriteCalls == 0) throw new InvalidOperationException($"Queued guest output mismatch: {failures}; writes={memory.OutputWriteCalls}");
        Console.WriteLine($"PASS original Gears queued compute/readback: {actual.Length} words; partial={partialLimit}");
        gpu.RequestClose();
        Environment.Exit(0);
    }

    private sealed record QueueFixture(uint[][] Buffers, int OutputBinding, uint GroupsX, int[]? FloatWords, double FloatTolerance);
    private sealed class OwnedReplayMemory : ICpuMemory
    {
        private readonly SortedDictionary<ulong, byte[]> _ranges = new();
        public int OutputWriteCalls { get; private set; }
        public void Add(ulong address, byte[] bytes)
        {
            if (_ranges.TryGetValue(address, out var previous))
            {
                if (!previous.AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException("Conflicting aliased captured input bytes");
                return;
            }
            if (address > ulong.MaxValue - (ulong)bytes.Length) throw new InvalidOperationException("Owned address range overflow");
            foreach (var range in _ranges)
                if (address < range.Key + (ulong)range.Value.Length && range.Key < address + (ulong)bytes.Length)
                    throw new InvalidOperationException("Unexpected overlapping captured ranges");
            _ranges.Add(address, bytes.ToArray());
        }
        public bool TryRead(ulong address, Span<byte> destination)
        {
            lock (_ranges) foreach (var range in _ranges)
                if (address >= range.Key && address - range.Key <= (ulong)range.Value.Length && (ulong)destination.Length <= (ulong)range.Value.Length - (address-range.Key))
                { range.Value.AsSpan((int)(address-range.Key), destination.Length).CopyTo(destination); return true; }
            return false;
        }
        public bool TryWrite(ulong address, ReadOnlySpan<byte> source)
        {
            lock (_ranges) foreach (var range in _ranges)
                if (address >= range.Key && address - range.Key <= (ulong)range.Value.Length && (ulong)source.Length <= (ulong)range.Value.Length - (address-range.Key))
                { source.CopyTo(range.Value.AsSpan((int)(address-range.Key), source.Length)); if (range.Key == 0x300000000) OutputWriteCalls++; return true; }
            return false;
        }
    }
}
