// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
using Xunit;

namespace SharpEmu.ShaderCompiler.Tests;

public sealed class Gen5NggIndexedBatchTests
{
    [Fact]
    public void SharedQuadUsesFourVerticesAndPreservesTwoPrimitiveRecords()
    {
        var batch=Gen5NggPassthroughBatch.CreateIndexedTriangleList([0,1,2,0,2,3],4);
        var explicitBatch=Gen5NggPassthroughBatch.Create([(new(4,2,32,128,0),
            new Gen5NggSourceVertex[]{new(0,0),new(1,0),new(2,0),new(3,0)},
            new Gen5NggSourceTriangle[]{new(0,0,1,2),new(1,0,2,3)})]);
        Assert.Equal(explicitBatch.Descriptors.ToArray(),batch.Descriptors.ToArray());
        Assert.Equal(explicitBatch.SourceWords.ToArray(),batch.SourceWords.ToArray());
        Assert.Equal(128u,batch.OutputSlotCapacity);
    }

    [Fact]
    public void GroupsKeepGlobalSourcesAndPrimitiveOrderAcrossSharedBoundaries()
    {
        var indices=Enumerable.Range(0,132).Select(i=>(uint)(i%7+10)).ToArray();
        var batch=Gen5NggPassthroughBatch.CreateIndexedTriangleList(indices,30,-3,9);
        Assert.Equal(2,batch.GroupCount);Assert.Equal(256u,batch.OutputSlotCapacity);
        var words=batch.SourceWords.Span;
        // Second group's first source is index 126: 10 - 3, not a new ordinal.
        Assert.Equal(7u,words[128*9+5]);Assert.Equal(9u,words[128*9+8]);
        Assert.Equal(42u,words[128*9+2]);Assert.Equal(43u,words[129*9+2]);
        Assert.Equal(0x00200400u,words[128*9]);
        Assert.Equal(128u,batch.Descriptors.Span[6]);
        Assert.Equal(128u,batch.Descriptors.Span[7]);
    }

    [Fact]
    public void DegenerateAndReversedTrianglesRetainTheirOriginalConnectivity()
    {
        var batch=Gen5NggPassthroughBatch.CreateIndexedTriangleList([5,5,5,7,5,3],8);
        Assert.Equal(0u,batch.SourceWords.Span[0]);
        Assert.Equal(1u|(0u<<10)|(2u<<20),batch.SourceWords.Span[9]);
        Assert.Equal(5u,batch.SourceWords.Span[5]);Assert.Equal(7u,batch.SourceWords.Span[14]);
        Assert.Equal(3u,batch.SourceWords.Span[23]);
    }

    [Theory]
    [InlineData(0u,0)]
    [InlineData(3u,0)]
    [InlineData(uint.MaxValue,0)]
    [InlineData(0u,-1)]
    [InlineData(uint.MaxValue,int.MaxValue)]
    public void InvalidAdjustedIndicesReject(uint index,int baseVertex)
    {
        Assert.ThrowsAny<ArgumentException>(()=>Gen5NggPassthroughBatch.CreateIndexedTriangleList([index,index,index],index==0 && baseVertex==0?0u:3u,baseVertex));
    }

    [Fact]
    public void InvalidTriangleExtentsReject()
    {
        Assert.ThrowsAny<ArgumentException>(()=>Gen5NggPassthroughBatch.CreateIndexedTriangleList([],3));
        Assert.ThrowsAny<ArgumentException>(()=>Gen5NggPassthroughBatch.CreateIndexedTriangleList([0,1],3));
        Assert.ThrowsAny<ArgumentException>(()=>Gen5NggPassthroughBatch.CreateIndexedTriangleList(new uint[2397],1));
    }
}
