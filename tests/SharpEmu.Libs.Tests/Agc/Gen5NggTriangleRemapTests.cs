// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class Gen5NggTriangleRemapTests
{
    [Fact]
    public void SeparateGroupsWithSameRelativeConnectivityUseTheirOwnOutputVertices()
    {
        uint[] record = [0x00300400, 0, 1, 3, 1];
        Assert.True(Gen5NggTriangleRemap.TryRemap(record, 4, 10, 104, out var first));
        Assert.True(Gen5NggTriangleRemap.TryRemap(record, 4, 100, 104, out var second));
        Assert.Equal((10u, 11u, 13u), first);
        Assert.Equal((100u, 101u, 103u), second);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(0xCAFEBABEu)]
    public void OnlyExactValidityOneIsAccepted(uint validity)
    {
        Assert.False(Gen5NggTriangleRemap.TryRemap([0x00300400, 0, 1, 3, validity], 4, 0, 4, out _));
    }

    [Theory]
    [InlineData(0x80300400u, 0u, 1u, 3u, 4u)] // null with forged validity
    [InlineData(0x00300400u, 0u, 1u, 3u, 3u)] // missing live vertex
    [InlineData(0x00300400u, 1u, 1u, 3u, 4u)] // mismatched index0
    [InlineData(0x00300400u, 0u, 2u, 3u, 4u)] // mismatched index1
    [InlineData(0x00300400u, 0u, 1u, 2u, 4u)] // mismatched index2
    public void InconsistentOrInvalidProducedRecordIsRejected(uint packed, uint a, uint b, uint c, uint count)
    {
        Assert.False(Gen5NggTriangleRemap.TryRemap([packed, a, b, c, 1], count, 0, 4, out var indices));
        Assert.Equal(default, indices);
    }

    [Theory]
    [InlineData(100u, 103u)]
    [InlineData(uint.MaxValue - 2, uint.MaxValue)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void FullGroupOutputRangeMustFitWithoutOverflow(uint outputBase, uint capacity)
    {
        Assert.False(Gen5NggTriangleRemap.TryRemap([0x00300400, 0, 1, 3, 1], 4, outputBase, capacity, out _));
    }

    [Fact]
    public void ExactRecordShapeAndDegenerateTriangleAreHandled()
    {
        Assert.False(Gen5NggTriangleRemap.TryRemap([0, 0, 0, 1], 1, 0, 1, out _));
        Assert.False(Gen5NggTriangleRemap.TryRemap([0, 0, 0, 0, 1, 0], 1, 0, 1, out _));
        Assert.True(Gen5NggTriangleRemap.TryRemap([0, 0, 0, 0, 1], 1, 7, 8, out var indices));
        Assert.Equal((7u, 7u, 7u), indices);
    }
}
