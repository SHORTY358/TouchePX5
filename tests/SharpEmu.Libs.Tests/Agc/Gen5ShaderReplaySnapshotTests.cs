// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.Json;
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5ShaderReplaySnapshotTests
{
    [Fact]
    public void ReplaySnapshotOwnsExactlyRetainedDataAndRoundTripsBindingIdentity()
    {
        uint[] words = [0xBF810000];
        uint[] userData = [123];
        byte[] pooled = [1, 2, 3, 4, 99, 99, 99, 99];
        var program = new Gen5ShaderProgram(0x200001200, [new(0, Gen5ShaderEncoding.Sopp, "SEndpgm", words, [], [], null)]);
        var state = new Gen5ShaderState(program, userData, null, UserDataScalarRegisterBase: 8);
        var globals = new Gen5GlobalMemoryBinding(8, 0x200004000, [12], pooled, 4, true) {
            Writable = true, WriteBackToGuest = false
        };
        var vertex = new Gen5VertexInputBinding(16, 0, 4, 14, 7, 0x200004000, 24, 0, pooled, 4, true, AliasPcs: [24]);
        var evaluation = new Gen5ShaderEvaluation([42], [43], [], [globals],
            ComputeSystemRegisters: new(2, null, null, null), RuntimeScalarRegisters: new HashSet<uint> { 8 }, VertexInputs: [vertex]);
        var snapshot = Gen5ShaderReplaySnapshot.Create(state, evaluation);
        pooled[0] = 255; words[0] = 0; userData[0] = 0;
        var restored = JsonSerializer.Deserialize<Gen5ShaderReplaySnapshot>(JsonSerializer.Serialize(snapshot))!;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, restored.GlobalMemoryBindings[0].Data);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, restored.VertexInputs[0].Data);
        Assert.False(restored.GlobalMemoryBindings[0].DataPooled);
        Assert.True(restored.GlobalMemoryBindings[0].Writable);
        Assert.False(restored.GlobalMemoryBindings[0].WriteBackToGuest);
        Assert.Equal(8u, restored.GlobalMemoryBindings[0].ScalarAddress);
        Assert.Equal(0x200004000ul, restored.GlobalMemoryBindings[0].BaseAddress);
        Assert.Equal(0xBF810000u, restored.Instructions[0].Words[0]);
        Assert.Equal(123u, restored.UserData[0]);
        Assert.Null(restored.ComputeSystemRegisters);
        Assert.NotNull(restored.EvaluationComputeSystemRegisters);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void InvalidRetainedExtentDoesNotSilentlyClamp(int length)
    {
        var state = new Gen5ShaderState(new(0, []), [], null);
        var binding = new Gen5GlobalMemoryBinding(0, 0, [], new byte[4], length, true);
        Assert.Throws<ArgumentException>(() => Gen5ShaderReplaySnapshot.Create(state, new([], [], [], [binding])));
    }
}
