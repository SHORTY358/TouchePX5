// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5NggPassthroughBatchTests
{
    [Fact]
    public void GroupsKeepDistinctCountsSourceIdentityAndUncompactedOutputRanges()
    {
        Gen5NggSourceVertex[] firstVertices = [new(42, 7), new(10, 8), new(900, 9), new(3, 10)];
        var batch = Gen5NggPassthroughBatch.Create([
            (new(4, 1, 32, 64, 17), firstVertices, [new(1234, 0, 1, 3)]),
            (new(2, 1, 32, 64, 18), [new(100, 3), new(200, 3)], [new(5678, 0, 1, 1)])
        ]);
        Assert.Equal(128u, batch.OutputSlotCapacity);
        Assert.Equal(2, batch.GroupCount);
        var descriptors = batch.Descriptors.Span;
        Assert.Equal(0x00404011u, descriptors[0]);
        Assert.Equal(0u, descriptors[1]);
        Assert.Equal(0u, descriptors[2]);
        Assert.Equal(0u, descriptors[3]);
        Assert.Equal(0x00402012u, descriptors[4]);
        Assert.Equal(576u, descriptors[5]);
        Assert.Equal(64u, descriptors[6]);
        Assert.Equal(64u, descriptors[7]);
        Assert.Equal(0x00100400u, batch.SourceWords.Span[576]);
        Assert.Equal(5678u, batch.SourceWords.Span[576 + 2]);
        Assert.Equal(100u, batch.SourceWords.Span[576 + 5]);
        Assert.True(Gen5NggTriangleRemap.TryRemap([0x00300400, 0, 1, 3, 1], 4, descriptors[2], 128, out var first));
        Assert.True(Gen5NggTriangleRemap.TryRemap([0x00100400, 0, 1, 1, 1], 2, descriptors[6], 128, out var second));
        Assert.Equal((0u, 1u, 3u), first);
        Assert.Equal((64u, 65u, 65u), second);
        firstVertices[0] = new(999, 999);
        Assert.Equal(42u, batch.SourceWords.Span[5]); // no retained caller-array alias
    }

    [Fact]
    public void EmptySubgroupStillReservesSlotsAndDefinedSources()
    {
        var batch = Gen5NggPassthroughBatch.Create([(new(0, 0, 32, 64, 0), [], [])]);
        Assert.Equal(64u, batch.OutputSlotCapacity);
        Assert.All(batch.SourceWords.ToArray(), value => Assert.Equal(0u, value));
    }

    [Fact]
    public void EmptyBatchAndMixedExecutionLayoutsReject()
    {
        Assert.Throws<ArgumentException>(() => Gen5NggPassthroughBatch.Create([]));
        Assert.Throws<ArgumentException>(() => Gen5NggPassthroughBatch.Create([
            (new(0, 0, 32, 64, 0), [], []), (new(0, 0, 64, 64, 1), [], [])]));
        Assert.Throws<ArgumentException>(() => Gen5NggPassthroughBatch.Create([
            (new(0, 0, 32, 64, 0), [], []), (new(0, 0, 32, 128, 1), [], [])]));
    }
}
