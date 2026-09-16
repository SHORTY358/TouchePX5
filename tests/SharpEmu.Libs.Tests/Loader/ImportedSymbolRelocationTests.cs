// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.Core.Loader;
using SharpEmu.Core.Memory;
using Xunit;

namespace SharpEmu.Libs.Tests.Loader;

public sealed class ImportedSymbolRelocationTests
{
    [Theory]
    [InlineData(false, 0xFFFFFFFFUL)]
    [InlineData(true, 0x7FFFFFFFUL)]
    [InlineData(true, 0xFFFFFFFF80000000UL)]
    public void FourByteWrite_PreservesNeighbor(bool signed, ulong value)
    {
        var memory = CreateMemory();
        var relocation = new ImportedSymbolRelocation(0x10000, 0, "probe", true, 4, signed);
        Assert.True(relocation.TryWriteResolvedValue(memory, value));
        var bytes = new byte[8];
        Assert.True(memory.TryRead(0x10000, bytes));
        Assert.Equal(unchecked((uint)value), BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(0xCCCCCCCCU, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
    }

    [Theory]
    [InlineData(false, 0x100000000UL)]
    [InlineData(true, 0x80000000UL)]
    [InlineData(true, 0xFFFFFFFF7FFFFFFFUL)]
    public void Overflow_DoesNotModifyMemory(bool signed, ulong value)
    {
        var memory = CreateMemory();
        var relocation = new ImportedSymbolRelocation(0x10000, 0, "probe", true, 4, signed);
        Assert.False(relocation.TryWriteResolvedValue(memory, value));
        var bytes = new byte[8];
        Assert.True(memory.TryRead(0x10000, bytes));
        Assert.All(bytes, value => Assert.Equal((byte)0xCC, value));
    }

    [Fact]
    public void EightByteWrite_RetainsHighAddressBits()
    {
        var memory = CreateMemory();
        var relocation = new ImportedSymbolRelocation(0x10000, 0, "probe", true);
        Assert.True(relocation.TryWriteResolvedValue(memory, 0x804204000UL));
        var bytes = new byte[8];
        Assert.True(memory.TryRead(0x10000, bytes));
        Assert.Equal(0x804204000UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes));
    }

    private static VirtualMemory CreateMemory()
    {
        var memory = new VirtualMemory();
        memory.Map(0x10000, 0x1000, 0, Enumerable.Repeat((byte)0xCC, 8).ToArray(),
            ProgramHeaderFlags.Read | ProgramHeaderFlags.Write);
        return memory;
    }
}
