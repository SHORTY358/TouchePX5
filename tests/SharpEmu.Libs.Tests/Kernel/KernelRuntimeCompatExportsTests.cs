// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using System.Buffers.Binary;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

// sceKernelGetTscFrequency must describe the same clock that sceKernelReadTsc returns. ReadTsc
// only returns the CPU's RDTSC when the host RDTSC reader is available (64-bit Windows) and
// otherwise falls back to the QPC-based Stopwatch, so the frequency selection has to follow suit.
public sealed class KernelRuntimeCompatExportsTests
{
    private const ulong GuestMemoryBase = 0x1_0000_0000;

    private static KernelRuntimeCompatExports.TryGetFrequency Yields(ulong hz) =>
        (out ulong frequencyHz) =>
        {
            frequencyHz = hz;
            return true;
        };

    private static readonly KernelRuntimeCompatExports.TryGetFrequency Fails =
        (out ulong frequencyHz) =>
        {
            frequencyHz = 0;
            return false;
        };

    [Fact]
    public void WithoutHostRdtsc_ReportsStopwatchFrequency_NotHardwareTsc()
    {
        // Regression: on Linux/macOS ReadTsc returns the Stopwatch counter, so the reported
        // frequency must be the Stopwatch's, never the CPU's much larger hardware TSC frequency.
        var (frequencyHz, source) = KernelRuntimeCompatExports.SelectKernelTscFrequency(
            rdtscAvailable: false,
            overrideHzText: null,
            tryCalibrate: Yields(2_400_000_000UL),
            tryResolveCpuid: Yields(3_000_000_000UL),
            stopwatchFrequency: 10_000_000);

        Assert.Equal(10_000_000UL, frequencyHz);
        Assert.Equal("qpc", source);
    }

    [Fact]
    public void WithHostRdtsc_PrefersCalibratedFrequency()
    {
        var (frequencyHz, source) = KernelRuntimeCompatExports.SelectKernelTscFrequency(
            rdtscAvailable: true,
            overrideHzText: null,
            tryCalibrate: Yields(2_400_000_000UL),
            tryResolveCpuid: Yields(3_000_000_000UL),
            stopwatchFrequency: 10_000_000);

        Assert.Equal(2_400_000_000UL, frequencyHz);
        Assert.Equal("calibrated-rdtsc", source);
    }

    [Fact]
    public void WithHostRdtsc_FallsBackToCpuid_WhenCalibrationFails()
    {
        var (frequencyHz, source) = KernelRuntimeCompatExports.SelectKernelTscFrequency(
            rdtscAvailable: true,
            overrideHzText: null,
            tryCalibrate: Fails,
            tryResolveCpuid: Yields(3_000_000_000UL),
            stopwatchFrequency: 10_000_000);

        Assert.Equal(3_000_000_000UL, frequencyHz);
        Assert.Equal("cpuid", source);
    }

    [Fact]
    public void WithHostRdtsc_UsesStopwatch_WhenRdtscFrequencyUnknown()
    {
        var (frequencyHz, source) = KernelRuntimeCompatExports.SelectKernelTscFrequency(
            rdtscAvailable: true,
            overrideHzText: null,
            tryCalibrate: Fails,
            tryResolveCpuid: Fails,
            stopwatchFrequency: 10_000_000);

        Assert.Equal(10_000_000UL, frequencyHz);
        Assert.Equal("qpc", source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnvOverride_Wins_WhenSane(bool rdtscAvailable)
    {
        var (frequencyHz, source) = KernelRuntimeCompatExports.SelectKernelTscFrequency(
            rdtscAvailable,
            overrideHzText: "1500000000",
            tryCalibrate: Yields(2_400_000_000UL),
            tryResolveCpuid: Yields(3_000_000_000UL),
            stopwatchFrequency: 10_000_000);

        Assert.Equal(1_500_000_000UL, frequencyHz);
        Assert.Equal("env", source);
    }

    [Fact]
    public void EnvOverride_BelowMinimum_IsIgnored()
    {
        // 500 kHz is below the sanity floor, so it is dropped; with rdtsc unavailable the
        // hardware-TSC path is gated off and the Stopwatch frequency is used.
        var (frequencyHz, _) = KernelRuntimeCompatExports.SelectKernelTscFrequency(
            rdtscAvailable: false,
            overrideHzText: "500000",
            tryCalibrate: Fails,
            tryResolveCpuid: Yields(3_000_000_000UL),
            stopwatchFrequency: 10_000_000);

        Assert.Equal(10_000_000UL, frequencyHz);
    }

    [Fact]
    public void NonPositiveStopwatchFrequency_FallsBackToDefault()
    {
        var (frequencyHz, source) = KernelRuntimeCompatExports.SelectKernelTscFrequency(
            rdtscAvailable: false,
            overrideHzText: null,
            tryCalibrate: Fails,
            tryResolveCpuid: Fails,
            stopwatchFrequency: 0);

        Assert.Equal(10_000_000UL, frequencyHz); // DefaultKernelTscFrequency
        Assert.Equal("qpc", source);
    }

    [Fact]
    public void ConvertLocaltimeToUtc_AcceptsGuestValuesOutsideDotNetDateRange()
    {
        const long guestLocalTime = 26_076_015_555_343_848;
        const ulong utcAddress = GuestMemoryBase + 0x100;
        const ulong timezoneAddress = GuestMemoryBase + 0x200;
        const ulong dstAddress = GuestMemoryBase + 0x300;
        var memory = new FakeCpuMemory(GuestMemoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        context[CpuRegister.Rdi] = unchecked((ulong)guestLocalTime);
        context[CpuRegister.Rdx] = utcAddress;
        context[CpuRegister.Rcx] = timezoneAddress;
        context[CpuRegister.R8] = dstAddress;

        var result = KernelRuntimeCompatExports.KernelConvertLocaltimeToUtc(context);

        Assert.Equal(0, result);
        Assert.True(context.TryReadUInt64(utcAddress, out var rawUtc));
        Assert.True(context.TryReadUInt64(dstAddress, out var rawDst));
        Span<byte> timezone = stackalloc byte[8];
        Assert.True(memory.TryRead(timezoneAddress, timezone));
        var minutesWest = BinaryPrimitives.ReadInt32LittleEndian(timezone);
        var dstType = BinaryPrimitives.ReadInt32LittleEndian(timezone[4..]);
        var expectedUtc = checked(guestLocalTime + (long)minutesWest * 60 - unchecked((int)rawDst));

        Assert.Equal(expectedUtc, unchecked((long)rawUtc));
        Assert.Contains(dstType, new[] { 0, 4 });
    }

    [Fact]
    public void ConvertUtcToLocaltime_AcceptsGuestValuesOutsideDotNetDateRange()
    {
        const long guestUtcTime = 26_076_015_555_343_848;
        const ulong localAddress = GuestMemoryBase + 0x100;
        const ulong timesecAddress = GuestMemoryBase + 0x200;
        const ulong dstAddress = GuestMemoryBase + 0x300;
        var memory = new FakeCpuMemory(GuestMemoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        context[CpuRegister.Rdi] = unchecked((ulong)guestUtcTime);
        context[CpuRegister.Rsi] = localAddress;
        context[CpuRegister.Rdx] = timesecAddress;
        context[CpuRegister.Rcx] = dstAddress;

        var result = KernelRuntimeCompatExports.KernelConvertUtcToLocaltime(context);

        Assert.Equal(0, result);
        Assert.True(context.TryReadUInt64(localAddress, out var rawLocal));
        Assert.True(context.TryReadUInt64(dstAddress, out var rawDst));
        Span<byte> timesec = stackalloc byte[16];
        Assert.True(memory.TryRead(timesecAddress, timesec));
        var eastSeconds = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(timesec[8..]));
        var expectedLocal = checked(guestUtcTime + eastSeconds + unchecked((long)rawDst));

        Assert.Equal(expectedLocal, unchecked((long)rawLocal));
        Assert.Equal(guestUtcTime, BinaryPrimitives.ReadInt64LittleEndian(timesec));
    }

    [Fact]
    public void SysmoduleGetModuleInfoForUnwind_UsesKernelValidation()
    {
        var context = new CpuContext(new FakeCpuMemory(GuestMemoryBase, 0x1000), Generation.Gen5);
        context[CpuRegister.Rdi] = 0x8_0000_0000;
        context[CpuRegister.Rsi] = 0;
        context[CpuRegister.Rdx] = 0;

        var result = KernelRuntimeCompatExports.SysmoduleGetModuleInfoForUnwind(context);

        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT, result);
    }

    [Fact]
    public void IsSignalReturn_ReportsFalseWithoutGuestSignalTrampolines()
    {
        var context = new CpuContext(new FakeCpuMemory(GuestMemoryBase, 0x1000), Generation.Gen5);
        context[CpuRegister.Rdi] = 0x1E7_B2A4_00C5;
        context[CpuRegister.Rax] = ulong.MaxValue;

        var result = KernelRuntimeCompatExports.IsSignalReturn(context);

        Assert.Equal(0, result);
        Assert.Equal(0UL, context[CpuRegister.Rax]);
    }

    [Fact]
    public void ElfPhdrMatchAddr_RequiresAddressAndHandleToBelongToSameModule()
    {
        const ulong moduleInfoAddress = GuestMemoryBase + 0x100;
        const ulong moduleBase = GuestMemoryBase + 0x800;
        var memory = new FakeCpuMemory(GuestMemoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        KernelModuleRegistry.Reset();
        var handle = KernelModuleRegistry.RegisterModule(
            "fixture.sprx",
            moduleBase,
            size: 0x100,
            entryPoint: moduleBase,
            initEntryPoint: 0,
            finiEntryPoint: 0,
            initializerFunctions: [],
            finalizerFunctions: [],
            ehFrameHeaderAddress: 0,
            ehFrameAddress: 0,
            ehFrameSize: 0,
            isMain: false);
        Assert.True(context.TryWriteUInt32(moduleInfoAddress + 0x108, unchecked((uint)handle)));
        context[CpuRegister.Rdi] = moduleInfoAddress;
        context[CpuRegister.Rsi] = moduleBase + 0x20;

        Assert.Equal(0, KernelRuntimeCompatExports.ElfPhdrMatchAddr(context));
        Assert.Equal(1UL, context[CpuRegister.Rax]);

        context[CpuRegister.Rsi] = moduleBase + 0x200;
        Assert.Equal(0, KernelRuntimeCompatExports.ElfPhdrMatchAddr(context));
        Assert.Equal(0UL, context[CpuRegister.Rax]);

        Assert.True(context.TryWriteUInt32(moduleInfoAddress + 0x108, unchecked((uint)(handle + 1))));
        context[CpuRegister.Rsi] = moduleBase + 0x20;
        Assert.Equal(0, KernelRuntimeCompatExports.ElfPhdrMatchAddr(context));
        Assert.Equal(0UL, context[CpuRegister.Rax]);
    }

    [Fact]
    public void GetModuleList_InterpretsBufferSizeInBytes()
    {
        const ulong handlesAddress = GuestMemoryBase + 0x100;
        const ulong countAddress = GuestMemoryBase + 0x200;
        var memory = new FakeCpuMemory(GuestMemoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        KernelModuleRegistry.Reset();
        var first = RegisterModuleForList("first.sprx", GuestMemoryBase + 0x400);
        var second = RegisterModuleForList("second.sprx", GuestMemoryBase + 0x600);
        context[CpuRegister.Rdi] = handlesAddress;
        context[CpuRegister.Rsi] = sizeof(int);
        context[CpuRegister.Rdx] = countAddress;

        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT,
            KernelRuntimeCompatExports.KernelGetModuleList2(context));
        Assert.True(context.TryReadUInt64(countAddress, out var reportedCount));
        Assert.Equal(2UL, reportedCount);
        Assert.True(context.TryReadUInt32(handlesAddress, out var firstWritten));
        Assert.Equal(unchecked((uint)first), firstWritten);
        Assert.True(context.TryReadUInt32(handlesAddress + sizeof(int), out var untouched));
        Assert.Equal(0U, untouched);

        context[CpuRegister.Rsi] = 2 * sizeof(int);
        Assert.Equal(0, KernelRuntimeCompatExports.KernelGetModuleList2(context));
        Assert.True(context.TryReadUInt32(handlesAddress + sizeof(int), out var secondWritten));
        Assert.Equal(unchecked((uint)second), secondWritten);
    }

    private static int RegisterModuleForList(string path, ulong baseAddress)
    {
        return KernelModuleRegistry.RegisterModule(
            path,
            baseAddress,
            size: 0x100,
            entryPoint: baseAddress,
            initEntryPoint: 0,
            finiEntryPoint: 0,
            initializerFunctions: [],
            finalizerFunctions: [],
            ehFrameHeaderAddress: 0,
            ehFrameAddress: 0,
            ehFrameSize: 0,
            isMain: false);
    }
}
