// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler;

/// <summary>
/// Checked consumption of the explicit five-word triangle export record.
/// Applies a caller-owned group's vertex output base to subgroup-relative
/// indices. Caller must establish producer completion and unchanged slot
/// numbering first; this does not implement vertex compaction or scheduling.
/// </summary>
public static class Gen5NggTriangleRemap
{
    public static bool TryRemap(ReadOnlySpan<uint> record, uint liveVertexCount,
        uint vertexOutputBase, uint vertexOutputCapacity,
        out (uint Vertex0, uint Vertex1, uint Vertex2) indices)
    {
        indices = default;
        // Suppressed stores may retain sentinels or stale values. Only the
        // exact emitted validity value is accepted, never arbitrary nonzero.
        if (record.Length != 5 || record[4] != 1) return false;
        var triangle = Gen5TriangleConnectivity.Decode(record[0]);
        if (!triangle.CanConsume(liveVertexCount) ||
            record[1] != triangle.Vertex0 || record[2] != triangle.Vertex1 || record[3] != triangle.Vertex2 ||
            (ulong)vertexOutputBase + liveVertexCount > vertexOutputCapacity) return false;
        indices = (vertexOutputBase + triangle.Vertex0,
            vertexOutputBase + triangle.Vertex1, vertexOutputBase + triangle.Vertex2);
        return true;
    }
}
