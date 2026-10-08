// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using SharpEmu.Libs.LibcStdio;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

[Collection(KernelMemoryCompatStateCollection.Name)]
public sealed class LibcFileWriteTests
{
    private const ulong Address = 0x100000000;

    [Fact]
    public void FwriteWritesOwnedHeapBytesAndRejectsAllocationOverrun()
    {
        var path = Path.Combine(Path.GetTempPath(), "touche-write-" + Guid.NewGuid().ToString("N") + ".bin");
        var memory = new FileObjectMemory();
        var ctx = new CpuContext(memory, Generation.Gen5);
        KernelMemoryCompatExports.RegisterGuestPathMount("/touche_write_test", Path.GetTempPath());
        ulong file = 0, heap = 0;
        try
        {
            memory.Storage.WriteCString(Address, "/touche_write_test/" + Path.GetFileName(path));
            memory.Storage.WriteCString(Address + 0x400, "wb");
            ctx[CpuRegister.Rdi] = Address;
            ctx[CpuRegister.Rsi] = Address + 0x400;
            Assert.Equal(0, LibcStdioExports.Fopen(ctx));
            file = ctx[CpuRegister.Rax];
            Assert.NotEqual(0UL, file);
            ctx[CpuRegister.Rdi] = 6;
            Assert.Equal(0, KernelMemoryCompatExports.Malloc(ctx));
            heap = ctx[CpuRegister.Rax];
            byte[] expected = [0, 255, 1, 128, 0, 77];
            Marshal.Copy(expected, 0, (nint)heap, expected.Length);
            ctx[CpuRegister.Rdi] = heap;
            ctx[CpuRegister.Rsi] = 2;
            ctx[CpuRegister.Rdx] = 3;
            ctx[CpuRegister.Rcx] = file;
            Assert.Equal(0, LibcStdioExports.Fwrite(ctx));
            Assert.Equal(3UL, ctx[CpuRegister.Rax]);
            Assert.Equal(0, LibcStdioExports.Fflush(SetStream(ctx, file)));
            // A write-only stream is rejected before FileStream.Read can throw.
            ctx[CpuRegister.Rdi] = Address + 0x800;
            ctx[CpuRegister.Rsi] = 1;
            ctx[CpuRegister.Rdx] = 1;
            ctx[CpuRegister.Rcx] = file;
            Assert.True(LibcStdioExports.Fread(ctx) < 0);
            Assert.Equal(0UL, ctx[CpuRegister.Rax]);
            ctx[CpuRegister.Rdi] = heap + 4;
            ctx[CpuRegister.Rsi] = 3;
            ctx[CpuRegister.Rdx] = 1;
            ctx[CpuRegister.Rcx] = file;
            Assert.True(LibcStdioExports.Fwrite(ctx) < 0);
            Assert.Equal(0UL, ctx[CpuRegister.Rax]);
            Assert.Equal(0, LibcStdioExports.Fclose(SetStream(ctx, file)));
            file = 0;
            Assert.Equal(expected, File.ReadAllBytes(path));
            memory.Storage.WriteCString(Address + 0x400, "rb");
            ctx[CpuRegister.Rdi] = Address;
            ctx[CpuRegister.Rsi] = Address + 0x400;
            Assert.Equal(0, LibcStdioExports.Fopen(ctx));
            file = ctx[CpuRegister.Rax];
            ctx[CpuRegister.Rdi] = heap;
            ctx[CpuRegister.Rsi] = ulong.MaxValue;
            ctx[CpuRegister.Rdx] = 2;
            ctx[CpuRegister.Rcx] = file;
            Assert.True(LibcStdioExports.Fread(ctx) < 0);
            Assert.Equal(0UL, ctx[CpuRegister.Rax]);
            ctx[CpuRegister.Rsi] = 2;
            ctx[CpuRegister.Rdx] = 3;
            Assert.Equal(0, LibcStdioExports.Fread(ctx));
            Assert.Equal(3UL, ctx[CpuRegister.Rax]);
            var actual = new byte[6];
            Marshal.Copy((nint)heap, actual, 0, actual.Length);
            Assert.Equal(expected, actual);
            Assert.True(LibcStdioExports.Fwrite(ctx) < 0);
            Assert.Equal(0UL, ctx[CpuRegister.Rax]);
        }
        finally
        {
            if (file != 0) LibcStdioExports.Fclose(SetStream(ctx, file));
            ctx[CpuRegister.Rdi] = heap;
            if (heap != 0) KernelMemoryCompatExports.Free(ctx);
            File.Delete(path);
            KernelMemoryCompatExports.UnregisterGuestPathMount("/touche_write_test");
        }
    }

    [Fact]
    public void FopenDeniedHostPathReturnsNullInsteadOfThrowing()
    {
        var memory = new FakeCpuMemory(Address, 1024);
        var ctx = new CpuContext(memory, Generation.Gen5);
        memory.WriteCString(Address, Path.Combine(Path.GetTempPath(), "touche-denied-" + Guid.NewGuid().ToString("N") + ".bin"));
        memory.WriteCString(Address + 512, "wb");
        ctx[CpuRegister.Rdi] = Address;
        ctx[CpuRegister.Rsi] = Address + 512;
        Assert.True(LibcStdioExports.Fopen(ctx) < 0);
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
    }

    [Theory]
    [InlineData(0UL, 1UL, 1UL)]
    [InlineData(0x1234UL, ulong.MaxValue, 2UL)]
    public void NullStreamOrSizeOverflowReturnsZeroElements(ulong handle, ulong size, ulong count)
    {
        var ctx = new CpuContext(new FakeCpuMemory(Address, 32), Generation.Gen5);
        ctx[CpuRegister.Rdi] = Address;
        ctx[CpuRegister.Rsi] = size;
        ctx[CpuRegister.Rdx] = count;
        ctx[CpuRegister.Rcx] = handle;
        Assert.True(LibcStdioExports.Fwrite(ctx) < 0);
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void IsprintClassifiesAllCByteValuesAndEof()
    {
        var ctx = new CpuContext(new FakeCpuMemory(Address, 32), Generation.Gen5);
        for (var value = -1; value <= 255; value++)
        {
            ctx[CpuRegister.Rdi] = unchecked((ulong)(long)value);
            Assert.Equal(0, KernelMemoryCompatExports.Isprint(ctx));
            Assert.Equal(value is >= 32 and <= 126 ? 1UL : 0UL, ctx[CpuRegister.Rax]);
        }
    }

    [Fact]
    public void FreadRejectsSizeOverflowAndZeroCountDoesNotRequireStream()
    {
        var ctx = new CpuContext(new FakeCpuMemory(Address, 32), Generation.Gen5);
        ctx[CpuRegister.Rdi] = Address;
        ctx[CpuRegister.Rsi] = ulong.MaxValue;
        ctx[CpuRegister.Rdx] = 2;
        ctx[CpuRegister.Rcx] = 0;
        Assert.True(LibcStdioExports.Fread(ctx) < 0);
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
        ctx[CpuRegister.Rdx] = 0;
        Assert.Equal(0, LibcStdioExports.Fread(ctx));
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
    }

    private static CpuContext SetStream(CpuContext ctx, ulong file) { ctx[CpuRegister.Rdi] = file; return ctx; }

    // Fopen reserves a guest FILE object through the address-space interface.
    // Keep that mapping in managed storage; malloc remains the owned libc heap.
    private sealed class FileObjectMemory : ICpuMemory, IGuestAddressSpace
    {
        public FakeCpuMemory Storage { get; } = new(Address, 0x4000);
        public bool TryRead(ulong address, Span<byte> destination) => Storage.TryRead(address, destination);
        public bool TryWrite(ulong address, ReadOnlySpan<byte> source) => Storage.TryWrite(address, source);
        public bool TryAllocateGuestMemory(ulong size, ulong alignment, out ulong address) => Storage.TryAllocateGuestMemory(size, alignment, out address);
        public bool TryFreeGuestMemory(ulong address) => false;
        public ulong AllocateAt(ulong desiredAddress, ulong size, bool executable = true, bool allowAlternative = true) => 0;
        public bool TryBackFixedRange(ulong address, ulong size, bool executable) => false;
        public bool TryAllocateAtOrAbove(ulong desiredAddress, ulong size, bool executable, ulong alignment, out ulong actualAddress) => TryAllocateGuestMemory(size, alignment, out actualAddress);
        public bool TryProtect(ulong address, ulong size, GuestPageProtection protection) => false;
    }
}
