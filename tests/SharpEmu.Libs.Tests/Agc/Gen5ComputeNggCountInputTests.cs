// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5ComputeNggCountInputTests
{
    [Fact]
    public void PrimitiveOutputCannotDisagreeWithInjectedLiveVertexCount()
    {
        var program = new Gen5ShaderProgram(0, [new(0, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)]);
        var state = new Gen5ShaderState(program, [], null);
        var binding = new Gen5GlobalMemoryBinding(0, 0x10000, [], new byte[20], 20, false) { Writable = true };
        var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], [binding]);
        Assert.False(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out var error, triangleOutput: new(0, 0, 1, 4), nggCountInputs: new(33, 13, 32, 64, 17)));
        Assert.Contains("triangle live vertex count", error);
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1,
            out _, out error, triangleOutput: new(0, 0, 1, 33), nggCountInputs: new(33, 13, 32, 64, 17)), error);
    }

    [Theory]
    [InlineData(64u, 1u, 1u, 32u, true)]
    [InlineData(32u, 1u, 1u, 32u, false)]
    [InlineData(64u, 2u, 1u, 32u, false)]
    [InlineData(64u, 1u, 2u, 32u, false)]
    [InlineData(64u, 1u, 1u, 64u, false)]
    public void CountInputsRequireMatchingThreadExtentAndWave(uint x, uint y, uint z, uint wave, bool valid)
    {
        var program = new Gen5ShaderProgram(0, [new(0, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)]);
        var state = new Gen5ShaderState(program, [], null);
        var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], []);
        var result = Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, x, y, z,
            out _, out var error, waveLaneCount: wave, nggCountInputs: new(33, 13, 32, 64, 17));
        Assert.Equal(valid, result);
        if (!valid) Assert.Contains("NGG count inputs require matching", error);
    }
}
