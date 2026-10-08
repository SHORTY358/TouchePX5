// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

public sealed class KernelElementaryMathExportsTests
{
    [Theory]
    [InlineData("sin", 0.0, 0.0)]
    [InlineData("sin", 1.5707963267948966, 1.0)]
    [InlineData("log", 1.0, 0.0)]
    [InlineData("log", 0.0, double.NegativeInfinity)]
    [InlineData("log", -1.0, double.NaN)]
    [InlineData("exp2", -3.0, 0.125)]
    [InlineData("exp2", double.PositiveInfinity, double.PositiveInfinity)]
    public void DoubleArgumentAndResultUseXmm0(string name, double argument, double expected)
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x10000, 0x100), Generation.Gen5);
        ctx.SetXmmRegister(0, unchecked((ulong)BitConverter.DoubleToInt64Bits(argument)), ulong.MaxValue);
        var status = name switch
        {
            "sin" => KernelElementaryMathExports.Sin(ctx),
            "log" => KernelElementaryMathExports.Log(ctx),
            _ => KernelElementaryMathExports.Exp2(ctx),
        };
        Assert.Equal(0, status);
        ctx.GetXmmRegister(0, out var low, out _);
        var actual = BitConverter.Int64BitsToDouble(unchecked((long)low));
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual));
        else Assert.Equal(expected, actual);
    }
}
