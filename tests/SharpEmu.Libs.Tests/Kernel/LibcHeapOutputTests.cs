// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

[Collection(KernelMemoryCompatStateCollection.Name)]
public sealed class LibcHeapOutputTests
{
    private const ulong GuestBase = 0x1_0000_0000;

    [Fact]
    public void Strdup_ReturnsIndependentFreeableTerminatedStorage()
    {
        var memory = new FakeCpuMemory(GuestBase, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        memory.WriteCString(GuestBase, "Wolf\ntext");
        ctx[CpuRegister.Rdi] = GuestBase;
        Assert.Equal(0, KernelMemoryCompatExports.Strdup(ctx));
        var heap = ctx[CpuRegister.Rax];
        Assert.NotEqual(0UL, heap);
        try
        {
            memory.WriteCString(GuestBase, "changed");
            var output = new byte[10];
            Assert.True(KernelMemoryCompatExports.TryReadGuestOrLibcHeap(ctx, heap, output));
            Assert.Equal(System.Text.Encoding.ASCII.GetBytes("Wolf\ntext\0"), output);
        }
        finally { ctx[CpuRegister.Rdi] = heap; KernelMemoryCompatExports.Free(ctx); }
        Assert.False(KernelMemoryCompatExports.TryReadGuestOrLibcHeap(ctx, heap, new byte[1]));
    }

    [Fact]
    public void Semaphore_UsesMallocStorageAcrossWaitPostAndDestroy()
    {
        var ctx = new CpuContext(new FakeCpuMemory(GuestBase, 0x1000), Generation.Gen5);
        ctx[CpuRegister.Rdi] = 4;
        Assert.Equal(0, KernelMemoryCompatExports.Malloc(ctx));
        var heap = ctx[CpuRegister.Rax];
        try
        {
            ctx[CpuRegister.Rdi] = heap;
            ctx[CpuRegister.Rsi] = 0;
            ctx[CpuRegister.Rdx] = 1;
            Assert.Equal(0, KernelSemaphoreCompatExports.PosixSemInit(ctx));
            ctx[CpuRegister.Rdi] = heap;
            Assert.Equal(0, KernelSemaphoreCompatExports.PosixSemTryWait(ctx));
            ctx[CpuRegister.Rdi] = heap;
            Assert.True(KernelSemaphoreCompatExports.PosixSemTryWait(ctx) < 0);
            ctx[CpuRegister.Rdi] = heap;
            Assert.Equal(0, KernelSemaphoreCompatExports.PosixSemPost(ctx));
            ctx[CpuRegister.Rdi] = heap;
            Assert.Equal(0, KernelSemaphoreCompatExports.PosixSemTryWait(ctx));
            ctx[CpuRegister.Rdi] = heap;
            Assert.Equal(0, KernelSemaphoreCompatExports.PosixSemDestroy(ctx));
            ctx[CpuRegister.Rdi] = heap;
            Assert.True(KernelSemaphoreCompatExports.PosixSemPost(ctx) < 0);
        }
        finally { ctx[CpuRegister.Rdi] = heap; KernelMemoryCompatExports.Free(ctx); }
    }

    [Fact]
    public void Snprintf_TruncatesAndTerminatesInsideMallocAllocation()
    {
        var memory = new FakeCpuMemory(GuestBase, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        memory.WriteCString(GuestBase, "abcdef");
        ctx[CpuRegister.Rdi] = 16;
        Assert.Equal(0, KernelMemoryCompatExports.Malloc(ctx));
        var heap = ctx[CpuRegister.Rax];
        Assert.NotEqual(0UL, heap);
        try
        {
            Marshal.WriteByte((nint)heap, 0xCC);
            Marshal.WriteByte((nint)(heap + 6), 0xCC);
            ctx[CpuRegister.Rdi] = heap + 1;
            ctx[CpuRegister.Rsi] = 5;
            ctx[CpuRegister.Rdx] = GuestBase;
            Assert.Equal(0, KernelMemoryCompatExports.Snprintf(ctx));
            Assert.Equal(6UL, ctx[CpuRegister.Rax]);
            var bytes = new byte[7];
            Marshal.Copy((nint)heap, bytes, 0, bytes.Length);
            Assert.Equal(new byte[] { 0xCC, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0, 0xCC }, bytes);
        }
        finally { ctx[CpuRegister.Rdi] = heap; KernelMemoryCompatExports.Free(ctx); }
    }

    [Fact]
    public void HeapOutput_RejectsOutOfBoundsFreedAndUnownedHostPointers()
    {
        var ctx = new CpuContext(new FakeCpuMemory(GuestBase, 0x1000), Generation.Gen5);
        ctx[CpuRegister.Rdi] = 16;
        Assert.Equal(0, KernelMemoryCompatExports.Malloc(ctx));
        var heap = ctx[CpuRegister.Rax];
        try
        {
            Assert.True(KernelMemoryCompatExports.TryWriteGuestOrLibcHeapUInt64(ctx, heap + 8, 123));
            Assert.True(KernelMemoryCompatExports.TryReadGuestOrLibcHeapUInt64(ctx, heap + 8, out var value));
            Assert.Equal(123UL, value);
            Assert.False(KernelMemoryCompatExports.TryWriteGuestOrLibcHeapUInt64(ctx, heap + 9, 456));
            Assert.False(KernelMemoryCompatExports.TryReadGuestOrLibcHeapUInt64(ctx, heap + 9, out _));
        }
        finally { ctx[CpuRegister.Rdi] = heap; KernelMemoryCompatExports.Free(ctx); }
        Assert.False(KernelMemoryCompatExports.TryWriteGuestOrLibcHeapUInt64(ctx, heap, 123));
        var unowned = Marshal.AllocHGlobal(16);
        try { Assert.False(KernelMemoryCompatExports.TryWriteGuestOrLibcHeapUInt64(ctx, (ulong)unowned, 123)); }
        finally { Marshal.FreeHGlobal(unowned); }
    }

    [Fact]
    public void EventQueue_CanReturnHandleIntoMallocStorage()
    {
        var ctx = new CpuContext(new FakeCpuMemory(GuestBase, 0x1000), Generation.Gen5);
        ctx[CpuRegister.Rdi] = 8;
        Assert.Equal(0, KernelMemoryCompatExports.Malloc(ctx));
        var heap = ctx[CpuRegister.Rax];
        ulong handle = 0;
        try
        {
            ctx[CpuRegister.Rdi] = heap;
            Assert.Equal(0, KernelEventQueueCompatExports.KernelCreateEqueue(ctx));
            Assert.True(KernelMemoryCompatExports.TryReadGuestOrLibcHeapUInt64(ctx, heap, out handle));
            Assert.True(KernelEventQueueCompatExports.IsValidEqueue(handle));
        }
        finally
        {
            if (handle != 0) { ctx[CpuRegister.Rdi] = handle; KernelEventQueueCompatExports.KernelDeleteEqueue(ctx); }
            ctx[CpuRegister.Rdi] = heap; KernelMemoryCompatExports.Free(ctx);
        }
    }

    [Fact]
    public void EmptyGuestEnvironment_DoesNotExposeHostPath()
    {
        var memory = new FakeCpuMemory(GuestBase, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        memory.WriteCString(GuestBase, "PATH");
        ctx[CpuRegister.Rdi] = GuestBase;
        ctx[CpuRegister.Rax] = 123;
        Assert.Equal(0, KernelRuntimeCompatExports.Getenv(ctx));
        Assert.Equal(0UL, ctx[CpuRegister.Rax]);
    }
}
