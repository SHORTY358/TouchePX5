// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

[Collection(AgcCommandBufferChainCollection.Name)]
public sealed class AgcDmaLabelTests
{
    [Theory]
    [InlineData(false, 1u, 0)]
    [InlineData(true, 1u, 0)]
    [InlineData(false, 2u, 1)]
    [InlineData(true, 2u, 1)]
    public void PlainMemoryDmaPreservesASignalBeforeReset(bool copy, uint signal, int remaining)
    {
        const ulong root = 0x1_0000_0000;
        var memory = new FakeCpuMemory(root, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var commands = root + 0x100;
        var submission = root + 0x200;
        var sources = root + 0x300;
        var label = root + 0x400;
        Write64(memory, sources, signal);
        GpuWaitRegistry.Clear();
        try
        {
            GpuWaitRegistry.Register(label, new GpuWaitRegistry.WaitingDcb
            {
                Memory = memory, ReferenceValue = 1, Mask = uint.MaxValue, CompareFunction = 3,
            });
            Span<byte> word = stackalloc byte[4];
            for (var reset = 0; reset < 2; reset++)
            {
                var source = copy ? sources + (ulong)reset * 4 : reset == 0 ? signal : 0UL;
                uint[] packet = [0xC0055000, copy ? 0u : 2u << 29,
                    (uint)source, (uint)(source >> 32), (uint)label, (uint)(label >> 32), 4];
                for (var i = 0; i < packet.Length; i++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(word, packet[i]);
                    Assert.True(memory.TryWrite(commands + (ulong)(reset * 7 + i) * 4, word));
                }
            }
            Write64(memory, submission, commands);
            Write64(memory, submission + 8, 14);
            ctx[CpuRegister.Rdi] = 7;
            ctx[CpuRegister.Rsi] = submission;
            Assert.Equal(0, AgcExports.DriverSubmitAcb(ctx));
            Assert.True(memory.TryRead(label, word));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(word));
            Assert.Equal(remaining, GpuWaitRegistry.CountForMemory(memory));
        }
        finally { GpuWaitRegistry.Clear(); }
    }

    private static void Write64(FakeCpuMemory memory, ulong address, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        Assert.True(memory.TryWrite(address, bytes));
    }
}
