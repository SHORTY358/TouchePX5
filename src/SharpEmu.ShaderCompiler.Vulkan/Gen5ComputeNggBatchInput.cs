// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.ShaderCompiler;
namespace SharpEmu.ShaderCompiler.Vulkan;

/// <summary>
/// Explicit canonical passthrough batch transport. Descriptor/source offsets
/// are binding-relative words. Each group reserves ThreadCount source/vertex/
/// primitive slots; no compaction or hardware scheduling is implemented.
/// Triangle output LiveVertexCount must be zero to select descriptor counts.
/// </summary>
public sealed record Gen5ComputeNggBatchInput(int DescriptorBinding, uint DescriptorFirstWord,
    int SourceBinding, uint SourceFirstWord, uint GroupCount, uint ThreadCount, uint WaveSize)
{
    internal bool Validate(Gen5ShaderEvaluation evaluation, int totalBuffers, int scalarBuffer,
        ulong alignment, Gen5ComputeExportOutput? vertices, Gen5ComputeTriangleOutput? primitives)
    {
        bool Range(int binding, uint first, ulong length) => binding >= 0 && binding < evaluation.GlobalMemoryBindings.Count &&
            (totalBuffers < 0 || binding < totalBuffers) && binding != scalarBuffer &&
            evaluation.GlobalMemoryBindings[binding].DataLength > 0 &&
            evaluation.GlobalMemoryBindings[binding].DataLength <= evaluation.GlobalMemoryBindings[binding].Data.Length &&
            evaluation.GlobalMemoryBindings[binding].ScalarAddress % Math.Max(alignment, 1) == 0 &&
            ((ulong)first + length) * 4 <= (ulong)evaluation.GlobalMemoryBindings[binding].DataLength;
        bool Overlap(int a, ulong firstA, ulong lengthA, int b, ulong firstB, ulong lengthB) =>
            a == b && firstA < firstB + lengthB && firstB < firstA + lengthA;
        if (GroupCount == 0 || WaveSize is not 32 and not 64 || ThreadCount == 0 ||
            ThreadCount % WaveSize != 0 || ThreadCount / WaveSize > 16) return false;
        var slots = (ulong)GroupCount * ThreadCount;
        var descriptors = (ulong)GroupCount * 4;
        var sources = slots * 9;
        if (!Range(DescriptorBinding, DescriptorFirstWord, descriptors) || !Range(SourceBinding, SourceFirstWord, sources) ||
            Overlap(DescriptorBinding, DescriptorFirstWord, descriptors, SourceBinding, SourceFirstWord, sources)) return false;
        foreach (var input in new[] { (DescriptorBinding, DescriptorFirstWord, descriptors), (SourceBinding, SourceFirstWord, sources) })
        {
            if (vertices is { } v && (v.RecordCapacity < slots ||
                Overlap(input.Item1, input.Item2, input.Item3, v.BufferBinding, 0, (ulong)v.RecordCapacity * ((ulong)v.ParameterCount + 1) * 4))) return false;
            if (primitives is { } p && (p.RecordCapacity < slots || p.LiveVertexCount != 0 ||
                Overlap(input.Item1, input.Item2, input.Item3, p.BufferBinding, p.FirstWord, (ulong)p.RecordCapacity * 5))) return false;
        }
        return true;
    }
}
