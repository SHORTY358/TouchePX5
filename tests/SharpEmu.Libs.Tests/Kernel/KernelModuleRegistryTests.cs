// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

[CollectionDefinition(KernelModuleRegistryCollection.Name, DisableParallelization = true)]
public sealed class KernelModuleRegistryCollection
{
    public const string Name = "KernelModuleRegistryState";
}

[Collection(KernelModuleRegistryCollection.Name)]
public sealed class KernelModuleRegistryTests
{
    [Fact]
    public void Module_CanStartStopAndStartAgain()
    {
        KernelModuleRegistry.Reset();
        var handle = RegisterModule(initEntryPoint: 0x20000, finiEntryPoint: 0x30000);

        Assert.True(KernelModuleRegistry.TryBeginModuleStart(handle, out var starting));
        Assert.Equal(KernelModuleRegistry.ModuleStartState.Starting, starting.StartState);
        KernelModuleRegistry.CompleteModuleStart(handle, succeeded: true);

        Assert.True(KernelModuleRegistry.TryBeginModuleStop(handle, out var stopping));
        Assert.Equal(0x30000UL, stopping.FiniEntryPoint);
        Assert.Equal(KernelModuleRegistry.ModuleStartState.Stopping, stopping.StartState);
        KernelModuleRegistry.CompleteModuleStop(handle, succeeded: true);

        Assert.True(KernelModuleRegistry.TryGetModuleByHandle(handle, out var stopped));
        Assert.Equal(KernelModuleRegistry.ModuleStartState.Stopped, stopped.StartState);
        Assert.DoesNotContain(handle, KernelModuleRegistry.GetModuleHandles(includeSystemModules: true));
        Assert.False(KernelModuleRegistry.TryGetModuleByAddress(0x10100, out _));
        Assert.False(KernelModuleRegistry.TryBeginModuleStop(handle, out _));
        Assert.True(KernelModuleRegistry.TryBeginModuleStart(handle, out _));
    }

    [Fact]
    public void StopWithoutFinalizer_MarksStartedModuleStopped()
    {
        KernelModuleRegistry.Reset();
        var handle = RegisterModule(initEntryPoint: 0, finiEntryPoint: 0);

        Assert.False(KernelModuleRegistry.TryBeginModuleStart(handle, out _));
        Assert.False(KernelModuleRegistry.TryBeginModuleStop(handle, out _));
        Assert.True(KernelModuleRegistry.TryGetModuleByHandle(handle, out var stopped));
        Assert.Equal(KernelModuleRegistry.ModuleStartState.Stopped, stopped.StartState);
    }

    private static int RegisterModule(ulong initEntryPoint, ulong finiEntryPoint)
    {
        return KernelModuleRegistry.RegisterModule(
            "fixture.sprx",
            baseAddress: 0x10000,
            size: 0x1000,
            entryPoint: 0x10100,
            initEntryPoint,
            finiEntryPoint,
            initializerFunctions: initEntryPoint == 0 ? [] : [initEntryPoint],
            finalizerFunctions: finiEntryPoint == 0 ? [] : [finiEntryPoint],
            ehFrameHeaderAddress: 0,
            ehFrameAddress: 0,
            ehFrameSize: 0,
            isMain: false);
    }
}
