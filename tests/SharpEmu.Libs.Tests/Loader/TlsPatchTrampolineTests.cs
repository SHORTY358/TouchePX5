// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Iced.Intel;
using SharpEmu.Core.Cpu.Native;
using Xunit;

namespace SharpEmu.Libs.Tests.Loader;

public sealed class TlsPatchTrampolineTests
{
    [Theory]
    [InlineData(0x100000UL, 0x200000UL)]
    [InlineData(0x200000UL, 0x100000UL)]
    public void Prefix_ReservesRedZoneBeforeCallingAndRestoresBeforeJumping(ulong address, ulong continuation)
    {
        var bytes = TlsPatchTrampoline.CreatePrefix(address, continuation);
        Assert.NotNull(bytes);
        Assert.Equal(TlsPatchTrampoline.PrefixSize, bytes.Length);
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes));
        decoder.IP = address;
        var reserve = decoder.Decode();
        Assert.Equal(Mnemonic.Lea, reserve.Mnemonic);
        Assert.Equal(Register.RSP, reserve.Op0Register);
        Assert.Equal(Register.RSP, reserve.MemoryBase);
        Assert.Equal(unchecked((ulong)-128L), reserve.MemoryDisplacement64);
        var call = decoder.Decode();
        Assert.Equal(Mnemonic.Call, call.Mnemonic);
        Assert.Equal(address + TlsPatchTrampoline.PrefixSize, call.NearBranchTarget);
        var restore = decoder.Decode();
        Assert.Equal(Mnemonic.Lea, restore.Mnemonic);
        Assert.Equal(Register.RSP, restore.Op0Register);
        Assert.Equal(Register.RSP, restore.MemoryBase);
        Assert.Equal(128UL, restore.MemoryDisplacement64);
        var jump = decoder.Decode();
        Assert.Equal(Mnemonic.Jmp, jump.Mnemonic);
        Assert.Equal(continuation, jump.NearBranchTarget);
    }

    [Theory]
    [InlineData(0x10000UL, 0x900000000UL)]
    [InlineData(0x900000000UL, 0x10000UL)]
    public void Prefix_RejectsUnreachableContinuation(ulong address, ulong continuation)
    {
        Assert.Null(TlsPatchTrampoline.CreatePrefix(address, continuation));
    }
}
