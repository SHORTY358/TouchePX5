// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

[Collection(AgcCommandBufferChainCollection.Name)]
public sealed class AgcWriteDataWaitTests
{
    [Theory]
    [InlineData(false, false, 1u, 0)]
    [InlineData(true, false, 1u, 0)]
    [InlineData(false, true, 1u, 0)]
    [InlineData(true, true, 1u, 0)]
    [InlineData(false, false, 2u, 1)]
    [InlineData(true, false, 2u, 1)]
    public void WriteDataPreservesSatisfactionBeforeALaterPayloadWordResetsTheLabel(
        bool gen5Packet, bool increment, uint signal, int expectedWaiters)
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
            // Incrementing writes first satisfy a wide label, then replace its
            // upper word. Nonincrementing writes signal/reset the same low word.
            Write64(memory, label, 0x1234_5678_0000_0000);
            GpuWaitRegistry.Register(label, new GpuWaitRegistry.WaitingDcb
            {
                Memory = memory, Is64Bit = increment,
                ReferenceValue = increment ? 0x1234_5678_0000_0001UL : 1,
                Mask = increment ? ulong.MaxValue : uint.MaxValue, CompareFunction = 3,
            });
            var control = (gen5Packet ? 5u : 5u << 8) | (increment ? 0u : 1u << 16);
            uint[] packet = [gen5Packet ? 0xC0041054u : 0xC0043700u,
                control, (uint)label, (uint)(label >> 32), signal, 0];
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
            Assert.Equal(increment ? signal : 0x1234_5678_0000_0000UL,
                BinaryPrimitives.ReadUInt64LittleEndian(actual));
            Assert.Equal(expectedWaiters, GpuWaitRegistry.CountForMemory(memory));
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
