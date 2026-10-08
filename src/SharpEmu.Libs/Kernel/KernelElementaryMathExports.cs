// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;

namespace SharpEmu.Libs.Kernel;

public static class KernelElementaryMathExports
{
    [SysAbiExport(Nid = "H8ya2H00jbI", ExportName = "sin", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Sin(CpuContext ctx) => ReturnDouble(ctx, Math.Sin);
    [SysAbiExport(Nid = "rtV7-jWC6Yg", ExportName = "log", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Log(CpuContext ctx) => ReturnDouble(ctx, Math.Log);
    [SysAbiExport(Nid = "dnaeGXbjP6E", ExportName = "exp2", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Exp2(CpuContext ctx) => ReturnDouble(ctx, value => Math.Pow(2, value));

    private static int ReturnDouble(CpuContext ctx, Func<double, double> function)
    {
        ctx.GetXmmRegister(0, out var bits, out _);
        var result = function(BitConverter.Int64BitsToDouble(unchecked((long)bits)));
        ctx.SetXmmRegister(0, unchecked((ulong)BitConverter.DoubleToInt64Bits(result)), 0);
        return 0;
    }
}
