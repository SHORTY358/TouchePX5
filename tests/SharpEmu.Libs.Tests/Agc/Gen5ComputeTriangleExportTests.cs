// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using System.Buffers.Binary;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5ComputeTriangleExportTests
{
    [Fact]
    public void TriangleTransportCanBeUsedWithoutPositionOutput()
    {
        var (state, evaluation) = Inputs();
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var missing));
        Assert.Contains("requires explicit output transport", missing);
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out var shader, out var error, triangleOutput: new(0, 0, 3, 4)), error);
        var checks = 0;
        for (var offset = 20; offset < shader.Spirv.Length;)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(shader.Spirv.AsSpan(offset));
            var count = (int)(word >> 16);
            Assert.True(count > 0 && offset + count * 4 <= shader.Spirv.Length);
            if ((ushort)word == 68) checks++; // OpArrayLength on each output word
            offset += count * 4;
        }
        Assert.Equal(5, checks);
    }

    [Theory]
    [InlineData(-1, 0u, 3u, 4u)]
    [InlineData(1, 0u, 3u, 4u)]
    [InlineData(0, 0u, 0u, 4u)]
    [InlineData(0, 0u, 3u, 0u)]
    [InlineData(0, 0u, 3u, 513u)]
    [InlineData(0, uint.MaxValue, 3u, 4u)]
    [InlineData(0, 0u, uint.MaxValue, 4u)]
    [InlineData(0, 25u, 3u, 4u)] // 40 words exceed the retained 39-word binding
    public void InvalidTriangleLayoutIsRejected(int binding, uint offset, uint capacity, uint count)
    {
        var (state, evaluation) = Inputs();
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, triangleOutput: new(binding, offset, capacity, count)));
        Assert.Contains("invalid compute triangle output", error);
    }

    [Fact]
    public void PositionAndPrimitiveRecordsCannotOverlap()
    {
        var (state, evaluation) = Inputs();
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var overlap, exportOutput: new(0, 3, 1), triangleOutput: new(0, 23, 3, 4)));
        Assert.Contains("overlap", overlap);
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, exportOutput: new(0, 3, 1), triangleOutput: new(0, 24, 3, 4)), error);
    }

    [Theory]
    [InlineData(3u, false)]
    [InlineData(1u, true)]
    public void UnsupportedPrimitiveEncodingFailsLoudly(uint mask, bool compressed)
    {
        var (state, evaluation) = Inputs(mask, compressed);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, triangleOutput: new(0, 0, 3, 4)));
        Assert.Contains("source0 only", error);
    }

    [Fact]
    public void ReadOnlyAndScalarAliasesAreRejected()
    {
        var (state, evaluation) = Inputs();
        evaluation.GlobalMemoryBindings[0].Writable = false;
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, triangleOutput: new(0, 0, 3, 4)));
        evaluation.GlobalMemoryBindings[0].Writable = true;
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, initialScalarBufferIndex: 0, triangleOutput: new(0, 0, 3, 4)));
    }

    [Theory]
    [InlineData(0u, 1u, 1u)]
    [InlineData(64u, 2u, 1u)]
    [InlineData(64u, 1u, 2u)]
    public void TriangleDispatchRequiresSupportedOneDimensionalLocalLayout(uint x, uint y, uint z)
    {
        var (state, evaluation) = Inputs();
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, x, y, z,
            out _, out _, triangleOutput: new(0, 0, 3, 4)));
    }

    private static (Gen5ShaderState, Gen5ShaderEvaluation) Inputs(uint mask = 1, bool compressed = false)
    {
        var program = new Gen5ShaderProgram(0, [
            new(0, Gen5ShaderEncoding.Exp, "Exp", [],
                [new(Gen5OperandKind.LiteralConstant, 0x00300400), Gen5Operand.Vector(1), Gen5Operand.Vector(2), Gen5Operand.Vector(3)], [],
                new Gen5ExportControl(20, mask, compressed, true, false)),
            new(8, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)
        ]);
        var output = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[156], 156, false) { Writable = true };
        return (new(program, [], null), new(new uint[256], new uint[256], [], [output]));
    }
}
