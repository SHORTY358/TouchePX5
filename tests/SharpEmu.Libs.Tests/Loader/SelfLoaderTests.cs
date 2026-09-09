// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Text;
using SharpEmu.Core.Loader;
using SharpEmu.Core.Memory;
using Xunit;

namespace SharpEmu.Libs.Tests.Loader;

public sealed class SelfLoaderTests
{
    private const uint Ps4SelfMagic = 0x4F153D1D;
    private const uint Ps5SelfMagic = 0x5414F5EE;
    private const int SelfHeaderSize = 0x20;
    private const int ElfHeaderSize = 0x40;

    [Theory]
    [InlineData(Ps4SelfMagic, (byte)0x00, 0x0000_0101u, (ushort)0x22)]
    [InlineData(Ps5SelfMagic, (byte)0x00, 0x0000_0101u, (ushort)0x22)]
    [InlineData(Ps5SelfMagic, (byte)0x10, 0x1000_0101u, (ushort)0x32)]
    public void Load_AcceptsSupportedSelfHeaderVariants(
        uint magic,
        byte version,
        uint keyType,
        ushort flags)
    {
        var imageData = CreateSelfImage(magic, version, keyType, flags);

        var image = new SelfLoader().Load(imageData, new VirtualMemory());

        Assert.True(image.IsSelf);
        Assert.Equal(2, image.ElfHeader.AbiVersion);
        Assert.Empty(image.ProgramHeaders);
        Assert.Empty(image.MappedRegions);
    }

    [Theory]
    [InlineData(0x05, (byte)0x00)]
    [InlineData(0x06, (byte)0x02)]
    [InlineData(0x07, (byte)0x00)]
    public void Load_RejectsUnsupportedStructuralSelfHeaderValues(int offset, byte value)
    {
        var imageData = CreateSelfImage(Ps5SelfMagic, 0x10, 0x1000_0101, 0x32);
        imageData[offset] = value;

        Assert.Throws<InvalidDataException>(() =>
            new SelfLoader().Load(imageData, new VirtualMemory()));
    }

    [Theory]
    [InlineData(0xDEADBEEF)]
    [InlineData(0x7F454C47)] // bare ELF magic read big-endian as a "SELF" candidate is not a SELF
    public void Load_RejectsUnrecognizedLeadingMagic(uint magic)
    {
        var imageData = new byte[SelfHeaderSize + ElfHeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(imageData, magic);

        Assert.Throws<InvalidDataException>(() =>
            new SelfLoader().Load(imageData, new VirtualMemory()));
    }

    [Fact]
    public void Load_RejectsImageSmallerThanElfHeader()
    {
        // A few bytes short of ElfHeaderSize (0x40); ParseLayout guards this
        // before any magic dispatch, so the error is deterministic for both
        // SELF and ELF inputs.
        var imageData = new byte[ElfHeaderSize - 1];

        Assert.Throws<InvalidDataException>(() =>
            new SelfLoader().Load(imageData, new VirtualMemory()));
    }

    [Fact]
    public void Load_RejectsTruncatedSelfHeader()
    {
        // SELF magic is present and recognized, but the image ends before the
        // SELF header + embedded ELF header can be read. ParseLayout computes
        // elfOffset = SelfHeaderSize + segments*SelfSegmentSize and then
        // EnsureRange must fail.
        var imageData = new byte[SelfHeaderSize + ElfHeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(imageData, Ps5SelfMagic);
        imageData[0x05] = 0x01;
        imageData[0x06] = 0x01;
        imageData[0x07] = 0x12;
        var truncated = imageData.AsSpan(0, SelfHeaderSize + 0x10).ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new SelfLoader().Load(truncated, new VirtualMemory()));
    }

    [Fact]
    public void Load_ParsesEmbeddedElfHeaderFromSelfContainer()
    {
        var imageData = CreateSelfImage(Ps5SelfMagic, 0x10, 0x1000_0101, 0x32);

        var image = new SelfLoader().Load(imageData, new VirtualMemory());

        Assert.True(image.IsSelf);
        // The ELF header parsed out of the SELF container must be a valid x86-64
        // ELF64 little-endian image with the PS5 ABI marker that drives Gen5
        // selection in SharpEmuRuntime.
        Assert.True(image.ElfHeader.HasElfMagic);
        Assert.True(image.ElfHeader.Is64Bit);
        Assert.True(image.ElfHeader.IsLittleEndian);
        Assert.Equal(2, image.ElfHeader.AbiVersion);
        Assert.Equal(62, image.ElfHeader.Machine);
    }

    [Fact]
    public void Load_AcceptsBareDecryptedElf()
    {
        // A decrypted eboot that has already been stripped of its SELF wrapper
        // is accepted directly; IsSelf must be false and the ELF header is read
        // from offset 0.
        var imageData = new byte[ElfHeaderSize];
        WriteMinimalElfHeader(imageData);

        var image = new SelfLoader().Load(imageData, new VirtualMemory());

        Assert.False(image.IsSelf);
        Assert.True(image.ElfHeader.HasElfMagic);
        Assert.True(image.ElfHeader.Is64Bit);
        Assert.Equal(62, image.ElfHeader.Machine);
        Assert.Empty(image.ProgramHeaders);
        Assert.Empty(image.MappedRegions);
    }

    [Fact]
    public void Load_ResolvesSceImportTablesFromUnmappedDynlibSegment()
    {
        var imageData = CreateElfWithSceDynlibImport();
        var memory = new VirtualMemory();

        var image = new SelfLoader().Load(imageData, memory);

        var import = Assert.Single(image.ImportStubs);
        Assert.Equal("HoLVWNanBBc", import.Value);

        Span<byte> patchedSlot = stackalloc byte[sizeof(ulong)];
        Assert.True(memory.TryRead(0x0000_0008_0000_0300, patchedSlot));
        Assert.Equal(import.Key, BinaryPrimitives.ReadUInt64LittleEndian(patchedSlot));
    }

    private static byte[] CreateSelfImage(uint magic, byte version, uint keyType, ushort flags)
    {
        var imageData = new byte[SelfHeaderSize + ElfHeaderSize];
        var selfHeader = imageData.AsSpan(0, SelfHeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(selfHeader, magic);
        selfHeader[0x04] = version;
        selfHeader[0x05] = 0x01;
        selfHeader[0x06] = 0x01;
        selfHeader[0x07] = 0x12;
        BinaryPrimitives.WriteUInt32LittleEndian(selfHeader[0x08..], keyType);
        BinaryPrimitives.WriteUInt16LittleEndian(selfHeader[0x0C..], SelfHeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(selfHeader[0x10..], (ulong)imageData.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(selfHeader[0x18..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(selfHeader[0x1A..], flags);

        WriteMinimalElfHeader(imageData.AsSpan(SelfHeaderSize, ElfHeaderSize));
        return imageData;
    }

    private static void WriteMinimalElfHeader(Span<byte> header)
    {
        header.Clear();
        header[0x00] = 0x7F;
        header[0x01] = (byte)'E';
        header[0x02] = (byte)'L';
        header[0x03] = (byte)'F';
        header[0x04] = 2;
        header[0x05] = 1;
        header[0x06] = 1;
        header[0x07] = 9;
        header[0x08] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x10..], 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x12..], 62);
        BinaryPrimitives.WriteUInt32LittleEndian(header[0x14..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(header[0x20..], ElfHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x34..], ElfHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header[0x36..], 0x38);
    }

    private static byte[] CreateElfWithSceDynlibImport()
    {
        const int loadSize = 0x600;
        const int dynamicOffset = 0x400;
        const int dynlibOffset = 0x800;
        const int dynlibSize = 0x70;
        const int symbolOffset = 0x20;
        const int jumpRelocationOffset = 0x50;
        const int relocationTarget = 0x300;
        var image = new byte[0x1000];
        WriteMinimalElfHeader(image);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x10), 0xFE10);
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(0x18), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x38), 3);

        WriteProgramHeader(image.AsSpan(0x40), 1, 7, 0, 0, loadSize, loadSize, 0x1000);
        WriteProgramHeader(image.AsSpan(0x78), 2, 6, dynamicOffset, dynamicOffset, 0xA0, 0xA0, 8);
        WriteProgramHeader(
            image.AsSpan(0xB0),
            0x61000000,
            4,
            dynlibOffset,
            dynlibOffset,
            dynlibSize,
            dynlibSize,
            8);

        var importName = Encoding.ASCII.GetBytes("\0HoLVWNanBBc#B#B\0");
        importName.CopyTo(image.AsSpan(dynlibOffset));
        var symbol = image.AsSpan(dynlibOffset + symbolOffset + 24, 24);
        BinaryPrimitives.WriteUInt32LittleEndian(symbol, 1);
        symbol[4] = 0x12;

        var relocation = image.AsSpan(dynlibOffset + jumpRelocationOffset, 24);
        BinaryPrimitives.WriteUInt64LittleEndian(relocation, relocationTarget);
        BinaryPrimitives.WriteUInt64LittleEndian(relocation[8..], 0x0000_0001_0000_0007);

        var dynamic = image.AsSpan(dynamicOffset);
        var dynamicIndex = 0;
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x61000035, 0);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x61000037, (ulong)importName.Length);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x61000039, symbolOffset);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x6100003B, 24);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x6100003F, 48);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x61000027, 0x280);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x6100002B, 7);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x61000029, jumpRelocationOffset);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x6100002D, 24);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0, 0);
        return image;
    }

    private static void WriteProgramHeader(
        Span<byte> destination,
        uint type,
        uint flags,
        ulong fileOffset,
        ulong virtualAddress,
        ulong fileSize,
        ulong memorySize,
        ulong alignment)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, type);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], flags);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], fileOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], virtualAddress);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], virtualAddress);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[32..], fileSize);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[40..], memorySize);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[48..], alignment);
    }

    private static void WriteDynamicEntry(Span<byte> table, ref int index, long tag, ulong value)
    {
        var entry = table.Slice(index * 16, 16);
        BinaryPrimitives.WriteInt64LittleEndian(entry, tag);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], value);
        index++;
    }
}
