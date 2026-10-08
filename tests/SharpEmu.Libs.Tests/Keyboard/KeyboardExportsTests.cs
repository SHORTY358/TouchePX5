// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.HLE.Host;
using SharpEmu.Libs.Keyboard;
using Xunit;

namespace SharpEmu.Libs.Tests.Keyboard;

public sealed class KeyboardExportsTests
{
    private const ulong Address = 0x10000;
    private readonly FakeCpuMemory memory = new(Address, 128);
    private readonly Input input = new();

    [Fact]
    public void HeldKeysKeepSlotsAndReleasedSlotIsNotImmediatelyReplaced()
    {
        var ctx = Open();
        input.Keys.UnionWith([0x41, 0x44]);
        var first = Read(ctx);
        Assert.Equal(1, first[16]);
        Assert.Equal(4, Scan(first, 0));
        Assert.Equal(7, Scan(first, 1));
        input.Keys.Remove(0x41);
        input.Keys.Add(0x0D);
        var changed = Read(ctx);
        Assert.Equal(0, Scan(changed, 0));
        Assert.Equal(7, Scan(changed, 1));
        Assert.Equal(40, Scan(changed, 2));
        Assert.Equal(changed, Read(ctx));
        input.Focused = false;
        var released = Read(ctx);
        Assert.All(released.Skip(32).Take(32), value => Assert.Equal(0, value));
    }

    [Fact]
    public void ModifiersUseSeparateBitsAndCloseInvalidatesHandle()
    {
        var ctx = Open();
        input.Keys.UnionWith([0xA2, 0xA1]);
        Assert.Equal(0x21u, BinaryPrimitives.ReadUInt32LittleEndian(Read(ctx).AsSpan(28)));
        Assert.Equal(0, KeyboardExports.Close(ctx));
        ctx[CpuRegister.Rsi] = Address;
        Assert.True(KeyboardExports.ReadState(ctx, input) < 0);
    }

    [Fact]
    public void ShortOutputFailsWithoutConsumingKeyTransition()
    {
        var ctx = Open();
        input.Keys.Add(0x0D);
        ctx[CpuRegister.Rsi] = Address + 64;
        Assert.True(KeyboardExports.ReadState(ctx, input) < 0);
        Assert.Equal(40, Scan(Read(ctx), 0));
    }

    [Fact]
    public void OpenRequiresInitializationAndGuestMemoryOwnsHandles()
    {
        var ctx = new CpuContext(memory, Generation.Gen5);
        ctx[CpuRegister.Rdi] = 0x10000000;
        Assert.True(KeyboardExports.Open(ctx) < 0);
        ctx = Open();
        var other = new CpuContext(new FakeCpuMemory(Address, 128), Generation.Gen5);
        other[CpuRegister.Rdi] = ctx[CpuRegister.Rdi];
        other[CpuRegister.Rsi] = Address;
        Assert.True(KeyboardExports.ReadState(other, input) < 0);
    }

    private CpuContext Open()
    {
        var ctx = new CpuContext(memory, Generation.Gen5);
        Assert.Equal(0, KeyboardExports.Init(ctx));
        ctx[CpuRegister.Rdi] = 0x10000000;
        var handle = KeyboardExports.Open(ctx);
        Assert.True(handle > 0);
        ctx[CpuRegister.Rdi] = (ulong)handle;
        return ctx;
    }
    private byte[] Read(CpuContext ctx)
    {
        ctx[CpuRegister.Rsi] = Address;
        Assert.Equal(0, KeyboardExports.ReadState(ctx, input));
        var bytes = new byte[96];
        Assert.True(memory.TryRead(Address, bytes));
        return bytes;
    }
    private static ushort Scan(byte[] bytes, int slot) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32 + slot * 2));
    private sealed class Input : IHostInput
    {
        public bool Focused = true;
        public HashSet<int> Keys = [];
        public bool IsKeyDown(int key) => Keys.Contains(key);
        public bool IsHostWindowFocused() => Focused;
        public void EnsureStarted() { }
        public int GetGamepadStates(Span<HostGamepadState> destination) => 0;
        public string? DescribeConnectedGamepad() => null;
        public void SetRumble(byte largeMotor, byte smallMotor) { }
        public void SetTriggerRumble(byte? leftTrigger, byte? rightTrigger) { }
        public void SetLightbar(byte red, byte green, byte blue) { }
        public void ResetLightbar() { }
    }
}
