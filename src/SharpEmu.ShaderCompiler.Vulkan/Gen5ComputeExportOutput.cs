// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler.Vulkan;

/// <summary>
/// Explicit output transport for one-dimensional compute expansion. Each source
/// invocation owns one position float4 followed by ParameterCount float4 slots.
/// This does not supply NGG system inputs or decode primitive connectivity.
/// </summary>
public sealed record Gen5ComputeExportOutput(int BufferBinding, uint RecordCapacity, uint ParameterCount);
