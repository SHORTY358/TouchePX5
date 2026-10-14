// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler;

/// <summary>
/// GFX10 triangle EXP target 20 connectivity. Indices are subgroup-relative,
/// nine bits wide at offsets 0, 10 and 20. This does not remap to host indices,
/// determine topology, compact vertices or interpret the intervening bits.
/// </summary>
public readonly record struct Gen5TriangleConnectivity(uint Packed)
{
    public const uint VertexMask = 0x1FF;
    public const uint NullMask = 0x80000000;
    public bool IsNull => (Packed & NullMask) != 0;
    public uint Vertex0 => Packed & VertexMask;
    public uint Vertex1 => (Packed >> 10) & VertexMask;
    public uint Vertex2 => (Packed >> 20) & VertexMask;

    public static Gen5TriangleConnectivity Decode(uint packed) =>
        new(packed);

    /// <summary>
    /// Check against the actual live subgroup vertex count before consumption.
    /// A non-null degenerate triangle is retained; it is not a decoding error.
    /// </summary>
    public bool CanConsume(uint liveVertexCount) =>
        liveVertexCount is > 0 and <= 512 && !IsNull &&
        Vertex0 < liveVertexCount && Vertex1 < liveVertexCount && Vertex2 < liveVertexCount;
}
