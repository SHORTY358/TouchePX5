// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler.Vulkan;

/// <summary>
/// One explicit GFX10 no-API-GS passthrough subgroup's nine-word source records.
/// Requires matching nggCountInputs. FirstWord is relative to an existing
/// retained storage binding; other dispatched groups are inactive. Caller owns
/// source identity, count validation and lifetime. No scheduler is supplied.
/// </summary>
public sealed record Gen5ComputeNggPassthroughInput(int BufferBinding, uint FirstWord);
