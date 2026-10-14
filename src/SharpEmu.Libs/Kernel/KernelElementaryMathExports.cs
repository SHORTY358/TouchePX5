// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;

namespace SharpEmu.Libs.Kernel;

public static class KernelElementaryMathExports
{
    [SysAbiExport(Nid = "pztV4AF18iI", ExportName = "sincosf", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Sincosf(CpuContext ctx)
    {
        var sineAddress = ctx[CpuRegister.Rdi];
        var cosineAddress = ctx[CpuRegister.Rsi];
        if (sineAddress == 0 || cosineAddress == 0 ||
            !ctx.TryReadUInt32(sineAddress, out _) || !ctx.TryReadUInt32(cosineAddress, out _))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
        ctx.GetXmmRegister(0, out var bits, out _);
        var angle = BitConverter.Int32BitsToSingle(unchecked((int)bits));
        var (sine, cosine) = MathF.SinCos(angle);
        if (!ctx.TryWriteUInt32(sineAddress, unchecked((uint)BitConverter.SingleToInt32Bits(sine))) ||
            !ctx.TryWriteUInt32(cosineAddress, unchecked((uint)BitConverter.SingleToInt32Bits(cosine))))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
        return 0;
    }

    [SysAbiExport(Nid = "H8ya2H00jbI", ExportName = "sin", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Sin(CpuContext ctx) => ReturnDouble(ctx, Math.Sin);
    [SysAbiExport(Nid = "rtV7-jWC6Yg", ExportName = "log", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Log(CpuContext ctx) => ReturnDouble(ctx, Math.Log);
    [SysAbiExport(Nid = "dnaeGXbjP6E", ExportName = "exp2", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Exp2(CpuContext ctx) => ReturnDouble(ctx, value => Math.Pow(2, value));
    [SysAbiExport(Nid = "OXmauLdQ8kY", ExportName = "atan", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Atan(CpuContext ctx) => ReturnDouble(ctx, Math.Atan);
    [SysAbiExport(Nid = "T7uyNqP7vQA", ExportName = "tan", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Tan(CpuContext ctx) => ReturnDouble(ctx, Math.Tan);
    [SysAbiExport(Nid = "Q4rRL34CEeE", ExportName = "sinf", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Sinf(CpuContext ctx)
    {
        ctx.GetXmmRegister(0, out var bits, out _);
        var result = MathF.Sin(BitConverter.Int32BitsToSingle(unchecked((int)bits)));
        ctx.SetXmmRegister(0, unchecked((uint)BitConverter.SingleToInt32Bits(result)), 0);
        return 0;
    }

    private static int ReturnDouble(CpuContext ctx, Func<double, double> function)
    {
        ctx.GetXmmRegister(0, out var bits, out _);
        var result = function(BitConverter.Int64BitsToDouble(unchecked((long)bits)));
        ctx.SetXmmRegister(0, unchecked((ulong)BitConverter.DoubleToInt64Bits(result)), 0);
        return 0;
    }
}
