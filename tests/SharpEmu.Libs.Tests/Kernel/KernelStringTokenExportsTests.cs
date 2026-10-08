// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

public sealed class KernelStringTokenExportsTests
{
    private const ulong Base = 0x1_0000_0000;

    [Fact]
    public void Strtok_SkipsRepeatedDelimitersChangesDelimiterAndSeparatesProcesses()
    {
        var memory = new FakeCpuMemory(Base, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        memory.WriteCString(Base, ",,alpha,beta;gamma;;");
        memory.WriteCString(Base + 0x100, ",");
        ctx[CpuRegister.Rdi] = Base;
        ctx[CpuRegister.Rsi] = Base + 0x100;
        Assert.Equal(0, KernelMemoryCompatExports.Strtok(ctx));
        Assert.Equal(Base + 2, ctx[CpuRegister.Rax]);
        var nul = new byte[1];
        Assert.True(memory.TryRead(Base + 7, nul));
        Assert.Equal(0, nul[0]);
        var other = new CpuContext(new FakeCpuMemory(Base, 0x1000), Generation.Gen5);
        Assert.Equal(0, KernelMemoryCompatExports.Strtok(other));
        Assert.Equal(0UL, other[CpuRegister.Rax]);
        memory.WriteCString(Base + 0x100, ";");
        ctx[CpuRegister.Rdi] = 0;
        Assert.Equal(0, KernelMemoryCompatExports.Strtok(ctx));
        Assert.Equal(Base + 8, ctx[CpuRegister.Rax]);
        Assert.Equal(0, KernelMemoryCompatExports.Strtok(ctx));
        Assert.Equal(Base + 13, ctx[CpuRegister.Rax]);
        Assert.Equal(0, KernelMemoryCompatExports.Strtok(ctx));
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
    }

    [Theory]
    [InlineData("  -0x2Az", 0, -42L, 7)]
    [InlineData("0779", 0, 63L, 3)]
    [InlineData("+z!", 36, 35L, 2)]
    [InlineData(" - nope", 10, 0L, 0)]
    [InlineData("9223372036854775808x", 10, long.MaxValue, 19)]
    [InlineData("-9223372036854775809x", 10, long.MinValue, 20)]
    [InlineData("0x", 16, 0L, 1)]
    public void Strtol_UsesLp64AndReportsFirstUnparsedByte(string input, int radix, long expected, int consumed)
    {
        var memory = new FakeCpuMemory(Base, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        memory.WriteCString(Base, input);
        ctx[CpuRegister.Rdi] = Base;
        ctx[CpuRegister.Rsi] = Base + 0x100;
        ctx[CpuRegister.Rdx] = (ulong)radix;
        Assert.Equal(0, KernelMemoryCompatExports.Strtol(ctx));
        Assert.Equal(unchecked((ulong)expected), ctx[CpuRegister.Rax]);
        Assert.True(ctx.TryReadUInt64(Base + 0x100, out var end));
        Assert.Equal(Base + (ulong)consumed, end);
    }

    [Fact]
    public void Strlcat_ReportsAttemptedLengthAndPreservesBoundary()
    {
        var memory = new FakeCpuMemory(Base, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        memory.WriteCString(Base, "ab");
        memory.WriteCString(Base + 0x100, "cdef");
        memory.TryWrite(Base + 5, new byte[] { 0xCC });
        ctx[CpuRegister.Rdi] = Base;
        ctx[CpuRegister.Rsi] = Base + 0x100;
        ctx[CpuRegister.Rdx] = 5;
        Assert.Equal(0, KernelMemoryCompatExports.Strlcat(ctx));
        Assert.Equal(6UL, ctx[CpuRegister.Rax]);
        var output = new byte[6];
        Assert.True(memory.TryRead(Base, output));
        Assert.Equal(new byte[] { 97, 98, 99, 100, 0, 0xCC }, output);
        ctx[CpuRegister.Rdx] = 2;
        Assert.Equal(0, KernelMemoryCompatExports.Strlcat(ctx));
        Assert.Equal(6UL, ctx[CpuRegister.Rax]);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(65, 97)]
    [InlineData(90, 122)]
    [InlineData(255, 255)]
    public void Tolower_CLocalePreservesEofAndNonAscii(int input, int expected)
    {
        var ctx = new CpuContext(new FakeCpuMemory(Base, 16), Generation.Gen5);
        ctx[CpuRegister.Rdi] = unchecked((ulong)input);
        Assert.Equal(0, KernelMemoryCompatExports.Tolower(ctx));
        Assert.Equal(unchecked((ulong)expected), ctx[CpuRegister.Rax]);
    }
}
