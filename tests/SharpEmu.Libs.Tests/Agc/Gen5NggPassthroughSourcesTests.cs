// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5NggPassthroughSourcesTests
{
    [Fact]
    public void ExplicitSourceIdsAndConnectivityAreIndependentOfLocalThreadNumber()
    {
        var group = new Gen5NggMergedGroup(4, 1, 32, 64, 17);
        var words = Gen5NggPassthroughSources.Encode(group,
            [new(42, 7), new(10, 8), new(900, 9), new(3, 10)], [new(1234, 0, 1, 3)]);
        Assert.Equal(64 * 9, words.Length);
        Assert.Equal(0x00300400u, words[0]);
        Assert.Equal(1234u, words[2]);
        Assert.Equal(42u, words[5]);
        Assert.Equal(7u, words[8]);
        Assert.Equal(900u, words[2 * 9 + 5]);
        Assert.Equal(9u, words[2 * 9 + 8]);
        Assert.Equal(0u, words[9]); // no primitive owned by thread one
        for (var index = 4 * 9; index < words.Length; index++) Assert.Equal(0u, words[index]);
    }

    [Fact]
    public void SourceCountMismatchAndEveryInvalidRelativeIndexReject()
    {
        var group = new Gen5NggMergedGroup(1, 1, 32, 32, 0);
        Assert.Throws<ArgumentException>(() => Gen5NggPassthroughSources.Encode(group, [], [new(0, 0, 0, 0)]));
        Assert.Throws<ArgumentException>(() => Gen5NggPassthroughSources.Encode(group, [new(5, 2)], []));
        foreach (var triangle in new Gen5NggSourceTriangle[] { new(0, 1, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1) })
            Assert.Throws<ArgumentException>(() => Gen5NggPassthroughSources.Encode(group, [new(5, 2)], [triangle]));
    }

    [Fact]
    public void DegenerateTriangleAndEmptyGroupRemainDefined()
    {
        var one = new Gen5NggMergedGroup(1, 1, 32, 32, 0);
        var words = Gen5NggPassthroughSources.Encode(one, [new(uint.MaxValue, 0)], [new(0, 0, 0, 0)]);
        Assert.Equal(uint.MaxValue, words[5]);
        Assert.Equal(0u, words[0]);
        var empty = Gen5NggPassthroughSources.Encode(new(0, 0, 32, 32, 0), [], []);
        Assert.All(empty, word => Assert.Equal(0u, word));
    }
}
