// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler;

/// <summary>
/// Host transport plan for explicit no-API-GS passthrough subgroups with a
/// common thread extent/wave size. Four descriptor words per group: GroupInfo,
/// SourceFirstWord, VertexOutputBase, PrimitiveOutputBase. Source offsets are
/// relative to SourceWords; output bases are record indices, never source IDs.
/// Full thread-slot ranges are reserved even for short/empty groups: no vertex
/// compaction, reordered topology or hardware scheduling is implied.
/// </summary>
public sealed class Gen5NggPassthroughBatch
{
    public const int DescriptorWords = 4;
    public uint WaveSize { get; }
    public uint ThreadCount { get; }
    public int GroupCount { get; }
    public uint OutputSlotCapacity { get; }
    public ReadOnlyMemory<uint> Descriptors { get; }
    public ReadOnlyMemory<uint> SourceWords { get; }

    private Gen5NggPassthroughBatch(uint wave, uint threads, int groups, uint capacity,
        uint[] descriptors, uint[] sources)
    {
        WaveSize = wave; ThreadCount = threads; GroupCount = groups;
        OutputSlotCapacity = capacity; Descriptors = descriptors; SourceWords = sources;
    }

    /// <summary>
    /// Bounded GFX10 no-API-GS passthrough expansion of one nonindexed,
    /// single-instance triangle list. Audit ordinals are explicit ordered IDs;
    /// this does not model AMD hardware scheduling, indexed reuse or culling.
    /// </summary>
    public static Gen5NggPassthroughBatch CreateNonindexedTriangleList(uint vertexCount,
        uint firstVertex = 0, uint instanceId = 0)
    {
        // 19 groups fit the bounded export transport used by the native prototype.
        if (vertexCount == 0 || vertexCount > 2394 || vertexCount % 3 != 0)
            throw new ArgumentOutOfRangeException(nameof(vertexCount), "Require 1..798 complete triangles");
        if (firstVertex > uint.MaxValue - (vertexCount - 1))
            throw new ArgumentOutOfRangeException(nameof(firstVertex), "Source vertex ID overflow");
        var groups = new List<(Gen5NggMergedGroup Counts, Gen5NggSourceVertex[] Vertices, Gen5NggSourceTriangle[] Triangles)>();
        for (uint first = 0; first < vertexCount; first += 126)
        {
            var count = Math.Min(126u, vertexCount - first);
            var vertices = Enumerable.Range(0, (int)count)
                .Select(i => new Gen5NggSourceVertex(firstVertex + first + (uint)i, instanceId)).ToArray();
            var triangles = Enumerable.Range(0, (int)count / 3).Select(i =>
                new Gen5NggSourceTriangle(first / 3 + (uint)i, (uint)i * 3, (uint)i * 3 + 1, (uint)i * 3 + 2)).ToArray();
            groups.Add((new(count, count / 3, 32, 128, (uint)groups.Count), vertices, triangles));
        }
        return Create(groups);
    }

    /// <summary>
    /// Bounded indexed transport for the same no-API-GS triangle-list mode.
    /// Preserve primitive order and deduplicate source IDs within each explicit
    /// group of at most 42 triangles. Shared vertices crossing groups are
    /// intentionally repeated. This is not hardware cache/scheduling behavior.
    /// </summary>
    public static Gen5NggPassthroughBatch CreateIndexedTriangleList(ReadOnlySpan<uint> indices,
        uint vertexRecordCount, int baseVertex = 0, uint instanceId = 0)
    {
        if(indices.Length is 0 or >2394 || indices.Length%3!=0)
            throw new ArgumentException("Require 1..798 complete indexed triangles",nameof(indices));
        if(vertexRecordCount==0) throw new ArgumentOutOfRangeException(nameof(vertexRecordCount));
        var groups=new List<(Gen5NggMergedGroup Counts,Gen5NggSourceVertex[] Vertices,Gen5NggSourceTriangle[] Triangles)>();
        for(var first=0;first<indices.Length;first+=126)
        {
            var length=Math.Min(126,indices.Length-first);
            var vertices=new List<Gen5NggSourceVertex>();var localBySource=new Dictionary<uint,uint>();
            var localIndices=new uint[length];
            for(var i=0;i<length;i++)
            {
                var adjusted=(long)indices[first+i]+baseVertex;
                if(adjusted<0 || adjusted>=vertexRecordCount)
                    throw new ArgumentOutOfRangeException(nameof(indices),"Adjusted source index outside captured vertex records");
                var source=(uint)adjusted;
                if(!localBySource.TryGetValue(source,out var local))
                {
                    local=(uint)vertices.Count;localBySource.Add(source,local);
                    vertices.Add(new(source,instanceId));
                }
                localIndices[i]=local;
            }
            var triangles=Enumerable.Range(0,length/3).Select(i=>new Gen5NggSourceTriangle((uint)(first/3+i),
                localIndices[i*3],localIndices[i*3+1],localIndices[i*3+2])).ToArray();
            groups.Add((new((uint)vertices.Count,(uint)triangles.Length,32,128,(uint)groups.Count),vertices.ToArray(),triangles));
        }
        return Create(groups);
    }

    public static Gen5NggPassthroughBatch Create(IReadOnlyList<(Gen5NggMergedGroup Counts,
        Gen5NggSourceVertex[] Vertices, Gen5NggSourceTriangle[] Triangles)> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0) throw new ArgumentException("Explicit subgroups required", nameof(groups));
        ArgumentNullException.ThrowIfNull(groups[0].Counts);
        var wave = groups[0].Counts.WaveSize;
        var threads = groups[0].Counts.ThreadCount;
        var capacity = checked((uint)groups.Count * threads);
        var descriptors = new uint[checked(groups.Count * DescriptorWords)];
        var sources = new uint[checked((int)capacity * Gen5NggPassthroughSources.RecordWords)];
        for (var group = 0; group < groups.Count; group++)
        {
            var item = groups[group];
            ArgumentNullException.ThrowIfNull(item.Counts);
            ArgumentNullException.ThrowIfNull(item.Vertices);
            ArgumentNullException.ThrowIfNull(item.Triangles);
            if (item.Counts.WaveSize != wave || item.Counts.ThreadCount != threads)
                throw new ArgumentException("A batch requires one wave size and thread extent", nameof(groups));
            var words = Gen5NggPassthroughSources.Encode(item.Counts, item.Vertices, item.Triangles);
            var firstSlot = checked((uint)group * threads);
            var sourceFirst = checked(firstSlot * Gen5NggPassthroughSources.RecordWords);
            Array.Copy(words, 0, sources, checked((int)sourceFirst), words.Length);
            var descriptor = group * DescriptorWords;
            descriptors[descriptor] = item.Counts.GroupInfo;
            descriptors[descriptor + 1] = sourceFirst;
            descriptors[descriptor + 2] = firstSlot;
            descriptors[descriptor + 3] = firstSlot;
        }
        return new(wave, threads, groups.Count, capacity, descriptors, sources);
    }
}
