// Copyright (C) 2026 Touché PX5 contributors
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.Core.Memory;
using SharpEmu.HLE;
using SharpEmu.Libs.Ngs2;
using Xunit;

namespace SharpEmu.Libs.Tests.Ngs2;

[CollectionDefinition("Ngs2State", DisableParallelization = true)]
public sealed class Ngs2StateCollection
{
    public const string Name = "Ngs2State";
}

[Collection(Ngs2StateCollection.Name)]
public sealed class Ngs2OutputValidationTests : IDisposable
{
    private const int InvalidOutAddress = unchecked((int)0x804A0053);
    private const ulong MemoryBase = 0x2_0000_0000;
    private const ulong SystemHandleAddress = MemoryBase + 0x100;
    private const ulong RackHandleAddress = MemoryBase + 0x108;
    private const ulong VoiceHandleAddress = MemoryBase + 0x110;

    private readonly PhysicalVirtualMemory _memory = new();
    private readonly CpuContext _ctx;

    public Ngs2OutputValidationTests()
    {
        Ngs2Exports.ResetForTests();
        Assert.Equal(MemoryBase, _memory.AllocateAt(
            MemoryBase,
            0x1000,
            executable: false,
            allowAlternative: false));
        _ctx = new CpuContext(_memory, Generation.Gen5);
    }

    [Fact]
    public void VoiceOutputs_RejectNullDestinations()
    {
        var (_, _, voiceHandle) = CreateVoice();

        _ctx[CpuRegister.Rdi] = voiceHandle;
        _ctx[CpuRegister.Rsi] = 0;
        _ctx[CpuRegister.Rdx] = 56;
        Assert.Equal(InvalidOutAddress, Ngs2Exports.Ngs2VoiceGetState(_ctx));

        _ctx[CpuRegister.Rdi] = voiceHandle;
        _ctx[CpuRegister.Rsi] = 0;
        Assert.Equal(InvalidOutAddress, Ngs2Exports.Ngs2VoiceGetStateFlags(_ctx));
    }

    [Fact]
    public void ExistingVoiceLookup_RejectsNullDestination()
    {
        var (_, rackHandle, _) = CreateVoice();

        _ctx[CpuRegister.Rdi] = rackHandle;
        _ctx[CpuRegister.Rsi] = 0;
        _ctx[CpuRegister.Rdx] = 0;

        Assert.Equal(InvalidOutAddress, Ngs2Exports.Ngs2RackGetVoiceHandle(_ctx));
    }

    public void Dispose()
    {
        Ngs2Exports.ResetForTests();
        _memory.Dispose();
    }

    private (ulong SystemHandle, ulong RackHandle, ulong VoiceHandle) CreateVoice()
    {
        _ctx[CpuRegister.Rdi] = 0;
        _ctx[CpuRegister.Rsi] = 0;
        _ctx[CpuRegister.Rdx] = SystemHandleAddress;
        Assert.Equal(0, Ngs2Exports.Ngs2SystemCreate(_ctx));
        var systemHandle = ReadUInt64(SystemHandleAddress);

        _ctx[CpuRegister.Rdi] = systemHandle;
        _ctx[CpuRegister.Rsi] = 0x3000;
        _ctx[CpuRegister.Rdx] = 0;
        _ctx[CpuRegister.R8] = RackHandleAddress;
        Assert.Equal(0, Ngs2Exports.Ngs2RackCreate(_ctx));
        var rackHandle = ReadUInt64(RackHandleAddress);

        _ctx[CpuRegister.Rdi] = rackHandle;
        _ctx[CpuRegister.Rsi] = 0;
        _ctx[CpuRegister.Rdx] = VoiceHandleAddress;
        Assert.Equal(0, Ngs2Exports.Ngs2RackGetVoiceHandle(_ctx));
        var voiceHandle = ReadUInt64(VoiceHandleAddress);

        return (systemHandle, rackHandle, voiceHandle);
    }

    private ulong ReadUInt64(ulong address)
    {
        Span<byte> value = stackalloc byte[sizeof(ulong)];
        Assert.True(_memory.TryRead(address, value));
        return BinaryPrimitives.ReadUInt64LittleEndian(value);
    }
}
