// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler.Vulkan;

/// <summary>
/// Explicit GFX10 triangle EXP transport. Each invocation owns five uint words:
/// packed connectivity, three subgroup-relative indices, and a consumable flag.
/// LiveVertexCount must be the actual live count for every dispatched subgroup.
/// No compaction, topology selection, group-to-host remapping or NGG inputs are
/// supplied here. FirstWord is relative to the existing storage binding.
/// Caller must clear validity words before dispatch and consume only flag == 1
/// after producer completion; suppressed exports leave prior storage untouched.
/// </summary>
public sealed record Gen5ComputeTriangleOutput(int BufferBinding, uint FirstWord,
    uint RecordCapacity, uint LiveVertexCount);
