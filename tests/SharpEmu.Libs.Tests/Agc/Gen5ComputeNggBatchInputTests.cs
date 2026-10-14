// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5ComputeNggBatchInputTests
{
    [Fact]
    public void ExplicitBatchUsesDescriptorCountsForPrimitiveValidity()
    {
        Assert.True(Compile(new(0, 1664, 0, 1672, 2, 64, 32), out var error), error);
        Assert.False(Compile(new(0, 1664, 0, 1672, 2, 64, 32), out _, liveCount: 4));
        Assert.False(Compile(null, out _, liveCount: 0));
    }

    [Theory]
    [InlineData(1, 1664u, 0, 1672u, 2u, 64u, 32u)]
    [InlineData(0, 1664u, 1, 1672u, 2u, 64u, 32u)]
    [InlineData(0, uint.MaxValue, 0, 1672u, 2u, 64u, 32u)]
    [InlineData(0, 1664u, 0, uint.MaxValue, 2u, 64u, 32u)]
    [InlineData(0, 1664u, 0, 1671u, 2u, 64u, 32u)] // descriptor/source overlap
    [InlineData(0, 1000u, 0, 1672u, 2u, 64u, 32u)] // descriptor/position output overlap
    [InlineData(0, 1200u, 0, 1672u, 2u, 64u, 32u)] // descriptor/primitive output overlap
    [InlineData(0, 1664u, 0, 1672u, 0u, 64u, 32u)]
    [InlineData(0, 1664u, 0, 1672u, 10000u, 64u, 32u)]
    [InlineData(0, 1664u, 0, 1672u, 2u, 32u, 32u)] // local extent mismatch
    [InlineData(0, 1664u, 0, 1672u, 2u, 64u, 64u)] // guest wave mismatch
    public void InvalidBatchTransportRejects(int db, uint df, int sb, uint sf, uint groups, uint threads, uint wave)
    {
        Assert.False(Compile(new(db, df, sb, sf, groups, threads, wave), out var error));
        Assert.Contains("invalid NGG batch", error);
    }

    [Fact]
    public void BatchCannotMixUniformInputModesOrUndersizedOutputReservation()
    {
        var (state, evaluation) = Inputs();
        var batch = new Gen5ComputeNggBatchInput(0, 1664, 0, 1672, 2, 64, 32);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, exportOutput: new(0, 127, 1), triangleOutput: new(0, 1024, 128, 0), nggBatchInputs: batch));
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, nggCountInputs: new(4, 1, 32, 64, 17), nggBatchInputs: batch));
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out _, initialScalarBufferIndex: 0, nggBatchInputs: batch));
    }

    private static bool Compile(Gen5ComputeNggBatchInput? batch, out string error, uint liveCount = 0)
    {
        var (state, evaluation) = Inputs();
        return Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out error, exportOutput: new(0, 128, 1), triangleOutput: new(0, 1024, 128, liveCount), nggBatchInputs: batch);
    }
    private static (Gen5ShaderState, Gen5ShaderEvaluation) Inputs()
    {
        var program = new Gen5ShaderProgram(0, [new(0, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)]);
        var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[11360], 11360, false) { Writable = true };
        return (new(program, [], null), new(new uint[256], new uint[256], [], [binding]));
    }
}
