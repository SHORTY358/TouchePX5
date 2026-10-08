// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.Core.Runtime;

using SharpEmu.Core.Cpu;
using SharpEmu.Core.Cpu.Debugging;
using SharpEmu.HLE;

public readonly struct SharpEmuRuntimeOptions
{
    public CpuExecutionEngine CpuEngine { get; init; }

    /// <summary>Maximum submitted video frames per second; 0 disables pacing.</summary>
    public int FpsLimit { get; init; }

    public bool StrictDynlibResolution { get; init; }

    public int ImportTraceLimit { get; init; }

    /// <summary>Explicit platform for open homebrew with ambiguous ELF ABI metadata; null keeps autodetection.</summary>
    public Generation? GenerationOverride { get; init; }

    /// <summary>
    /// An optional debugger to attach to guest execution. Flows through to
    /// <see cref="CpuExecutionOptions.DebugHook"/>. Null (the default) runs with
    /// no debugger attached.
    /// </summary>
    public ICpuDebugHook? DebugHook { get; init; }
}
