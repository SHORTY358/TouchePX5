// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5ComputeNggSourceInputTests
{
    [Fact]
    public void SourceInputsRequireExplicitCountsButAllowReadOnlyStorage()
    {
        var (state, evaluation) = Inputs();
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var missing, nggSourceInputs: new(0, 0)));
        Assert.Contains("invalid NGG source input", missing);
        evaluation.GlobalMemoryBindings[0].Writable = false;
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, nggCountInputs: new(4, 1, 32, 64, 17), nggSourceInputs: new(0, 0)), error);
    }

    [Theory]
    [InlineData(-1, 0u)]
    [InlineData(1, 0u)]
    [InlineData(0, uint.MaxValue)]
    [InlineData(0, 1473u)] // 1473 + 576 exceeds the 2048-word retained storage
    public void MissingOrShortSourceRangeIsRejected(int binding, uint first)
    {
        var (state, evaluation) = Inputs();
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, nggCountInputs: new(4, 1, 32, 64, 17), nggSourceInputs: new(binding, first)));
        Assert.Contains("invalid NGG source input", error);
    }

    [Fact]
    public void SourceInputCannotAliasPositionOrPrimitiveWrites()
    {
        var (state, evaluation) = Inputs();
        var counts = new Gen5NggMergedGroup(4, 1, 32, 64, 17);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, exportOutput: new(0, 64, 2), nggCountInputs: counts, nggSourceInputs: new(0, 767)));
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, exportOutput: new(0, 64, 2), nggCountInputs: counts, nggSourceInputs: new(0, 768)), error);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, triangleOutput: new(0, 64, 1, 4), nggCountInputs: counts, nggSourceInputs: new(0, 0)));
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out error, triangleOutput: new(0, 64, 1, 4), nggCountInputs: counts, nggSourceInputs: new(0, 69)), error);
    }

    [Fact]
    public void ScalarInputAliasAndUnretainedDescriptorAreRejected()
    {
        var (state, evaluation) = Inputs();
        var counts = new Gen5NggMergedGroup(4, 1, 32, 64, 17);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, initialScalarBufferIndex: 0, nggCountInputs: counts, nggSourceInputs: new(0, 0)));
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, totalGlobalBufferCount: 0, nggCountInputs: counts, nggSourceInputs: new(0, 0)));
    }

    private static (Gen5ShaderState, Gen5ShaderEvaluation) Inputs()
    {
        var program = new Gen5ShaderProgram(0, [new(0, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)]);
        var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[8192], 8192, false) { Writable = true };
        return (new(program, [], null), new(new uint[256], new uint[256], [], [binding]));
    }
}
