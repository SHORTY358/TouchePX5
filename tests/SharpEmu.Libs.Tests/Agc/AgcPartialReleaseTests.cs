// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

[Collection(AgcCommandBufferChainCollection.Name)]
public sealed class AgcPartialReleaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Release32DoesNotSatisfy64BitWaitWithDifferentUpperWord(bool gen5Packet)
    {
        const ulong baseAddress = 0x1_0000_0000;
        var memory = new FakeCpuMemory(baseAddress, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var label = baseAddress + 0x1000;
        var commands = baseAddress + 0x100;
        var submit = baseAddress + 0x200;
        GpuWaitRegistry.Clear();
        try
        {
            Write64(memory, label, 0x1234_5678_0000_0000);
            GpuWaitRegistry.Register(label, new GpuWaitRegistry.WaitingDcb
            {
                Memory = memory, Is64Bit = true, ReferenceValue = 1,
                Mask = ulong.MaxValue, CompareFunction = 3,
            });
            uint[] packet = gen5Packet
                ? [0xC0061060, 0, 1u << 16, (uint)label, (uint)(label >> 32), 1, 0, 0]
                : [0xC0064900, 0, 1u << 29, (uint)label, (uint)(label >> 32), 1, 0, 0];
            Span<byte> bytes = stackalloc byte[4];
            for (var i = 0; i < packet.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, packet[i]);
                Assert.True(memory.TryWrite(commands + (ulong)i * 4, bytes));
            }
            Write64(memory, submit, commands);
            Write64(memory, submit + 8, (ulong)packet.Length);
            ctx[CpuRegister.Rdi] = 7;
            ctx[CpuRegister.Rsi] = submit;
            Assert.Equal(0, AgcExports.DriverSubmitAcb(ctx));
            Span<byte> actual = stackalloc byte[8];
            Assert.True(memory.TryRead(label, actual));
            Assert.Equal(0x1234_5678_0000_0001UL, BinaryPrimitives.ReadUInt64LittleEndian(actual));
            // The upper word is untouched. A narrow payload of 1 is not the
            // full value observed by this waiter, even when the parser wakes it.
            Assert.Equal(1, GpuWaitRegistry.CountForMemory(memory));
            Assert.Equal(0, GpuWaitRegistry.SnapshotOutstanding(memory).Latched);
        }
        finally { GpuWaitRegistry.Clear(); }
    }

    [Theory]
    [InlineData(0, 4, true, 0, 0x1234_5678_0000_0001UL)]
    [InlineData(4, 4, true, 0, 0x1234_5678_0000_0001UL)]
    [InlineData(0, 8, false, 4, 0x1234_5678UL)]
    public void OverlappingWriteLatchesFullLabelBeforeReset(
        int writeOffset, int writeLength, bool wideWait, int waitOffset, ulong reference)
    {
        const ulong label = 0x1000;
        var memory = new object();
        GpuWaitRegistry.Clear();
        try
        {
            var address = label + (ulong)waitOffset;
            GpuWaitRegistry.Register(address, new GpuWaitRegistry.WaitingDcb
            {
                Memory = memory, Is64Bit = wideWait, ReferenceValue = reference,
                Mask = wideWait ? ulong.MaxValue : uint.MaxValue, CompareFunction = 3,
            });
            Assert.True(GpuWaitRegistry.LatchSatisfiedByMemoryWrite(
                memory, label + (ulong)writeOffset, (ulong)writeLength,
                (readAddress, readWide) =>
                {
                    Assert.Equal(address, readAddress);
                    Assert.Equal(wideWait, readWide);
                    return reference;
                }));
            // A reset after the write must not erase already observed satisfaction.
            var ready = GpuWaitRegistry.CollectSatisfied(memory, (_, _) => 0, out var remaining);
            Assert.NotNull(ready);
            Assert.Single(ready!);
            Assert.Equal(0, remaining);
        }
        finally { GpuWaitRegistry.Clear(); }
    }

    [Fact]
    public void MemoryWriteDoesNotLatchUnrelatedOrUnreadableLabels()
    {
        var memory = new object();
        GpuWaitRegistry.Clear();
        try
        {
            GpuWaitRegistry.Register(0x1000, new GpuWaitRegistry.WaitingDcb
            {
                Memory = memory, ReferenceValue = 1, Mask = uint.MaxValue, CompareFunction = 3,
            });
            static ulong? UnexpectedRead(ulong address, bool wide) =>
                throw new InvalidOperationException("An unrelated write must not read this label.");
            Assert.False(GpuWaitRegistry.LatchSatisfiedByMemoryWrite(memory, 0x1004, 4, UnexpectedRead));
            Assert.False(GpuWaitRegistry.LatchSatisfiedByMemoryWrite(memory, 0xFFC, 4, UnexpectedRead));
            Assert.False(GpuWaitRegistry.LatchSatisfiedByMemoryWrite(memory, 0x1000, 0, UnexpectedRead));
            Assert.False(GpuWaitRegistry.LatchSatisfiedByMemoryWrite(new object(), 0x1000, 4, UnexpectedRead));
            Assert.False(GpuWaitRegistry.LatchSatisfiedByMemoryWrite(memory, 0x1000, 4, (_, _) => null));
            Assert.Equal(0, GpuWaitRegistry.SnapshotOutstanding(memory).Latched);
            Assert.Equal(1, GpuWaitRegistry.CountForMemory(memory));
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
