// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;
public sealed class Gen5NggTriangleListPartitionTests
{
    [Fact]
    public void BoundaryPartitionPreservesAbsoluteIdsAndLocalTriangleConnectivity()
    {
        var batch = Gen5NggPassthroughBatch.CreateNonindexedTriangleList(129, 1000, 7);
        Assert.Equal(2, batch.GroupCount);
        Assert.Equal(256u, batch.OutputSlotCapacity);
        var sources = batch.SourceWords.Span;
        Assert.Equal(1000u, sources[5]);
        Assert.Equal(1125u, sources[125 * 9 + 5]);
        Assert.Equal(1126u, sources[128 * 9 + 5]);
        Assert.Equal(1128u, sources[130 * 9 + 5]);
        Assert.Equal(7u, sources[128 * 9 + 8]);
        Assert.Equal(42u, sources[128 * 9 + 2]);
        Assert.Equal(0x00200400u, sources[128 * 9]);
        var lastFirstGroupTriangle = Gen5TriangleConnectivity.Decode(sources[41 * 9]);
        Assert.Equal(123u, lastFirstGroupTriangle.Vertex0);
        Assert.Equal(125u, lastFirstGroupTriangle.Vertex2);
        Assert.Equal(0u, sources[126 * 9 + 5]);
        Assert.Equal(0u, sources[131 * 9 + 5]);
        Assert.Equal((1u << 22) | (3u << 12) | 1u, batch.Descriptors.Span[4]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(4u)]
    [InlineData(2397u)]
    public void InvalidOrOversizedTriangleListsReject(uint count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Gen5NggPassthroughBatch.CreateNonindexedTriangleList(count));

    [Fact]
    public void SourceVertexIdOverflowRejectsInsteadOfWrapping() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Gen5NggPassthroughBatch.CreateNonindexedTriangleList(3, uint.MaxValue - 1));
}
