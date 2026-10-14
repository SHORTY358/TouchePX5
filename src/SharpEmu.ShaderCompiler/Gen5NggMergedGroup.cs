// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler;

/// <summary>
/// Explicit GFX10 merged group/wave count fields consumed by LLPC NGG.
/// Counts describe one subgroup, not the entire draw. Only fields decoded by
/// NggPrimShader::initWaveThreadInfo are supplied; no source IDs, scratch/ring
/// state, group scheduling or culling/compaction behavior is established here.
/// </summary>
public sealed class Gen5NggMergedGroup
{
    public uint VertexCount { get; }
    public uint PrimitiveCount { get; }
    public uint WaveSize { get; }
    public uint ThreadCount { get; }
    public uint OrderedWaveId { get; }
    public uint WaveCount => ThreadCount / WaveSize;
    public uint GroupInfo => (PrimitiveCount << 22) | (VertexCount << 12) | OrderedWaveId;

    public Gen5NggMergedGroup(uint vertexCount, uint primitiveCount, uint waveSize,
        uint threadCount, uint orderedWaveId)
    {
        if (waveSize is not 32 and not 64)
            throw new ArgumentOutOfRangeException(nameof(waveSize));
        if (threadCount == 0 || threadCount % waveSize != 0 || threadCount / waveSize > 16)
            throw new ArgumentOutOfRangeException(nameof(threadCount));
        // Each group count has nine bits; 512 would corrupt a neighboring field.
        if (vertexCount > 511 || vertexCount > threadCount)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (primitiveCount > 511 || primitiveCount > threadCount)
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        if (orderedWaveId > 4095)
            throw new ArgumentOutOfRangeException(nameof(orderedWaveId));
        VertexCount = vertexCount;
        PrimitiveCount = primitiveCount;
        WaveSize = waveSize;
        ThreadCount = threadCount;
        OrderedWaveId = orderedWaveId;
    }

    public uint GetWaveInfo(uint waveIndex)
    {
        if (waveIndex >= WaveCount) throw new ArgumentOutOfRangeException(nameof(waveIndex));
        var first = waveIndex * WaveSize;
        var vertices = VertexCount > first ? Math.Min(VertexCount - first, WaveSize) : 0;
        var primitives = PrimitiveCount > first ? Math.Min(PrimitiveCount - first, WaveSize) : 0;
        return (waveIndex << 24) | (primitives << 8) | vertices;
    }
}
