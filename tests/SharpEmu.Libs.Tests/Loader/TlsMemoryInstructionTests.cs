// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Iced.Intel;
using SharpEmu.Core.Cpu.Native;
using Xunit;

namespace SharpEmu.Libs.Tests.Loader;

public sealed class TlsMemoryInstructionTests
{
    [Theory]
    [InlineData("648B0425F0FFFFFF", Register.EAX, 8, -16)]
    [InlineData("64448B0425F0FFFFFF", Register.R8D, 9, -16)]
    [InlineData("64488B142500000000", Register.RDX, 9, 0)]
    [InlineData("644C8B0C25F0FFFFFF", Register.R9, 9, -16)]
    public void Helper_LoadsCorrectWidthAndPreservesOtherRegisters(string hex, Register destination, int length, int displacement)
    {
        Assert.True(TlsMemoryInstruction.TryDecode(Convert.FromHexString(hex), out var patch));
        Assert.Equal(length, patch.Length);
        Assert.Equal(displacement, patch.Displacement);
        var bytes = patch.CreateHelperBytes(0x100000, 0x101000);
        Assert.NotNull(bytes);
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes));
        decoder.IP = 0x100000;
        var push = decoder.Decode();
        Assert.Equal(Mnemonic.Push, push.Mnemonic);
        Assert.Equal(Register.RAX, push.Op0Register);
        var call = decoder.Decode();
        Assert.Equal(Mnemonic.Call, call.Mnemonic);
        Assert.Equal(0x101000UL, call.NearBranchTarget);
        var load = decoder.Decode();
        Assert.Equal(Mnemonic.Mov, load.Mnemonic);
        Assert.Equal(destination, load.Op0Register);
        Assert.Equal(Register.RAX, load.MemoryBase);
        Assert.Equal(unchecked((ulong)(long)displacement), load.MemoryDisplacement64);
        var restore = decoder.Decode();
        if (destination == Register.EAX || destination == Register.RAX)
        {
            Assert.Equal(Mnemonic.Lea, restore.Mnemonic);
            Assert.Equal(Register.RSP, restore.Op0Register);
            Assert.Equal(Register.RSP, restore.MemoryBase);
            Assert.Equal(8UL, restore.MemoryDisplacement64);
        }
        else
        {
            Assert.Equal(Mnemonic.Pop, restore.Mnemonic);
            Assert.Equal(Register.RAX, restore.Op0Register);
        }
        Assert.Equal(Mnemonic.Ret, decoder.Decode().Mnemonic);
    }

    [Theory]
    [InlineData("64488B0425")]
    [InlineData("66488B042500000000")]
    [InlineData("65488B042500000000")]
    [InlineData("64488B242500000000")]
    [InlineData("644A8B042500000000")]
    public void Decoder_RejectsUnsupportedOrTruncatedForms(string hex)
    {
        Assert.False(TlsMemoryInstruction.TryDecode(Convert.FromHexString(hex), out _));
    }

    [Fact]
    public void Helper_RejectsUnreachableHandler()
    {
        Assert.True(TlsMemoryInstruction.TryDecode(Convert.FromHexString("64488B042500000000"), out var patch));
        Assert.Null(patch.CreateHelperBytes(0x10000, 0x900000000));
    }

    [Theory]
    [InlineData("64890425F8FFFFFF", Register.ECX, true)]
    [InlineData("6448890425F8FFFFFF", Register.RCX, true)]
    [InlineData("6444890425F8FFFFFF", Register.R8D, false)]
    [InlineData("644C890C25F8FFFFFF", Register.R9, false)]
    public void StoreHelper_PreservesSourcesAndEmitsCorrectWidth(string hex, Register source, bool savedRaxSource)
    {
        Assert.True(TlsMemoryInstruction.TryDecode(Convert.FromHexString(hex), out var patch));
        Assert.True(patch.IsStore);
        var bytes = patch.CreateHelperBytes(0x100000, 0x101000);
        Assert.NotNull(bytes);
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes));
        Assert.Equal(Mnemonic.Push, decoder.Decode().Mnemonic);
        Assert.Equal(Mnemonic.Call, decoder.Decode().Mnemonic);
        if (savedRaxSource)
        {
            Assert.Equal(Register.RCX, decoder.Decode().Op0Register);
            var recover = decoder.Decode();
            Assert.Equal(Mnemonic.Mov, recover.Mnemonic);
            Assert.Equal(Register.RCX, recover.Op0Register);
            Assert.Equal(Register.RSP, recover.MemoryBase);
            Assert.Equal(8UL, recover.MemoryDisplacement64);
        }
        var store = decoder.Decode();
        Assert.Equal(Mnemonic.Mov, store.Mnemonic);
        Assert.Equal(OpKind.Memory, store.Op0Kind);
        Assert.Equal(Register.RAX, store.MemoryBase);
        Assert.Equal(unchecked((ulong)-8L), store.MemoryDisplacement64);
        Assert.Equal(source, store.Op1Register);
        if (savedRaxSource) { Assert.Equal(Register.RCX, decoder.Decode().Op0Register); }
        var restore = decoder.Decode();
        Assert.Equal(Mnemonic.Pop, restore.Mnemonic);
        Assert.Equal(Register.RAX, restore.Op0Register);
        Assert.Equal(Mnemonic.Ret, decoder.Decode().Mnemonic);
    }
}
