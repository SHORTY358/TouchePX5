// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcDriverWaitExportsTests
{
    private const ulong Base = 0x1_0000_0000;

    [Fact]
    public void DriverWaitUsesCallerCursorAndExistingOrderedPacketWithoutTouchingTail()
    {
        var memory = new FakeCpuMemory(Base, 4096);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));
        Assert.True(manager.TryGetExport("0MtUJ3BpGhE", out var sizeExport));
        Assert.Equal("libSceAgcDriver", sizeExport.LibraryName);
        Assert.Equal("sceAgcDriverGetWaitRenderingPacketSizeInDwords", sizeExport.Name);
        Assert.True(manager.TryGetExport("u8BkdHb1+Po", out var waitExport));
        Assert.Equal("libSceAgcDriver", waitExport.LibraryName);
        Assert.Equal("sceAgcDriverWaitUntilSafeForRendering", waitExport.Name);
        var commands = Base + 256;
        var sentinel = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        Assert.True(memory.TryWrite(commands, sentinel));
        Assert.True(ctx.TryWriteUInt64(Base, commands));
        Assert.Equal(7, (int)manager.Dispatch("0MtUJ3BpGhE", ctx));
        Assert.Equal(7UL, ctx[CpuRegister.Rax]);
        ctx[CpuRegister.Rdi] = Base;
        ctx[CpuRegister.Rsi] = ctx[CpuRegister.Rax];
        ctx[CpuRegister.Rcx] = 3;
        ctx[CpuRegister.R8] = 2;
        Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("u8BkdHb1+Po", ctx));
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
        var result = new byte[32];
        Assert.True(memory.TryRead(commands, result));
        Assert.Equal(0xC0051018u, BinaryPrimitives.ReadUInt32LittleEndian(result));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(8)));
        Assert.All(result.Skip(12).Take(16), value => Assert.Equal((byte)0, value));
        Assert.Equal(sentinel.Skip(28), result.Skip(28));
        var cursor = new byte[8];
        Assert.True(memory.TryRead(Base, cursor));
        Assert.Equal(commands + 28, BinaryPrimitives.ReadUInt64LittleEndian(cursor));
    }

    [Theory]
    [InlineData(6u, 0u, 3u, 2u, 256UL)]
    [InlineData(7u, 1u, 3u, 2u, 256UL)]
    [InlineData(7u, 0u, uint.MaxValue, 2u, 256UL)]
    [InlineData(7u, 0u, 3u, uint.MaxValue, 256UL)]
    [InlineData(7u, 0u, 3u, 2u, 4096UL)]
    [InlineData(7u, 0u, 3u, 2u, 4UL)]
    [InlineData(7u, 0u, 3u, 2u, 257UL)]
    public void RejectedCallsDoNotAdvanceCursorOrModifyTheCommands(uint capacity, uint reserved, uint handle, uint index, ulong offset)
    {
        var memory = new FakeCpuMemory(Base, 4096);
        var ctx = new CpuContext(memory, Generation.Gen5);
        Assert.True(ctx.TryWriteUInt64(Base, Base + offset));
        var before = new byte[4096];
        Assert.True(memory.TryRead(Base, before));
        ctx[CpuRegister.Rdi] = Base;
        ctx[CpuRegister.Rsi] = capacity;
        ctx[CpuRegister.Rdx] = reserved;
        ctx[CpuRegister.Rcx] = handle;
        ctx[CpuRegister.R8] = index;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT, AgcExports.DriverWaitUntilSafeForRendering(ctx));
        var after = new byte[4096];
        Assert.True(memory.TryRead(Base, after));
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(0x1_0000_1000UL)]
    [InlineData(ulong.MaxValue)]
    public void InvalidCursorAddressReturnsErrorWithoutAccessingCommands(ulong cursor)
    {
        var ctx = new CpuContext(new FakeCpuMemory(Base, 4096), Generation.Gen5);
        ctx[CpuRegister.Rdi] = cursor;
        ctx[CpuRegister.Rsi] = 7;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT, AgcExports.DriverWaitUntilSafeForRendering(ctx));
    }
}
