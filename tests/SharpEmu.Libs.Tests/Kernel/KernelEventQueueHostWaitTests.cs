// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

public sealed class KernelEventQueueHostWaitTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NullTimeoutWithoutGuestSchedulerWaitsForEventOrDeletion(bool delete, bool display)
    {
        const ulong address = 0x100000000;
        var memory = new FakeCpuMemory(address, 0x1000);
        var control = new CpuContext(memory, Generation.Gen5);
        control[CpuRegister.Rdi] = address;
        Assert.Equal(0, KernelEventQueueCompatExports.KernelCreateEqueue(control));
        Assert.True(control.TryReadUInt64(address, out var handle));
        var wait = new CpuContext(memory, Generation.Gen5);
        wait[CpuRegister.Rdi] = handle;
        wait[CpuRegister.Rsi] = address + 0x100;
        wait[CpuRegister.Rdx] = 1;
        wait[CpuRegister.Rcx] = address + 0x200;
        wait[CpuRegister.R8] = 0;
        using var entered = new ManualResetEventSlim();
        var task = Task.Factory.StartNew(() =>
        {
            entered.Set();
            return KernelEventQueueCompatExports.KernelWaitEqueue(wait);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.NotSame(task, await Task.WhenAny(task, Task.Delay(50)));
            control[CpuRegister.Rdi] = handle;
            if (delete)
                Assert.Equal(0, KernelEventQueueCompatExports.KernelDeleteEqueue(control));
            else if (display)
                Assert.True(KernelEventQueueCompatExports.TriggerDisplayEvent(handle, 0x77, -14, 0x12340000, 0));
            else
                Assert.True(KernelEventQueueCompatExports.EnqueueEvent(handle, new(
                    Ident: 0x77, Filter: KernelEventQueueCompatExports.KernelEventFilterUser,
                    Flags: KernelEventQueueCompatExports.KernelEventFlagClear,
                    Fflags: 0, Data: 0x1234, UserData: 0)));
            Assert.Equal(delete ? (int)OrbisGen2Result.ORBIS_GEN2_ERROR_DELETED : 0,
                await task.WaitAsync(TimeSpan.FromSeconds(2)));
            if (!delete)
            {
                var count = new byte[4];
                Assert.True(memory.TryRead(address + 0x200, count));
                Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(count));
                Assert.True(wait.TryReadUInt64(address + 0x110, out var data));
                Assert.Equal(0x1234UL, display ? data >> 16 : data);
            }
        }
        finally
        {
            control[CpuRegister.Rdi] = handle;
            KernelEventQueueCompatExports.KernelDeleteEqueue(control);
            await task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
