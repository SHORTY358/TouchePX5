// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.HLE.Host;
using SharpEmu.Libs.VideoOut;
using Xunit;
using MouseApi = SharpEmu.Libs.Mouse.MouseExports;
using MouseState = SharpEmu.Libs.Pad.MouseExports;

namespace SharpEmu.Libs.Tests.Mouse;

public sealed class MouseInputTests
{
    [Fact]
    public void WindowActivationDoesNotCaptureUntilClientClickAndReleaseDoesNotRecapture()
    {
        var policy = new MouseCapturePolicy();
        policy.SetFocus(true);
        Assert.False(policy.Captured);
        Assert.False(policy.TryCapture(true, 40, -20, 1920, 1080)); // Title bar.
        Assert.False(policy.TryCapture(false, 100, 100, 1920, 1080));
        Assert.False(policy.Captured);
        Assert.True(policy.TryCapture(true, 100, 100, 1920, 1080));
        policy.Release(); // F8: still focused, still uncaptured.
        policy.SetFocus(true);
        Assert.False(policy.Captured);
        Assert.True(policy.TryCapture(true, 100, 100, 1920, 1080));
        policy.SetFocus(false); policy.SetFocus(true);
        Assert.False(policy.Captured);
    }

    [Fact]
    public void RelativeEventsAccumulateOnceBeyondScreenEdgesAndResetOnFocusChange()
    {
        var motion = new HostMouseMotion();
        motion.Attach(); motion.SetFocus(true);
        motion.Submit(100, 200); // Establish baseline after entering capture.
        motion.Submit(5100.5, -1800.25);
        motion.Submit(10100.75, -3800.5);
        Assert.True(motion.TryConsume(out var x, out var y));
        Assert.Equal(10000, x); Assert.Equal(-4000, y);
        Assert.True(motion.TryConsume(out x, out y));
        Assert.Equal(0, x); Assert.Equal(0, y);
        motion.Submit(10101.25, -3801.25);
        Assert.True(motion.TryConsume(out x, out y));
        Assert.Equal(1, x); Assert.Equal(-1, y);
        motion.SetFocus(false); motion.Submit(999999, 999999);
        Assert.False(motion.TryConsume(out x, out y));
        motion.SetFocus(true); motion.Submit(0, 0);
        Assert.True(motion.TryConsume(out x, out y));
        Assert.Equal(0, x); Assert.Equal(0, y);
        motion.Detach(); Assert.False(motion.TryConsume(out x, out y));
    }

    [Fact]
    public void ExportAliasesDeliverSignedMotionButtonsAndReservedZeros()
    {
        var memory = new FakeCpuMemory(0x1000, 128);
        var ctx = new CpuContext(memory, Generation.Gen5);
        Assert.Equal(0, MouseApi.MouseInit(ctx));
        ctx[CpuRegister.Rdi] = 0x10000000;
        Assert.Equal(0, MouseApi.MouseOpen(ctx));
        ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x1000; ctx[CpuRegister.Rdx] = 1;
        Assert.Equal(1, MouseState.MouseRead(ctx, new Input(true, -37, 19, 5)));
        var bytes = new byte[40]; Assert.True(memory.TryRead(0x1000, bytes));
        Assert.Equal(1, bytes[8]);
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
        Assert.Equal(-37, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(19, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20)));
        Assert.All(bytes[24..], value => Assert.Equal(0, value));
        Assert.Equal(1, MouseState.MouseRead(ctx, new Input(false, 999, 999, 7)));
        Assert.True(memory.TryRead(0x1000, bytes));
        Assert.All(bytes[8..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void HandlesDoNotLeakBetweenGuestsAndCloseRejectsLaterReads()
    {
        var first = new CpuContext(new FakeCpuMemory(0x1000, 128), Generation.Gen5);
        var second = new CpuContext(new FakeCpuMemory(0x1000, 128), Generation.Gen5);
        MouseApi.MouseInit(first); first[CpuRegister.Rdi] = 0x10000000;
        Assert.Equal(0, MouseApi.MouseOpen(first));
        second[CpuRegister.Rsi] = 0x1000; second[CpuRegister.Rdx] = 1;
        Assert.Equal(unchecked((int)0x80020003), MouseState.MouseRead(second, new Input(true, 1, 2, 1)));
        first[CpuRegister.Rdi] = 0;
        Assert.Equal(0, MouseState.MouseClose(first));
        first[CpuRegister.Rsi] = 0x1000; first[CpuRegister.Rdx] = 1;
        Assert.Equal(unchecked((int)0x80020003), MouseState.MouseRead(first, new Input(true, 1, 2, 1)));
    }

    private sealed class Input(bool active, int x, int y, uint buttons) : IHostInput
    {
        public bool TryReadMouse(out int dx, out int dy, out uint mask) { dx = x; dy = y; mask = buttons; return active; }
        public bool IsHostWindowFocused() => active;
        public bool IsKeyDown(int key) => false;
        public void EnsureStarted() { }
        public int GetGamepadStates(Span<HostGamepadState> destination) => 0;
        public string? DescribeConnectedGamepad() => null;
        public void SetRumble(byte largeMotor, byte smallMotor) { }
        public void SetTriggerRumble(byte? leftTrigger, byte? rightTrigger) { }
        public void SetLightbar(byte red, byte green, byte blue) { }
        public void ResetLightbar() { }
    }
}
