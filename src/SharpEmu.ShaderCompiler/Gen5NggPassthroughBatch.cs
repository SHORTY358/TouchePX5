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
