// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Core.Cpu;
using SharpEmu.Core.Cpu.Native;
using SharpEmu.Core.Memory;
using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Libs.Tests.Loader;

public sealed class CpuDispatcherFailureTests
{
    [Theory]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_CPU_TRAP, CpuExitReason.CpuTrap)]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT, CpuExitReason.UnhandledException)]
    public void Dispatch_PreservesConcreteBackendFailure(OrbisGen2Result failure, CpuExitReason reason)
    {
        using var dispatcher = new CpuDispatcher(new VirtualMemory(), new ModuleManager(), new FailedBackend(failure));
        Assert.Equal(failure, dispatcher.DispatchModuleInitializer(0x800200120, Generation.Gen5));
        Assert.Equal(failure, dispatcher.LastSessionSummary.Result);
        Assert.Equal(reason, dispatcher.LastSessionSummary.Reason);
        Assert.Null(dispatcher.LastNotImplementedInfo);
        Assert.Contains("test execution failure", dispatcher.LastMilestoneLog);
    }

    [Theory]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_OK)]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_IMPLEMENTED)]
    public void Dispatch_StillReportsUnavailableBackendWithoutConcreteFailure(OrbisGen2Result failure)
    {
        using var dispatcher = new CpuDispatcher(new VirtualMemory(), new ModuleManager(), new FailedBackend(failure));
        Assert.Equal(OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_IMPLEMENTED,
            dispatcher.DispatchModuleInitializer(0x800200120, Generation.Gen5));
        Assert.Equal(CpuExitReason.NativeBackendUnavailable, dispatcher.LastSessionSummary.Reason);
        Assert.NotNull(dispatcher.LastNotImplementedInfo);
    }

    private sealed class FailedBackend(OrbisGen2Result failure) : INativeCpuBackend
    {
        public string BackendName => "test";
        public string? LastError => "test execution failure";

        public bool TryExecute(CpuContext context, ulong entryPoint, Generation generation,
            IReadOnlyDictionary<ulong, string> importStubs, IReadOnlyDictionary<string, ulong> runtimeSymbols,
            CpuExecutionOptions executionOptions, out OrbisGen2Result result)
        {
            result = failure;
            return false;
        }
    }
}
