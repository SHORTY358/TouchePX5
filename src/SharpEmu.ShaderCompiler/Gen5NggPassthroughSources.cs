// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler;

public readonly record struct Gen5NggSourceVertex(uint VertexId, uint InstanceId);
public readonly record struct Gen5NggSourceTriangle(uint PrimitiveId, uint Vertex0, uint Vertex1, uint Vertex2);

/// <summary>
/// GFX10 no-API-GS passthrough input records, nine VGPR words per local thread.
/// This layout is specific to the proven passthrough mode; culling and API GS
/// inputs require different contracts. Source IDs are supplied by the caller,
/// not inferred from a local thread ID. Guest count predicates guard idle lanes.
/// </summary>
public static class Gen5NggPassthroughSources
{
    public const int RecordWords = 9;
    public static uint[] Encode(Gen5NggMergedGroup group,
        ReadOnlySpan<Gen5NggSourceVertex> vertices, ReadOnlySpan<Gen5NggSourceTriangle> triangles)
    {
        ArgumentNullException.ThrowIfNull(group);
        if ((uint)vertices.Length != group.VertexCount || (uint)triangles.Length != group.PrimitiveCount)
            throw new ArgumentException("Source counts must match the explicit subgroup counts");
        var words = new uint[checked((int)group.ThreadCount * RecordWords)];
        for (var index = 0; index < vertices.Length; index++)
        {
            words[index * RecordWords + 5] = vertices[index].VertexId;
            words[index * RecordWords + 8] = vertices[index].InstanceId;
        }
        for (var index = 0; index < triangles.Length; index++)
        {
            var triangle = triangles[index];
            if (triangle.Vertex0 >= group.VertexCount || triangle.Vertex1 >= group.VertexCount ||
                triangle.Vertex2 >= group.VertexCount)
                throw new ArgumentException("Primitive source index must refer to a live subgroup vertex");
            words[index * RecordWords] = triangle.Vertex0 | (triangle.Vertex1 << 10) | (triangle.Vertex2 << 20);
            words[index * RecordWords + 2] = triangle.PrimitiveId;
        }
        return words;
    }
}
