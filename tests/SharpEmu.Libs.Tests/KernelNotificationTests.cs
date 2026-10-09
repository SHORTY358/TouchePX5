// Copyright (C) 2026 Touché PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests;

public sealed class KernelNotificationTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT)]
    public void TextRequestRequiresTerminator(bool unterminated, int expected)
    {
        const ulong address = 0x1_0000_0000;
        var memory = new FakeCpuMemory(address, 4096);
        var request = new byte[3120];
        if (unterminated) request.AsSpan(45).Fill(65);
        else System.Text.Encoding.UTF8.GetBytes("cube completed").CopyTo(request, 45);
        Assert.True(memory.TryWrite(address, request));
        var ctx = new CpuContext(memory, Generation.Gen5);
        ctx[CpuRegister.Rsi] = address;
        ctx[CpuRegister.Rdx] = 3120;
        Assert.Equal(expected, KernelNotificationExports.SendNotificationRequest(ctx));
        Assert.Equal(unchecked((ulong)(long)expected), ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void UnmappedRequestReturnsMemoryFault()
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 4096), Generation.Gen5);
        ctx[CpuRegister.Rsi] = 0x2_0000_0000;
        ctx[CpuRegister.Rdx] = 3120;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT,
            KernelNotificationExports.SendNotificationRequest(ctx));
    }
}
