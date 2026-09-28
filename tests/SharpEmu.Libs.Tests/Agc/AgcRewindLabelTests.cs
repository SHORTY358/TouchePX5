// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

[Collection(AgcCommandBufferChainCollection.Name)]
public sealed class AgcRewindLabelTests
{
    [Theory]
    [InlineData(false, 0x80000000UL, true)]
    [InlineData(true, 0x80000000UL, false)]
    [InlineData(true, 0x1234567880000000UL, true)]
    public void RewindPatchUsesTheFullWaitValueAndPreservesSatisfaction(
        bool wide, ulong reference, bool shouldLatch)
    {
        const ulong label = 0x1_0000_0100;
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        Span<byte> data = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(data, 0x1234567800000000UL);
        Assert.True(memory.TryWrite(label, data));
        GpuWaitRegistry.Clear();
        try
        {
            GpuWaitRegistry.Register(label, new GpuWaitRegistry.WaitingDcb
            {
                Memory = memory, Is64Bit = wide, ReferenceValue = reference,
                Mask = wide ? ulong.MaxValue : uint.MaxValue, CompareFunction = 3,
            });
            ctx[CpuRegister.Rdi] = label;
            ctx[CpuRegister.Rsi] = 1;
            Assert.Equal(0, AgcExports.RewindPatchSetRewindState(ctx));
            Assert.True(memory.TryRead(label, data));
            Assert.Equal(0x1234567880000000UL, BinaryPrimitives.ReadUInt64LittleEndian(data));
            Assert.Equal(shouldLatch ? 1 : 0, GpuWaitRegistry.SnapshotOutstanding(memory).Latched);
            ctx[CpuRegister.Rsi] = 0;
            Assert.Equal(0, AgcExports.RewindPatchSetRewindState(ctx));
            var ready = GpuWaitRegistry.CollectSatisfied(memory, (_, _) => 0, out var remaining);
            Assert.Equal(shouldLatch ? 0 : 1, remaining);
            Assert.Equal(shouldLatch ? 1 : 0, ready?.Count ?? 0);
        }
        finally { GpuWaitRegistry.Clear(); }
    }
}
