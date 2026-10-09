// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcInitializationTests
{
    [Fact]
    public void TwoArgumentInitializerKeepsItsOriginalCallingConvention()
    {
        var ctx = new CpuContext(new DefaultsMemory(0x300000000), Generation.Gen5);
        ctx[CpuRegister.Rdi] = 0;
        ctx[CpuRegister.Rsi] = 8;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT, AgcExports.Init(ctx));
        ctx[CpuRegister.Rdi] = 0x300000100;
        Assert.Equal(0, AgcExports.Init(ctx));
        AgcExports.GetRegisterDefaults(ctx);
        Assert.NotEqual(0UL, ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void PublicInitializerReadsRdiAndDefaultsStayIsolatedPerGuest()
    {
        var first = new CpuContext(new DefaultsMemory(0x100000000), Generation.Gen5);
        var second = new CpuContext(new DefaultsMemory(0x200000000), Generation.Gen5);
        AgcExports.GetRegisterDefaults(first);
        Assert.Equal(0UL, first[CpuRegister.Rax]);
        first[CpuRegister.Rdi] = 8;
        first[CpuRegister.Rsi] = uint.MaxValue; // Must not read the two-argument ABI register.
        Assert.Equal(0, AgcExports.InitPublic(first));
        AgcExports.GetRegisterDefaults(first);
        var primary = first[CpuRegister.Rax];
        Assert.NotEqual(0UL, primary);
        AgcExports.GetRegisterDefaultsInternal(first);
        Assert.NotEqual(0UL, first[CpuRegister.Rax]);
        Assert.NotEqual(primary, first[CpuRegister.Rax]);
        AgcExports.GetRegisterDefaults(second);
        Assert.Equal(0UL, second[CpuRegister.Rax]);
        first[CpuRegister.Rdi] = uint.MaxValue;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT, AgcExports.InitPublic(first));
        AgcExports.GetRegisterDefaults(first);
        Assert.Equal(primary, first[CpuRegister.Rax]); // Rejected reinit preserves existing defaults.
    }

    private sealed class DefaultsMemory(ulong baseAddress) : ICpuMemory, IGuestAddressSpace
    {
        private readonly FakeCpuMemory storage = new(baseAddress, 0x20000);
        public bool TryRead(ulong address, Span<byte> bytes) => storage.TryRead(address, bytes);
        public bool TryWrite(ulong address, ReadOnlySpan<byte> bytes) => storage.TryWrite(address, bytes);
        public bool TryAllocateGuestMemory(ulong size, ulong alignment, out ulong address) => storage.TryAllocateGuestMemory(size, alignment, out address);
        public bool TryFreeGuestMemory(ulong address) => false;
        public ulong AllocateAt(ulong address, ulong size, bool executable = true, bool allowAlternative = true) => 0;
        public bool TryBackFixedRange(ulong address, ulong size, bool executable) => false;
        public bool TryAllocateAtOrAbove(ulong address, ulong size, bool executable, ulong alignment, out ulong actual) => storage.TryAllocateGuestMemory(size, alignment, out actual);
        public bool TryProtect(ulong address, ulong size, GuestPageProtection protection) => false;
    }
}
