// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5TriangleConnectivityTests
{
    [Fact]
    public void DecodeRetainsPackedBitsWithoutTreatingGapsAsIndices()
    {
        // Index fields 1/256/511; gap bits 9,19,29 and bit 30 are retained
        // for later interpretation, not included in these nine-bit indices.
        const uint packed = 0x7FFC0201;
        var triangle = Gen5TriangleConnectivity.Decode(packed);
        Assert.Equal(packed, triangle.Packed);
        Assert.Equal(1u, triangle.Vertex0);
        Assert.Equal(256u, triangle.Vertex1);
        Assert.Equal(511u, triangle.Vertex2);
        Assert.True(triangle.CanConsume(512));
        Assert.False(triangle.CanConsume(511));
    }

    [Theory]
    [InlineData(0x80000000u)]
    [InlineData(0x80300400u)]
    public void NullPrimitiveCannotBeConsumedEvenWithValidIndices(uint packed)
    {
        var triangle = Gen5TriangleConnectivity.Decode(packed);
        Assert.True(triangle.IsNull);
        Assert.False(triangle.CanConsume(512));
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(3u << 10)]
    [InlineData(3u << 20)]
    public void EveryIndexMustReferToLiveSubgroupVertex(uint packed)
    {
        var triangle = Gen5TriangleConnectivity.Decode(packed);
        Assert.False(triangle.CanConsume(3));
        Assert.True(triangle.CanConsume(4));
    }

    [Fact]
    public void DegeneratePrimitiveIsPreservedButEmptyOrUnrepresentableCountsAreRejected()
    {
        var triangle = Gen5TriangleConnectivity.Decode(0);
        Assert.True(triangle.CanConsume(1));
        Assert.False(triangle.CanConsume(0));
        Assert.False(triangle.CanConsume(513));
    }
}
