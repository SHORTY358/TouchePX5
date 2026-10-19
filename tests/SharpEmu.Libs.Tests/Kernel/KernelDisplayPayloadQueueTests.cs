// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

public sealed class KernelDisplayPayloadQueueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlowConsumerPreservesTokensOnlyWhenRequested(bool preserve)
    {
        using var queue = new Queue();
        Assert.True(queue.Trigger(1, preserve));
        Assert.True(queue.Trigger(2, preserve));
        Assert.True(KernelEventQueueCompatExports.TryReservePendingEventForTest(queue.Handle, out var first));
        Assert.Equal(preserve ? 1UL : 2UL, first.Data >> 16);
        Assert.Equal(preserve ? 1UL : 2UL, (first.Data >> 12) & 15);
        if (preserve)
        {
            Assert.True(KernelEventQueueCompatExports.TryReservePendingEventForTest(queue.Handle, out var second));
            Assert.Equal(2UL, second.Data >> 16);
        }
        Assert.False(KernelEventQueueCompatExports.TryReservePendingEventForTest(queue.Handle, out _));
    }

    [Fact]
    public void CapacityRejectsNewPayloadWithoutReplacingAnyUnreadToken()
    {
        using var queue = new Queue();
        for (ulong i = 1; i <= 16; i++) Assert.True(queue.Trigger(i, true));
        Assert.False(queue.Trigger(17, true));
        for (ulong i = 1; i <= 16; i++)
        {
            Assert.True(KernelEventQueueCompatExports.TryReservePendingEventForTest(queue.Handle, out var item));
            Assert.Equal(i, item.Data >> 16);
        }
        Assert.False(KernelEventQueueCompatExports.TryReservePendingEventForTest(queue.Handle, out _));
        Assert.True(queue.Trigger(18, true));
    }

    [Fact]
    public void DeletedQueueRejectsPayloadAndDiscardsItsBacklog()
    {
        var queue = new Queue();
        Assert.True(queue.Trigger(1, true));
        Assert.True(queue.Trigger(2, true));
        queue.Dispose();
        Assert.False(queue.Trigger(3, true));
        Assert.False(KernelEventQueueCompatExports.TryReservePendingEventForTest(queue.Handle, out _));
    }

    private sealed class Queue : IDisposable
    {
        private readonly CpuContext _context = new(new FakeCpuMemory(0x100000000, 0x1000), Generation.Gen5);
        public ulong Handle { get; }
        public Queue()
        {
            _context[CpuRegister.Rdi] = 0x100000000;
            Assert.Equal(0, KernelEventQueueCompatExports.KernelCreateEqueue(_context));
            Assert.True(_context.TryReadUInt64(0x100000000, out var handle));
            Handle = handle;
        }
        public bool Trigger(ulong token, bool preserve) =>
            KernelEventQueueCompatExports.TriggerDisplayEvent(Handle, 6, -13, token << 16, 0, preserve);
        public void Dispose()
        {
            _context[CpuRegister.Rdi] = Handle;
            Assert.Equal(0, KernelEventQueueCompatExports.KernelDeleteEqueue(_context));
        }
    }
}
