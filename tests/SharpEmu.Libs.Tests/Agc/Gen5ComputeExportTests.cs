// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5ComputeExportTests
{
    [Fact]
    public void PositionAndParameterExportsRequireExplicitBoundedTransport()
    {
        var (state, evaluation) = Inputs(12, 32);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var missing));
        Assert.Contains("requires explicit output transport", missing);
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out var shader, out var error, exportOutput: new(0, 3, 1)), error);
        // Every enabled output channel checks the actual storage array length,
        // in addition to the logical record-capacity and EXEC guards.
        var arrayChecks = 0;
        for (var offset = 20; offset < shader.Spirv.Length;)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(shader.Spirv.AsSpan(offset));
            var count = (int)(word >> 16);
            Assert.True(count > 0 && offset + count * 4 <= shader.Spirv.Length);
            if ((ushort)word == 68) arrayChecks++; // OpArrayLength
            offset += count * 4;
        }
        Assert.Equal(8, arrayChecks);
    }

    [Theory]
    [InlineData(-1, 3u, 1u)]
    [InlineData(1, 3u, 1u)]
    [InlineData(0, 0u, 1u)]
    [InlineData(0, 4u, 1u)] // four records exceed the retained 96-byte output
    [InlineData(0, 3u, 33u)]
    public void InvalidOutputCannotProduceShader(int binding, uint capacity, uint parameters)
    {
        var (state, evaluation) = Inputs(12);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, exportOutput: new(binding, capacity, parameters)));
        Assert.Contains("invalid compute export output", error);
    }

    [Fact]
    public void ReadOnlyOutputIsRejected()
    {
        var (state, evaluation) = Inputs(12);
        evaluation.GlobalMemoryBindings[0].Writable = false;
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, exportOutput: new(0, 3, 1)));
        Assert.Contains("invalid compute export output", error);
    }

    [Fact]
    public void PrimitiveExportIsNotSilentlyDiscarded()
    {
        var (state, evaluation) = Inputs(20);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, exportOutput: new(0, 3, 1)));
        Assert.Contains("unsupported compute export target 20", error);
    }

    private static (Gen5ShaderState, Gen5ShaderEvaluation) Inputs(params uint[] targets)
    {
        var instructions = targets.Select((target, index) => new Gen5ShaderInstruction(
            (uint)index * 8, Gen5ShaderEncoding.Exp, "Exp", [],
            [Gen5Operand.Vector(0), Gen5Operand.Vector(1), Gen5Operand.Vector(2), Gen5Operand.Vector(3)], [],
            new Gen5ExportControl(target, 15, false, false, false))).ToList();
        instructions.Add(new((uint)targets.Length * 8, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null));
        var output = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[96], 96, false) { Writable = true };
        return (new Gen5ShaderState(new Gen5ShaderProgram(0, instructions), [], null),
            new Gen5ShaderEvaluation(new uint[256], new uint[256], [], [output]));
    }
}
