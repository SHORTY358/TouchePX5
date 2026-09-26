// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using Xunit;
using System.Buffers.Binary;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcIndirectDrawSizeTests
{
    private const ulong BaseAddress = 0x1_0000_0000;

    [Fact]
    public void DcbDrawIndexIndirectMultiGetSize_ReturnsTenDwords()
    {
        var ctx = new CpuContext(new FakeCpuMemory(BaseAddress, 0x1000), Generation.Gen5);

        var result = AgcExports.DcbDrawIndexIndirectMultiGetSize(ctx);

        Assert.Equal(40, result);
        Assert.Equal(40UL, ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void SubmittedIndirectDraw_PreservesDecodedInstanceCount()
    {
        const ulong commandBuffer = BaseAddress + 0x100;
        const ulong commands = BaseAddress + 0x400;
        const ulong indirectArguments = BaseAddress + 0x800;
        const ulong submitPacket = BaseAddress + 0x900;
        var memory = new FakeCpuMemory(BaseAddress, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);

        WriteUInt64(memory, commandBuffer + 0x10, commands);
        WriteUInt64(memory, commandBuffer + 0x18, commands + 0x100);

        ctx[CpuRegister.Rdi] = commandBuffer;
        ctx[CpuRegister.Rsi] = 0;
        ctx[CpuRegister.Rdx] = indirectArguments;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_OK, AgcExports.DcbSetBaseIndirectArgs(ctx));

        ctx[CpuRegister.Rdi] = commandBuffer;
        ctx[CpuRegister.Rsi] = 0;
        ctx[CpuRegister.Rdx] = 0;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_OK, AgcExports.DcbDrawIndirect(ctx));

        WriteUInt32(memory, indirectArguments, 3);
        WriteUInt32(memory, indirectArguments + 4, 7);
        WriteUInt32(memory, indirectArguments + 8, 0);
        WriteUInt32(memory, indirectArguments + 12, 0);
        WriteUInt64(memory, submitPacket, commands);
        WriteUInt32(memory, submitPacket + 8, 9);

        ctx[CpuRegister.Rdi] = submitPacket;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_OK, AgcExports.DriverSubmitDcb(ctx));
        Assert.Equal(7u, AgcExports.GetSubmittedGraphicsInstanceCountForDiagnostics(ctx));
    }

    [Fact]
    public void MultiIndirectBuilderEmitsTenDwordsWithCountAddressBeforeStride()
    {
        const ulong buffer = BaseAddress + 0x100, commands = BaseAddress + 0x400;
        var memory = new FakeCpuMemory(BaseAddress, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        WriteUInt64(memory, buffer + 0x10, commands);
        WriteUInt64(memory, buffer + 0x18, commands + 40);
        WriteUInt32(memory, commands + 40, 0xDEADBEEF);
        ctx[CpuRegister.Rdi] = buffer;
        ctx[CpuRegister.Rsi] = 32;
        ctx[CpuRegister.Rdx] = 7;
        ctx[CpuRegister.R8] = 0x1234;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_OK, AgcExports.DcbDrawIndexIndirectMulti(ctx));
        var bytes = new byte[44];
        Assert.True(memory.TryRead(commands, bytes));
        uint[] expected = [0xC0083800, 32, 0, 0, 0, 7, 0, 0, 20, 0x1234, 0xDEADBEEF];
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleIndirectDrawCopiesArgumentValuesIntoSelectedShaderRegisters(bool indexed)
    {
        const ulong commands = BaseAddress + 0x400;
        const ulong arguments = BaseAddress + 0x800;
        const ulong submit = BaseAddress + 0x900;
        var memory = new FakeCpuMemory(BaseAddress, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        // SET_BASE followed by a single indirect draw; nonzero offset skips poison.
        uint[] packet = [0xC0021100, 1, unchecked((uint)arguments), (uint)(arguments >> 32),
            indexed ? 0xC0032500u : 0xC0032400u, 32, 0xCC, 0xCD, 2];
        for (var i = 0; i < packet.Length; i++) WriteUInt32(memory, commands + (ulong)i * 4, packet[i]);
        WriteUInt32(memory, arguments + 32, 3);
        WriteUInt32(memory, arguments + 36, 2);
        WriteUInt32(memory, arguments + 40, indexed ? 5u : 17u);
        WriteUInt32(memory, arguments + 44, indexed ? unchecked((uint)-13) : 29u);
        if (indexed) WriteUInt32(memory, arguments + 48, 29);
        WriteUInt64(memory, submit, commands);
        WriteUInt32(memory, submit + 8, (uint)packet.Length);
        ctx[CpuRegister.Rdi] = submit;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_OK, AgcExports.DriverSubmitDcb(ctx));
        Assert.Equal(indexed ? unchecked((uint)-13) : 17u,
            AgcExports.GetSubmittedGraphicsShRegisterForDiagnostics(ctx, 0xCC));
        Assert.Equal(29u, AgcExports.GetSubmittedGraphicsShRegisterForDiagnostics(ctx, 0xCD));
        Assert.Equal(2u, AgcExports.GetSubmittedGraphicsInstanceCountForDiagnostics(ctx));
    }

    [Theory]
    [InlineData(false, 2u, 0u, 29, 32u)]
    [InlineData(false, 0u, 0u, null, 32u)]
    [InlineData(true, 2u, 1u, 7, 32u)]
    [InlineData(true, 2u, 99u, 29, 32u)]
    [InlineData(true, 2u, 0u, null, 32u)]
    [InlineData(false, 2u, 0u, 7, 0u)]
    public void MultiIndirectDrawUsesHardwareStrideCountAndRegisterFields(
        bool counted, uint maximumCount, uint memoryCount, int? expectedInstance, uint stride)
    {
        const ulong commands = BaseAddress + 0x400, arguments = BaseAddress + 0x800;
        const ulong submit = BaseAddress + 0x900, countAddress = BaseAddress + 0xA00;
        var memory = new FakeCpuMemory(BaseAddress, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        uint control = 0xCF | (1u << 28) | (1u << 31) | (counted ? 1u << 30 : 0);
        uint[] packet = [0xC0021100, 1, unchecked((uint)arguments), (uint)(arguments >> 32),
            0xC0083800, 32, 0x00CE00CC, 0xCD, control, maximumCount,
            unchecked((uint)countAddress), (uint)(countAddress >> 32), stride, 0];
        for (var i = 0; i < packet.Length; i++) WriteUInt32(memory, commands + (ulong)i * 4, packet[i]);
        uint[] first = [3, 3, 4, 11, 7], second = [3, 5, 6, 13, 29];
        for (var i = 0; i < first.Length; i++)
        {
            WriteUInt32(memory, arguments + 32 + (ulong)i * 4, first[i]);
            WriteUInt32(memory, arguments + 64 + (ulong)i * 4, second[i]);
        }
        WriteUInt32(memory, countAddress, memoryCount);
        WriteUInt64(memory, submit, commands);
        WriteUInt32(memory, submit + 8, (uint)packet.Length);
        ctx[CpuRegister.Rdi] = submit;
        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_OK, AgcExports.DriverSubmitDcb(ctx));
        Assert.Equal(expectedInstance is null ? (uint?)null : (uint)expectedInstance.Value,
            AgcExports.GetSubmittedGraphicsShRegisterForDiagnostics(ctx, 0xCD));
        if (expectedInstance is not null)
        {
            var last = expectedInstance == 29;
            Assert.Equal(last ? 13u : 11u, AgcExports.GetSubmittedGraphicsShRegisterForDiagnostics(ctx, 0xCC));
            Assert.Equal(last ? 6u : 4u, AgcExports.GetSubmittedGraphicsShRegisterForDiagnostics(ctx, 0xCE));
            var executed = counted ? Math.Min(maximumCount, memoryCount) : maximumCount;
            Assert.Equal(executed - 1, AgcExports.GetSubmittedGraphicsShRegisterForDiagnostics(ctx, 0xCF));
        }
    }

    private static void WriteUInt32(FakeCpuMemory memory, ulong address, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        Assert.True(memory.TryWrite(address, bytes));
    }

    private static void WriteUInt64(FakeCpuMemory memory, ulong address, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        Assert.True(memory.TryWrite(address, bytes));
    }
}
