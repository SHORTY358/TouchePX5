// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Gpu;
using SharpEmu.Libs.Kernel;
using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

[Collection("VideoOut flip session")]
public sealed class VideoOutFlipLifecycleTests
{
    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public async Task ExperimentalPayloadPolicyIsCapturedPerGen5PortOnly(Generation generation)
    {
        const string variable = "TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "1");
            using var port = new Port(generation);
            Environment.SetEnvironmentVariable(variable, previous);
            port.Begin(100).Presented();
            port.Begin(101).Presented();
            await DrainNotifications();
            Assert.True(port.TakeEvent(out var first));
            Assert.Equal(generation == Generation.Gen5 ? 100UL : 101UL, first >> 16);
            if (generation == Generation.Gen5)
            {
                Assert.True(port.TakeEvent(out var second));
                Assert.Equal(101UL, second >> 16);
            }
            Assert.False(port.TakeEvent(out _));
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }

    [Fact]
    public async Task AcceptedFlipRemainsPendingUntilPresented_ThenSignalsExactlyOnce()
    {
        using var port = new Port();
        var ticket = port.Begin(123);
        Assert.Equal(123L, ticket.FlipArg);
        Assert.Equal((0UL, 1, -1L), port.Status());
        Assert.Equal(1, port.Pending());
        Assert.False(port.TakeEvent(out _));
        ticket.Presented();
        ticket.Cancel();
        ticket.Presented();
        await DrainNotifications();
        Assert.Equal((1UL, 0, 123L), port.Status());
        Assert.Equal(0, port.Pending());
        Assert.True(port.TakeEvent(out var data));
        Assert.Equal(123UL, data >> 16);
        Assert.False(port.TakeEvent(out _));
    }

    [Fact]
    public async Task CancellationDoesNotPublishAFrameOrEvent_AndReleasesCapacity()
    {
        using var port = new Port();
        var tickets = Enumerable.Range(0, 16).Select(i => port.Begin(i)).ToArray();
        Assert.Equal(unchecked((int)0x80290009),
            VideoOutExports.BeginFlip(port.Handle, -1, 99, out var rejected));
        Assert.Null(rejected);
        foreach (var ticket in tickets) { ticket.Cancel(); ticket.Presented(); }
        await DrainNotifications();
        Assert.Equal((0UL, 0, -1L), port.Status());
        Assert.False(port.TakeEvent(out _));
        port.Begin(77).Presented();
        await DrainNotifications();
        Assert.Equal((1UL, 0, 77L), port.Status());
    }

    [Fact]
    public async Task TwoPortsAndOutOfSubmissionOrderCompletionRemainIndependent()
    {
        using var first = new Port();
        using var second = new Port();
        var early = first.Begin(10);
        var late = first.Begin(20);
        var other = second.Begin(30);
        late.Presented();
        await DrainNotifications();
        Assert.Equal((1UL, 1, 20L), first.Status());
        Assert.Equal((0UL, 1, -1L), second.Status());
        early.Presented();
        other.Cancel();
        await DrainNotifications();
        Assert.Equal((2UL, 0, 10L), first.Status());
        Assert.Equal((0UL, 0, -1L), second.Status());
    }

    [Fact]
    public async Task CloseWithPendingFlipRejectsLateCompletionAndDoesNotAffectNewPort()
    {
        using var old = new Port();
        var ticket = old.Begin(45);
        old.Close();
        using var next = new Port();
        ticket.Presented();
        await DrainNotifications();
        Assert.True(ticket.IsCancelled);
        Assert.False(old.TakeEvent(out _));
        Assert.Equal((0UL, 0, -1L), next.Status());
    }

    [Fact]
    public async Task SessionCancellationClearsAllPendingPortsWithoutFakeEvents()
    {
        using var first = new Port();
        using var second = new Port();
        var a = first.Begin(1);
        var b = second.Begin(2);
        GuestPresentationCompletion.CancelSession();
        a.Presented(); b.Presented();
        await DrainNotifications();
        Assert.Equal((0UL, 0, -1L), first.Status());
        Assert.Equal((0UL, 0, -1L), second.Status());
        Assert.False(first.TakeEvent(out _));
        Assert.False(second.TakeEvent(out _));
        // The next session's tickets must not inherit terminal state.
        first.Begin(3).Presented();
        await DrainNotifications();
        Assert.Equal((1UL, 0, 3L), first.Status());
    }

    [Fact]
    public void InvalidSubmissionDoesNotConsumeQueueCapacity()
    {
        using var port = new Port();
        Assert.NotEqual(0, VideoOutExports.BeginFlip(port.Handle, 0, 1, out var unregistered));
        Assert.NotEqual(0, VideoOutExports.BeginFlip(port.Handle, -2, 1, out var invalid));
        Assert.NotEqual(0, VideoOutExports.BeginFlip(-555, -1, 1, out var missing));
        Assert.Null(unregistered); Assert.Null(invalid); Assert.Null(missing);
        Assert.Equal((0UL, 0, -1L), port.Status());
    }

    [Fact]
    public async Task ConcurrentPresentAndCancelChooseOneTerminalOutcome()
    {
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var ticket = new GuestPresentationCompletion((presented, timestamp) =>
        {
            Assert.True(timestamp > 0);
            Interlocked.Increment(ref calls);
            delivered.TrySetResult(presented);
        });
        await Task.WhenAll(Task.Run(ticket.Presented), Task.Run(ticket.Cancel));
        var presented = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await DrainNotifications();
        Assert.Equal(1, calls);
        Assert.Equal(!presented, ticket.IsCancelled);
    }

    [Fact]
    public async Task DeletingFlipRegistrationSuppressesLateNotificationsButPreservesOtherPortsAndReAdd()
    {
        using var first = new Port();
        using var second = new Port();
        var pending = first.Begin(11);
        Assert.NotEqual(0, first.RemoveFlipEvent(invalidQueue: true));
        Assert.Equal(0, first.RemoveFlipEvent());
        pending.Presented();
        second.Begin(22).Presented();
        await DrainNotifications();
        Assert.Equal((1UL, 0, 11L), first.Status());
        Assert.False(first.TakeEvent(out _));
        Assert.True(second.TakeEvent(out _));
        first.AddFlipEvent();
        first.Begin(33).Presented();
        await DrainNotifications();
        Assert.True(first.TakeEvent(out var data));
        Assert.Equal(33UL, data >> 16);
    }

    // Enqueue a barrier behind preceding notifications without sleeps or GPU startup.
    private static async Task DrainNotifications()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new GuestPresentationCompletion((_, _) => done.SetResult()).Cancel();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Port : IDisposable
    {
        private const ulong MemoryBase = 0x1_0000_0000;
        private readonly FakeCpuMemory _memory = new(MemoryBase, 0x1000);
        private readonly CpuContext _context;
        private readonly ulong _equeue;
        private bool _closed;
        public int Handle { get; }

        public Port(Generation generation = Generation.Gen5)
        {
            _context = new CpuContext(_memory, generation);
            Handle = VideoOutExports.VideoOutOpen(_context);
            Assert.True(Handle > 0);
            _context[CpuRegister.Rdi] = MemoryBase;
            Assert.Equal(0, KernelEventQueueCompatExports.KernelCreateEqueue(_context));
            Assert.True(_context.TryReadUInt64(MemoryBase, out _equeue));
            _context[CpuRegister.Rdi] = _equeue;
            _context[CpuRegister.Rsi] = (ulong)Handle;
            _context[CpuRegister.Rdx] = 0x1234;
            Assert.Equal(0, VideoOutExports.VideoOutAddFlipEvent(_context));
        }

        public GuestPresentationCompletion Begin(long arg)
        {
            Assert.Equal(0, VideoOutExports.BeginFlip(Handle, -1, arg, out var ticket));
            return Assert.IsType<GuestPresentationCompletion>(ticket);
        }

        public int RemoveFlipEvent(bool invalidQueue = false)
        {
            var manager = new ModuleManager();
            manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));
            Assert.True(manager.TryGetExport("-Ozn0F1AFRg", out var export));
            Assert.Equal("sceVideoOutDeleteFlipEvent", export.Name);
            _context[CpuRegister.Rdi] = invalidQueue ? ulong.MaxValue : _equeue;
            _context[CpuRegister.Rsi] = (ulong)Handle;
            return (int)manager.Dispatch("-Ozn0F1AFRg", _context);
        }

        public void AddFlipEvent()
        {
            _context[CpuRegister.Rdi] = _equeue;
            _context[CpuRegister.Rsi] = (ulong)Handle;
            Assert.Equal(0, VideoOutExports.VideoOutAddFlipEvent(_context));
        }

        public (ulong Count, int Pending, long Arg) Status()
        {
            _context[CpuRegister.Rdi] = (ulong)Handle;
            _context[CpuRegister.Rsi] = MemoryBase + 0x100;
            Assert.Equal(0, VideoOutExports.VideoOutGetFlipStatus(_context));
            var bytes = new byte[0x80];
            Assert.True(_memory.TryRead(MemoryBase + 0x100, bytes));
            return (BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x34)),
                BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(0x18)));
        }

        public int Pending()
        {
            _context[CpuRegister.Rdi] = (ulong)Handle;
            return VideoOutExports.VideoOutIsFlipPending(_context);
        }

        public bool TakeEvent(out ulong data)
        {
            var found = KernelEventQueueCompatExports.TryReservePendingEventForTest(_equeue, out var item);
            data = found ? item.Data : 0;
            return found;
        }

        public void Close()
        {
            if (_closed) return;
            _context[CpuRegister.Rdi] = (ulong)Handle;
            Assert.Equal(0, VideoOutExports.VideoOutClose(_context));
            _closed = true;
        }

        public void Dispose()
        {
            Close();
            _context[CpuRegister.Rdi] = _equeue;
            KernelEventQueueCompatExports.KernelDeleteEqueue(_context);
        }
    }
}

[CollectionDefinition("VideoOut flip session", DisableParallelization = true)]
public sealed class VideoOutFlipSessionCollection;
