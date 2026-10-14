// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5NggMergedGroupTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(13u)]
    [InlineData(32u)]
    [InlineData(33u)]
    [InlineData(63u)]
    [InlineData(64u)]
    public void Wave32TailsCoverExactlyTheLiveVertices(uint count)
    {
        var group = new Gen5NggMergedGroup(count, 13, 32, 64, 17);
        Assert.Equal(count, (group.GroupInfo >> 12) & 511);
        Assert.Equal(13u, (group.GroupInfo >> 22) & 511);
        Assert.Equal(17u, group.GroupInfo & 4095);
        var activeVertices = 0;
        var activePrimitives = 0;
        for (uint local = 0; local < 64; local++)
        {
            var waveInfo = group.GetWaveInfo(local / 32);
            Assert.Equal(local / 32, (waveInfo >> 24) & 15);
            Assert.Equal(0u, waveInfo & 0xF0FF0000); // no invented reserved fields
            if (local % 32 < (waveInfo & 255)) activeVertices++;
            if (local % 32 < ((waveInfo >> 8) & 255)) activePrimitives++;
        }
        Assert.Equal((int)count, activeVertices);
        Assert.Equal(13, activePrimitives);
    }

    [Fact]
    public void Wave64FieldsAndMaximumGroupCountsDoNotSpill()
    {
        var group = new Gen5NggMergedGroup(511, 511, 64, 512, 4095);
        Assert.Equal(0x7FDF_FFFFu, group.GroupInfo);
        Assert.Equal(0x00004040u, group.GetWaveInfo(0));
        Assert.Equal(0x07003F3Fu, group.GetWaveInfo(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => group.GetWaveInfo(8));
    }

    [Theory]
    [InlineData(512u, 0u, 32u, 512u, 0u)]
    [InlineData(0u, 512u, 32u, 512u, 0u)]
    [InlineData(65u, 0u, 32u, 64u, 0u)]
    [InlineData(0u, 65u, 32u, 64u, 0u)]
    [InlineData(0u, 0u, 16u, 64u, 0u)]
    [InlineData(0u, 0u, 32u, 0u, 0u)]
    [InlineData(0u, 0u, 32u, 33u, 0u)]
    [InlineData(0u, 0u, 32u, 544u, 0u)]
    [InlineData(0u, 0u, 32u, 64u, 4096u)]
    public void UnrepresentableOrInconsistentInputsAreRejected(uint vertices, uint primitives,
        uint wave, uint threads, uint ordered)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Gen5NggMergedGroup(vertices, primitives, wave, threads, ordered));
    }
}
