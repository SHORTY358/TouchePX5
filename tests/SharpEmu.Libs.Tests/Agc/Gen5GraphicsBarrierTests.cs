// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5GraphicsBarrierTests
{
    [Fact]
    public void VertexWorkgroupBarrierFailsBeforeProducingInvalidShader()
    {
        var (state, evaluation) = Inputs();
        Assert.False(Gen5SpirvTranslator.TryCompileVertexShader(state, evaluation, out _, out var error));
        Assert.Contains("unsupported graphics workgroup barrier pc=0x0", error);
    }

    [Fact]
    public void ComputeBarrierRetainsWorkgroupSynchronization()
    {
        var (state, evaluation) = Inputs();
        Assert.True(Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 32, 1, 1,
            out var shader, out var error), error);
        var constants = new Dictionary<uint, uint>();
        var barriers = new List<uint[]>();
        for (var offset = 20; offset < shader.Spirv.Length;)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(shader.Spirv.AsSpan(offset));
            var count = (int)(word >> 16);
            Assert.True(count > 0 && offset + count * 4 <= shader.Spirv.Length);
            uint At(int i) => BinaryPrimitives.ReadUInt32LittleEndian(shader.Spirv.AsSpan(offset + i * 4));
            if ((ushort)word == 43 && count == 4) constants[At(2)] = At(3);
            if ((ushort)word == (ushort)SpirvOp.ControlBarrier) barriers.Add([At(1), At(2), At(3)]);
            offset += count * 4;
        }
        var barrier = Assert.Single(barriers);
        Assert.Equal(2u, constants[barrier[0]]);
        Assert.Equal(2u, constants[barrier[1]]);
        Assert.Equal(0x108u, constants[barrier[2]]);
    }

    private static (Gen5ShaderState, Gen5ShaderEvaluation) Inputs()
    {
        var program = new Gen5ShaderProgram(0,
        [new Gen5ShaderInstruction(0, Gen5ShaderEncoding.Sopp, "SBarrier", [], [], [], null),
         new Gen5ShaderInstruction(4, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)]);
        return (new Gen5ShaderState(program, [], null),
            new Gen5ShaderEvaluation(new uint[256], new uint[256], [], []));
    }
}
