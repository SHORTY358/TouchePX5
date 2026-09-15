// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Iced.Intel;
using SharpEmu.Core.Cpu.Native;
using Xunit;

namespace SharpEmu.Libs.Tests.Loader;

public sealed class TlsImmediateStoreInstructionTests
{
    [Theory]
    [InlineData("6664C70425F8FFFFFF2143")]
    [InlineData("6466C70425F8FFFFFF2143")]
    public void WordStore_ConsumesOnlyTwoImmediateBytes(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.True(TlsImmediateStoreInstruction.TryDecode(bytes, out var instruction));
        Assert.True(instruction.Is16Bit);
        Assert.False(instruction.Is64Bit);
        Assert.Equal(bytes.Length, instruction.Length);
        Assert.Equal(0x4321, instruction.Immediate);
        Assert.False(TlsImmediateStoreInstruction.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _));
        var code = instruction.CreateHelperBytes(0x100000, 0x101000);
        Assert.NotNull(code);
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(code));
        decoder.Decode();
        decoder.Decode();
        var store = decoder.Decode();
        Assert.Equal(MemorySize.UInt16, store.MemorySize);
        Assert.Equal(0x4321UL, store.GetImmediate(1));
        Assert.Equal(Register.RAX, store.MemoryBase);
        Assert.Equal(Mnemonic.Pop, decoder.Decode().Mnemonic);
        Assert.Equal(Mnemonic.Ret, decoder.Decode().Mnemonic);
    }

    [Theory]
    [InlineData("64C70425F8FFFFFFFFFFFFFF", false, -1L)]
    [InlineData("6448C70425F8FFFFFFFFFFFFFF", true, -1L)]
    [InlineData("6448C70425F8FFFFFF00000080", true, -2147483648L)]
    [InlineData("6448C70425F8FFFFFFFFFFFF7F", true, 2147483647L)]
    public void Helper_PreservesWidthSignedImmediateAndRegisters(string hex, bool wide, long value)
    {
        Assert.True(TlsImmediateStoreInstruction.TryDecode(Convert.FromHexString(hex), out var instruction));
        Assert.Equal(hex.Length / 2, instruction.Length);
        Assert.Equal(wide, instruction.Is64Bit);
        Assert.Equal(-8, instruction.Displacement);
        Assert.Equal(value, instruction.Immediate);
        var code = instruction.CreateHelperBytes(0x100000, 0x101000);
        Assert.NotNull(code);
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(code));
        decoder.IP = 0x100000;
        var push = decoder.Decode();
        Assert.Equal(Mnemonic.Push, push.Mnemonic);
        Assert.Equal(Register.RAX, push.Op0Register);
        Assert.Equal(0x101000UL, decoder.Decode().NearBranchTarget);
        var store = decoder.Decode();
        Assert.Equal(Mnemonic.Mov, store.Mnemonic);
        Assert.Equal(Register.RAX, store.MemoryBase);
        Assert.Equal(wide ? MemorySize.UInt64 : MemorySize.UInt32, store.MemorySize);
        Assert.Equal(unchecked((ulong)-8L), store.MemoryDisplacement64);
        Assert.Equal(wide ? unchecked((ulong)value) : unchecked((uint)value), store.GetImmediate(1));
        var pop = decoder.Decode();
        Assert.Equal(Mnemonic.Pop, pop.Mnemonic);
        Assert.Equal(Register.RAX, pop.Op0Register);
        Assert.Equal(Mnemonic.Ret, decoder.Decode().Mnemonic);
    }

    [Theory]
    [InlineData("6448C70425F8FFFFFF000000")]
    [InlineData("65C70425F8FFFFFFFFFFFFFF")]
    [InlineData("6449C70425F8FFFFFFFFFFFFFF")]
    [InlineData("64C74425F8FFFFFFFFFFFFFF")]
    [InlineData("64C70465F8FFFFFFFFFFFFFF")]
    [InlineData("64C70C25F8FFFFFFFFFFFFFF")]
    public void Decoder_RejectsUnsupportedOrTruncatedForms(string hex)
    {
        Assert.False(TlsImmediateStoreInstruction.TryDecode(Convert.FromHexString(hex), out _));
    }

    [Fact]
    public void Helper_RejectsUnreachableHandler()
    {
        var instruction = new TlsImmediateStoreInstruction(13, true, -8, -1);
        Assert.Null(instruction.CreateHelperBytes(0x10000, 0x900000000));
    }
}
