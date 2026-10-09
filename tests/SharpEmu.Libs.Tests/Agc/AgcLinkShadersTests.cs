// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcLinkShadersTests
{
    private const ulong Base = 0x1_0000_0000;

    [Fact]
    public void TrianglePipelineMatchesReorderedSemanticsAndPreservesArguments()
    {
        var memory = new FakeCpuMemory(Base, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var cx = Base + 0x100;
        var uc = Base + 0x300;
        var gs = Base + 0x400;
        var ps = Base + 0x500;
        var specials = Base + 0x600;
        var outputs = Base + 0x700;
        var inputs = Base + 0x800;
        Write(memory, gs + 0x5A, [2]);
        Write(memory, ps + 0x5A, [1]);
        U64(memory, gs + 0x28, specials);
        U64(memory, gs + 0x38, outputs);
        Write(memory, gs + 0x56, [2, 0]);
        U64(memory, ps + 0x30, inputs);
        U32(memory, ps + 0x50, 2);
        U64(memory, specials, 0x0012_3456_0000_025B);
        U64(memory, specials + 8, 0x0000_0020_0000_02D5);
        U64(memory, specials + 0x20, 0x0000_0002_0000_029B);
        U64(memory, specials + 0x28, 0x0000_0007_0000_025C);
        U32(memory, outputs, 0x0309); // semantic 9 exported at location 3
        U32(memory, outputs + 4, 0x0705); // semantic 5 at location 7
        U32(memory, inputs, 0x0040_0005); // flat semantic 5
        U32(memory, inputs + 4, 9);
        ctx[CpuRegister.Rdi] = cx;
        ctx[CpuRegister.Rsi] = uc;
        ctx[CpuRegister.Rdx] = 0;
        ctx[CpuRegister.Rcx] = gs;
        ctx[CpuRegister.R8] = ps;
        ctx[CpuRegister.R9] = 4;

        Assert.Equal(0, AgcExports.LinkShaders(ctx));
        Assert.Equal(0x2D5u, Read(memory, cx));
        Assert.Equal(0x191u, Read(memory, cx + 16));
        Assert.Equal(0x407u, Read(memory, cx + 20));
        Assert.Equal(3u, Read(memory, cx + 28));
        Assert.Equal(0x242u, Read(memory, uc + 16));
        Assert.Equal(4u, Read(memory, uc + 20));
        Assert.Equal(ps, ctx[CpuRegister.R8]);
        Assert.Equal(uc, ctx[CpuRegister.Rsi]);
        Assert.Equal(0UL, ctx[CpuRegister.Rdx]);
        Assert.Equal(31u, Read(memory, cx + 268));
    }

    [Theory]
    [InlineData(1UL, 4UL)]
    [InlineData(0UL, 7UL)]
    public void UnsupportedPipelineDoesNotOverwriteOutput(ulong hull, ulong primitive)
    {
        var memory = new FakeCpuMemory(Base, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        U32(memory, Base, 0xDEADBEEF);
        ctx[CpuRegister.Rdi] = Base;
        ctx[CpuRegister.Rsi] = Base + 0x200;
        ctx[CpuRegister.Rdx] = hull;
        ctx[CpuRegister.Rcx] = Base + 0x400;
        ctx[CpuRegister.R8] = Base + 0x500;
        ctx[CpuRegister.R9] = primitive;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT, AgcExports.LinkShaders(ctx));
        Assert.Equal(0xDEADBEEFu, Read(memory, Base));
    }

    private static void Write(FakeCpuMemory m, ulong a, byte[] b) => Assert.True(m.TryWrite(a, b));
    private static void U64(FakeCpuMemory m, ulong a, ulong v)
    {
        var b = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, v); Write(m, a, b);
    }
    private static void U32(FakeCpuMemory m, ulong a, uint v)
    {
        var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); Write(m, a, b);
    }
    private static uint Read(FakeCpuMemory m, ulong a)
    {
        var b = new byte[4]; Assert.True(m.TryRead(a, b)); return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }
}
