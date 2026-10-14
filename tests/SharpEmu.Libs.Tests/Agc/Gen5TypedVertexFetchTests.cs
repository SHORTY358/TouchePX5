// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5TypedVertexFetchTests
{
    private const ulong Base = 0x1_0000_0000;

    [Theory]
    [InlineData(0xEA6B2000u, 77u, 4u, 14u, 7u, 0u)] // actual Gears XYZW fetch
    [InlineData(0xEA012010u, 64u, 2u, 11u, 7u, 16u)] // actual Gears XY fetch
    [InlineData(0xE00C2000u, null, 1u, 4u, 4u, 0u)] // MUBUF keeps descriptor format
    public void InstructionFormatControlsTypedVertexViewOnly(uint word, uint? format,
        uint components, uint dataFormat, uint numberFormat, uint offset)
    {
        var (ctx, state) = Create(word);
        var control = Assert.IsType<Gen5BufferMemoryControl>(state.Program.Instructions[0].Control);
        Assert.Equal(format, control.TypedFormat);
        Assert.True(Gen5ShaderScalarEvaluator.TryEvaluate(ctx, state, out var evaluated,
            out var error, resolveVertexInputs: true), error);
        var binding = Assert.Single(evaluated.VertexInputs!);
        Assert.Equal(components, binding.ComponentCount);
        Assert.Equal(dataFormat, binding.DataFormat);
        Assert.Equal(numberFormat, binding.NumberFormat);
        Assert.Equal(offset, binding.OffsetBytes);
        Assert.Equal(24u, binding.Stride);
        Assert.Equal(Base + 0x800, binding.BaseAddress);
        Assert.Equal(0xFACu, binding.DestinationSelect);
    }

    [Fact]
    public void ReservedTypedFormatFailsInsteadOfFallingBackToDescriptor()
    {
        var (ctx, state) = Create((0xEA6B2000u & ~(0x7Fu << 19)) | (30u << 19));
        Assert.False(Gen5ShaderScalarEvaluator.TryEvaluate(ctx, state, out _,
            out var error, resolveVertexInputs: true));
        Assert.Contains("vertex-input-binding-failed", error);
    }

    [Fact]
    public void ComputeStorageFetchRejectsReservedMtbufFormat()
    {
        var (_, state) = Create((0xEA6B2000u & ~(0x7Fu << 19)) | (30u << 19));
        var binding = new Gen5GlobalMemoryBinding(36, Base + 0x800, [0], new byte[48], 48, false);
        var scalars = new uint[256];
        Array.Copy(state.UserData.ToArray(), 0, scalars, 36, 4);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state,
            new(scalars, scalars, [], [binding]), 1, 1, 1, out _, out var error));
        Assert.Contains("unsupported typed buffer format 30", error);
    }

    private static (CpuContext, Gen5ShaderState) Create(uint word)
    {
        var memory = new FakeCpuMemory(Base, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        Gen5ShaderAtomicDecodeTests.WriteProgram(memory, Base, [word, 0x80090105u]);
        Assert.True(Gen5ShaderTranslator.TryCreateState(ctx, Base, 0,
            new Dictionary<uint, uint> { [Gen5ShaderAtomicDecodeTests.ComputePgmRsrc2Register] = 0 },
            Gen5ShaderAtomicDecodeTests.ComputeUserDataRegister, out var decoded, out var error), error);
        // Descriptor intentionally says single-component UINT, unlike the
        // instruction's float formats. It retains Gears' real stride/dst_sel.
        return (ctx, new Gen5ShaderState(decoded.Program,
            [unchecked((uint)(Base + 0x800)), (uint)(Base >> 32) | (24u << 16), 2, 0x11014FAC],
            null, UserDataScalarRegisterBase: 36));
    }
}
