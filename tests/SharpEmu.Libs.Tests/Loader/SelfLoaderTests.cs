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
        Assert.Equal(["libFixtureDependency"], image.NeededModules);
        Assert.Equal(["libFixtureConsumer"], image.ModuleNames);

        Span<byte> patchedSlot = stackalloc byte[sizeof(ulong)];
        Assert.True(memory.TryRead(0x0000_0008_0000_0300, patchedSlot));
        Assert.Equal(import.Key, BinaryPrimitives.ReadUInt64LittleEndian(patchedSlot));
    }

    [Fact]
    public void Load_ResolvesSceImportTablesFromSelfSegmentMappings()
    {
        var imageData = WrapElfImportFixtureInSelf(CreateElfWithSceDynlibImport());
        var memory = new VirtualMemory();

        var image = new SelfLoader().Load(imageData, memory);

        Assert.True(image.IsSelf);
        var import = Assert.Single(image.ImportStubs);
        Assert.Equal("HoLVWNanBBc", import.Value);

        Span<byte> patchedSlot = stackalloc byte[sizeof(ulong)];
        Assert.True(memory.TryRead(0x0000_0008_0000_0300, patchedSlot));
        Assert.Equal(import.Key, BinaryPrimitives.ReadUInt64LittleEndian(patchedSlot));
    }

    [Fact]
    public void Load_CollectsFinalizerArrayInReverseOrderBeforeFiniFunction()
    {
        var image = new SelfLoader().Load(CreateElfWithLifecycleFunctions(), new VirtualMemory());
        var imageBase = image.EntryPoint - image.ElfHeader.EntryPoint;

        Assert.Equal(
            [imageBase + 0x252, imageBase + 0x251, imageBase + 0x250, imageBase + 0x260],
            image.FinalizerFunctions);
    }

    [Theory]
    [InlineData(0x12, 0, 1, 0x200UL, true)] // global function
    [InlineData(0x22, 0, 1, 0x200UL, true)] // weak function
    [InlineData(0x12, 3, 1, 0x200UL, true)] // protected function
    [InlineData(0x11, 0, 1, 0x300UL, true)] // data object
    [InlineData(0x12, 0, 1, 0UL, true)] // valid definition at image base
    [InlineData(0x12, 0, 0xFFF1, 0x20000UL, true)] // absolute address
    [InlineData(0x02, 0, 1, 0x200UL, false)] // local
    [InlineData(0x12, 1, 1, 0x200UL, false)] // internal
    [InlineData(0x12, 2, 1, 0x200UL, false)] // hidden
    [InlineData(0x12, 0, 0, 0x200UL, false)] // undefined, even with a nonzero value
    [InlineData(0x16, 0, 1, 0x200UL, false)] // TLS offset is not an address
    [InlineData(0x12, 0, 0xFFF2, 0x200UL, false)] // unallocated common
    public void Load_IndexesOnlyPublicDynamicAddresses(
        byte info, byte visibility, ushort section, ulong value, bool exported)
    {
        var bytes = CreateElfWithSceDynlibImport();
        var symbol = bytes.AsSpan(0x858, 24);
        symbol[4] = info;
        symbol[5] = visibility;
        BinaryPrimitives.WriteUInt16LittleEndian(symbol[6..], section);
        BinaryPrimitives.WriteUInt64LittleEndian(symbol[8..], value);
        // This fixture now contains a definition; disable its original import relocation.
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x488), 0);

        var image = new SelfLoader().Load(bytes, new VirtualMemory());
        Assert.Equal(exported, image.ExportedSymbols.TryGetValue("HoLVWNanBBc#B#B", out var address));
        Assert.Equal(exported, image.ExportedSymbols.TryGetValue("HoLVWNanBBc", out var nidAddress));
        if (exported)
        {
            var imageBase = image.EntryPoint - image.ElfHeader.EntryPoint;
            Assert.Equal(section == 0xFFF1 ? value : imageBase + value, address);
            Assert.Equal(address, nidAddress);
        }
        else
        {
            Assert.Empty(image.ExportedSymbols);
        }
    }

    [Theory]
    [InlineData(25, 27, false)] // INIT_ARRAY, forward order
    [InlineData(26, 28, true)] // FINI_ARRAY, reverse order
    [InlineData(32, 33, false)] // PREINIT_ARRAY, forward order
    public void Load_PreservesRepeatedLifecycleSlots(long arrayTag, long sizeTag, bool reverse)
    {
        var bytes = CreateElfWithLifecycleFunctions();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x350), 0x250);
        var table = bytes.AsSpan(0x400, 0x90);
        table.Clear();
        var index = 0;
        WriteDynamicEntry(table, ref index, arrayTag, 0x340);
        WriteDynamicEntry(table, ref index, sizeTag, 24);
        WriteDynamicEntry(table, ref index, arrayTag == 26 ? 13 : 12, 0x250);
        WriteDynamicEntry(table, ref index, 0, 0);

        var image = new SelfLoader().Load(bytes, new VirtualMemory());
        var imageBase = image.EntryPoint - image.ElfHeader.EntryPoint;
        var actual = arrayTag == 32 ? image.PreInitializerFunctions
            : reverse ? image.FinalizerFunctions : image.InitializerFunctions;
        ulong[] offsets = arrayTag == 32 ? [0x250, 0x251, 0x250]
            : reverse ? [0x250, 0x251, 0x250, 0x250] : [0x250, 0x250, 0x251, 0x250];
        Assert.Equal(offsets.Select(offset => imageBase + offset), actual);
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
        const int dynlibSize = 0x88;
        const int symbolOffset = 0x40;
        const int jumpRelocationOffset = 0x70;
        const int relocationTarget = 0x300;
        var image = new byte[0x1000];
        WriteMinimalElfHeader(image);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x10), 0xFE10);
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(0x18), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x38), 3);

        WriteProgramHeader(image.AsSpan(0x40), 1, 7, 0, 0, loadSize, loadSize, 0x1000);
        WriteProgramHeader(image.AsSpan(0x78), 2, 6, dynamicOffset, dynamicOffset, 0xC0, 0xC0, 8);
        WriteProgramHeader(
            image.AsSpan(0xB0),
            0x61000000,
            4,
            dynlibOffset,
            dynlibOffset,
            dynlibSize,
            dynlibSize,
            8);

        var importName = Encoding.ASCII.GetBytes("\0HoLVWNanBBc#B#B\0libFixtureDependency\0libFixtureConsumer\0");
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
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x6100000F, 0x0001_0101_0000_0011);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x6100000D, 0x0001_0101_0000_0026);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0, 0);
        return image;
    }

    private static byte[] CreateElfWithLifecycleFunctions()
    {
        const int loadSize = 0x600;
        const int dynamicOffset = 0x400;
        var image = new byte[loadSize];
        WriteMinimalElfHeader(image);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x10), 0xFE10);
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(0x18), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x38), 2);

        WriteProgramHeader(image.AsSpan(0x40), 1, 7, 0, 0, loadSize, loadSize, 0x1000);
        WriteProgramHeader(image.AsSpan(0x78), 2, 6, dynamicOffset, dynamicOffset, 0x90, 0x90, 8);

        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(0x340), 0x250);
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(0x348), 0x251);
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(0x350), 0x252);

        var dynamic = image.AsSpan(dynamicOffset);
        var dynamicIndex = 0;
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x0D, 0x260);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x1A, 0x340);
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0x1C, 3 * sizeof(ulong));
        WriteDynamicEntry(dynamic, ref dynamicIndex, 0, 0);
        return image;
    }

    private static byte[] WrapElfImportFixtureInSelf(byte[] elf)
    {
        const int segmentCount = 3;
        const int embeddedElfOffset = SelfHeaderSize + (segmentCount * 0x20);
        const int loadPayloadOffset = 0x200;
        const int dynamicPayloadOffset = 0x800;
        const int dynlibPayloadOffset = 0x900;
        var image = new byte[0x1000];
        var selfHeader = image.AsSpan(0, SelfHeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(selfHeader, Ps5SelfMagic);
        selfHeader[0x04] = 0x10;
        selfHeader[0x05] = 0x01;
        selfHeader[0x06] = 0x01;
        selfHeader[0x07] = 0x12;
        BinaryPrimitives.WriteUInt32LittleEndian(selfHeader[0x08..], 0x1000_0101);
        BinaryPrimitives.WriteUInt16LittleEndian(selfHeader[0x0C..], embeddedElfOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(selfHeader[0x10..], (ulong)image.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(selfHeader[0x18..], segmentCount);
        BinaryPrimitives.WriteUInt16LittleEndian(selfHeader[0x1A..], 0x32);

        WriteSelfSegment(image.AsSpan(0x20), 0, loadPayloadOffset, 0x600);
        WriteSelfSegment(image.AsSpan(0x40), 1, dynamicPayloadOffset, 0xC0);
        WriteSelfSegment(image.AsSpan(0x60), 2, dynlibPayloadOffset, 0x88);
        elf.AsSpan(0, 0xE8).CopyTo(image.AsSpan(embeddedElfOffset));
        elf.AsSpan(0, 0x600).CopyTo(image.AsSpan(loadPayloadOffset));
        elf.AsSpan(0x400, 0xC0).CopyTo(image.AsSpan(dynamicPayloadOffset));
        elf.AsSpan(0x800, 0x88).CopyTo(image.AsSpan(dynlibPayloadOffset));
        return image;
    }

    private static void WriteSelfSegment(
        Span<byte> destination,
        int programHeaderIndex,
        ulong physicalOffset,
        ulong size)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(
            destination,
            0x800UL | ((ulong)programHeaderIndex << 20));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], physicalOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], size);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], size);
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
