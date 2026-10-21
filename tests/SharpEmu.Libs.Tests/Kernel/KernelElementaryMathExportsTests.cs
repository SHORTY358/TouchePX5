// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

public sealed class KernelElementaryMathExportsTests
{
    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void CameraFunctionsUseFloatArgumentsAndResultsThroughGeneratedDispatch(Generation generation)
    {
        var ctx=new CpuContext(new FakeCpuMemory(0x10000,64),generation);
        var manager=new ModuleManager();manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));
        void Check(string nid,float y,float x,float expected)
        {
            ctx.SetXmmRegister(0,BitConverter.SingleToUInt32Bits(y)|0xDEADBEEF00000000UL,ulong.MaxValue);
            ctx.SetXmmRegister(1,BitConverter.SingleToUInt32Bits(x)|0x1234567800000000UL,ulong.MaxValue);
            Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK,manager.Dispatch(nid,ctx));
            ctx.GetXmmRegister(0,out var bits,out _);
            var actual=BitConverter.UInt32BitsToSingle((uint)bits);
            if(float.IsNaN(expected)) Assert.True(float.IsNaN(actual));
            else if(expected==0) Assert.Equal(BitConverter.SingleToUInt32Bits(expected),BitConverter.SingleToUInt32Bits(actual));
            else Assert.InRange(MathF.Abs(actual-expected),0,0.000001f);
        }
        Check("EH-x713A99c",-1,0,-MathF.PI/2);
        Check("EH-x713A99c",1,-1,MathF.PI*0.75f);
        Check("EH-x713A99c",-0f,1,-0f);
        Check("EH-x713A99c",float.PositiveInfinity,float.PositiveInfinity,MathF.PI/4);
        Check("GZWjF-YIFFk",-0.5f,0,-MathF.PI/6);
        Check("GZWjF-YIFFk",2,0,float.NaN);
        Check("-P6FNMzk2Kc",MathF.PI,0,-1);
        Check("-P6FNMzk2Kc",float.PositiveInfinity,0,float.NaN);
    }
    [Theory]
    [InlineData(0.0f, 0.0f, 1.0f)]
    [InlineData(1.5707963267948966f, 1.0f, 0.0f)]
    [InlineData(-1.5707963267948966f, -1.0f, 0.0f)]
    public void SincosfWritesBothPointerResultsAndPreservesAdjacentMemory(float angle, float sine, float cosine)
    {
        var memory = new FakeCpuMemory(0x10000, 64);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var original = Enumerable.Repeat((byte)0xA5, 64).ToArray();
        Assert.True(memory.TryWrite(0x10000, original));
        ctx.SetXmmRegister(0, (uint)BitConverter.SingleToInt32Bits(angle) | 0xDEADBEEF00000000UL, ulong.MaxValue);
        ctx[CpuRegister.Rdi] = 0x10010;
        ctx[CpuRegister.Rsi] = 0x10020;
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));
        Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("pztV4AF18iI", ctx));
        Assert.True(ctx.TryReadUInt32(0x10010, out var sineBits));
        Assert.True(ctx.TryReadUInt32(0x10020, out var cosineBits));
        Assert.InRange(MathF.Abs(BitConverter.Int32BitsToSingle((int)sineBits) - sine), 0, 0.000001f);
        Assert.InRange(MathF.Abs(BitConverter.Int32BitsToSingle((int)cosineBits) - cosine), 0, 0.000001f);
        var after = new byte[64];
        Assert.True(memory.TryRead(0x10000, after));
        for (var i = 0; i < 64; i++)
            if (!(i >= 16 && i < 20) && !(i >= 32 && i < 36)) Assert.Equal(original[i], after[i]);
    }

    [Fact]
    public void SincosfRejectsMissingCosinePointerBeforeWritingSine()
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x10000, 64), Generation.Gen5);
        Assert.True(ctx.TryWriteUInt32(0x10010, 0xA5A5A5A5));
        ctx[CpuRegister.Rdi] = 0x10010;
        ctx[CpuRegister.Rsi] = 0x20000;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT, KernelElementaryMathExports.Sincosf(ctx));
        Assert.True(ctx.TryReadUInt32(0x10010, out var value));
        Assert.Equal(0xA5A5A5A5u, value);
    }

    [Theory]
    [InlineData("sin", 0.0, 0.0)]
    [InlineData("sin", 1.5707963267948966, 1.0)]
    [InlineData("log", 1.0, 0.0)]
    [InlineData("log", 0.0, double.NegativeInfinity)]
    [InlineData("log", -1.0, double.NaN)]
    [InlineData("exp2", -3.0, 0.125)]
    [InlineData("exp2", double.PositiveInfinity, double.PositiveInfinity)]
    [InlineData("atan", 0.0, 0.0)]
    [InlineData("atan", double.PositiveInfinity, 1.5707963267948966)]
    [InlineData("tan", 0.0, 0.0)]
    [InlineData("tan", double.PositiveInfinity, double.NaN)]
    public void DoubleArgumentAndResultUseXmm0(string name, double argument, double expected)
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x10000, 0x100), Generation.Gen5);
        ctx.SetXmmRegister(0, unchecked((ulong)BitConverter.DoubleToInt64Bits(argument)), ulong.MaxValue);
        var status = name switch
        {
            "sin" => KernelElementaryMathExports.Sin(ctx),
            "log" => KernelElementaryMathExports.Log(ctx),
            "atan" => KernelElementaryMathExports.Atan(ctx),
            "tan" => KernelElementaryMathExports.Tan(ctx),
            _ => KernelElementaryMathExports.Exp2(ctx),
        };
        Assert.Equal(0, status);
        ctx.GetXmmRegister(0, out var low, out _);
        var actual = BitConverter.Int64BitsToDouble(unchecked((long)low));
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual));
        else Assert.Equal(expected, actual);
    }

    [Fact]
    public void SinfUsesLowSinglePrecisionBitsInsteadOfDoubleArgument()
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x10000, 0x100), Generation.Gen5);
        var argument = (ulong)(uint)BitConverter.SingleToInt32Bits(MathF.PI / 2);
        ctx.SetXmmRegister(0, argument | 0xDEADBEEF00000000UL, ulong.MaxValue);
        Assert.Equal(0, KernelElementaryMathExports.Sinf(ctx));
        ctx.GetXmmRegister(0, out var bits, out _);
        Assert.Equal(1.0f, BitConverter.Int32BitsToSingle(unchecked((int)bits)));
    }
}
