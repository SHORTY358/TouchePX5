// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5ComputeTransportAlignmentTests
{
    [Theory]
    [InlineData("export")]
    [InlineData("triangle")]
    [InlineData("source")]
    [InlineData("batch")]
    public void AlignmentUsesGuestBaseAddressRatherThanScalarRegisterNumber(string mode)
    {
        var state = new Gen5ShaderState(new(0, [new(0, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null)]), [], null);
        bool Compile(uint scalarRegister, ulong address)
        {
            var binding = new Gen5GlobalMemoryBinding(scalarRegister, address, [], new byte[16384], 16384, false) { Writable = true };
            var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], [binding]);
            return Gen5SpirvTranslator.TryCompileComputeShader(state, evaluation, 64, 1, 1, out _, out _,
                storageBufferOffsetAlignment: 256,
                exportOutput: mode == "export" ? new(0, 3, 1) : null,
                triangleOutput: mode == "triangle" ? new(0, 0, 3, 4) : null,
                nggCountInputs: mode == "source" ? new(4, 1, 32, 64, 17) : null,
                nggSourceInputs: mode == "source" ? new(0, 0) : null,
                nggBatchInputs: mode == "batch" ? new(0, 1664, 0, 1672, 2, 64, 32) : null);
        }
        Assert.True(Compile(8, 0x10000)); // s8 is not a byte address
        Assert.False(Compile(0, 0x10004)); // aligned s0 cannot rescue an unaligned address
    }
}
