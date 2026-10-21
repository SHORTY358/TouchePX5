// Copyright (C) 2026 Touché PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcIndexedDrawPacketTests
{
    [Theory]
    [InlineData(0UL, 2u, 3u)]
    [InlineData(0x40000000UL, 2u, 120u)]
    [InlineData(0x100UL, 0x22u, 0u)]
    [InlineData(0x100000100UL, 2u, 36u)]
    public void AutoDrawUsesThreeDwordsAndDecodesModifier(ulong modifier, uint initiator, uint count)
    {
        const ulong address = 0x1_0000_0000;
        var memory = new FakeCpuMemory(address, 4096);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var commands = address + 0x100;
        Assert.True(ctx.TryWriteUInt64(address + 0x10, commands));
        Assert.True(ctx.TryWriteUInt64(address + 0x18, commands + 12));
        Assert.True(ctx.TryWriteUInt32(commands + 12, 0xDEADBEEF));
        ctx[CpuRegister.Rdi] = address;
        ctx[CpuRegister.Rsi] = count;
        ctx[CpuRegister.Rdx] = modifier;
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));
        Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("Yw0jKSqop+E", ctx));
        Assert.Equal(commands, ctx[CpuRegister.Rax]);
        var data = new byte[16];
        Assert.True(memory.TryRead(commands, data));
        Assert.Equal(0xC0012D00u, BinaryPrimitives.ReadUInt32LittleEndian(data));
        Assert.Equal(count, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)));
        Assert.Equal(initiator, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)));
        Assert.Equal(0xDEADBEEFu, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)));
        Assert.True(ctx.TryReadUInt64(address + 0x10, out var cursor));
        Assert.Equal(commands + 12, cursor);
    }

    [Theory]
    [InlineData(0u, 36u)]
    [InlineData(0x40000000u, 36u)]
    [InlineData(0u, 0u)]
    [InlineData(5u, 3u)]
    [InlineData(0x1234u, 100u)]
    public void IndexedDrawEmitsCountAddressAndInitiator(uint initiator, uint count)
    {
        const ulong address = 0x1_0000_0000;
        var memory = new FakeCpuMemory(address, 4096);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var commands = address + 0x100;
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, commands);
        Assert.True(memory.TryWrite(address + 0x10, bytes));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, commands + 24);
        Assert.True(memory.TryWrite(address + 0x18, bytes));
        Assert.True(ctx.TryWriteUInt32(commands + 24, 0xDEADBEEFu));
        ctx[CpuRegister.Rdi] = address;
        ctx[CpuRegister.Rsi] = count;
        ctx[CpuRegister.Rdx] = 0x12_34567890;
        ctx[CpuRegister.Rcx] = initiator;
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));
        Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("q88lQ+GP5Yk", ctx));
        var packet = ctx[CpuRegister.Rax];
        Assert.Equal(commands, packet);
        var data = new byte[24];
        Assert.True(memory.TryRead(packet, data));
        Assert.Equal(0xC0042700u, BinaryPrimitives.ReadUInt32LittleEndian(data));
        Assert.Equal(Math.Max(count, 1u), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)));
        Assert.Equal(0x12_34567890UL, BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8)));
        Assert.Equal(count, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)));
        Assert.Equal(initiator, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20)));
        Assert.True(ctx.TryReadUInt64(address + 0x10, out var cursor));
        Assert.Equal(commands + 24, cursor);
        Assert.True(ctx.TryReadUInt32(commands + 24, out var tail));
        Assert.Equal(0xDEADBEEFu, tail);
    }

    [Fact]
    public void IndexedDrawRejectsInsufficientCapacityWithoutPartialPackets()
    {
        const ulong address = 0x1_0000_0000;
        var memory = new FakeCpuMemory(address, 4096);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var commands = address + 0x100;
        Assert.True(ctx.TryWriteUInt64(address + 0x10, commands));
        Assert.True(ctx.TryWriteUInt64(address + 0x18, commands + 20));
        Assert.True(ctx.TryWriteUInt32(commands, 0xDEADBEEFu));
        ctx[CpuRegister.Rdi] = address;
        ctx[CpuRegister.Rsi] = 3;
        ctx[CpuRegister.Rdx] = 0x12_34567890;
        ctx[CpuRegister.Rcx] = 5;
        Assert.Equal(0, AgcExports.DcbDrawIndex(ctx));
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
        Assert.True(ctx.TryReadUInt64(address + 0x10, out var cursor));
        Assert.Equal(commands, cursor);
        Assert.True(ctx.TryReadUInt32(commands, out var first));
        Assert.Equal(0xDEADBEEFu, first);
    }
}
