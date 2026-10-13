// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

[Collection(KernelModuleRegistryCollection.Name)]
public sealed class SysmoduleInternalExportsTests
{
    [Fact]
    public void GearsInternalLifecycleResolvesByNidAndIgnoresWithArgScratchPointer()
    {
        const ulong memoryBase = 0x1_0000_0000;
        const uint agcModule = 0x80000094;
        var memory = new FakeCpuMemory(memoryBase, 4096);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));
        Assert.True(manager.TryGetExport("39iV5E1HoCk", out var load));
        Assert.Equal("sceSysmoduleLoadModuleInternal", load.Name);
        Assert.Equal("libSceSysmodule", load.LibraryName);
        Assert.True(manager.TryGetExport("vXZhrtJxkGc", out var unload));
        Assert.Equal("sceSysmoduleUnloadModuleInternal", unload.Name);
        ctx[CpuRegister.Rdi] = agcModule;
        // A stale, unmapped R8 must not cause a memory fault or any write.
        ctx[CpuRegister.R8] = ulong.MaxValue;
        var sentinel = Enumerable.Repeat((byte)0xA5, 4096).ToArray();
        Assert.True(memory.TryWrite(memoryBase, sentinel));
        try
        {
            Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("39iV5E1HoCk", ctx));
            Assert.Equal(0UL, ctx[CpuRegister.Rax]);
            Assert.True(KernelModuleRegistry.IsSysmoduleLoaded(unchecked((int)agcModule)));
            Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("fMP5NHUOaMk", ctx));
            Assert.Equal(0UL, ctx[CpuRegister.Rax]);
            Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("vXZhrtJxkGc", ctx));
            Assert.Equal(0UL, ctx[CpuRegister.Rax]);
            Assert.False(KernelModuleRegistry.IsSysmoduleLoaded(unchecked((int)agcModule)));
            Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, manager.Dispatch("fMP5NHUOaMk", ctx));
            Assert.NotEqual(0UL, ctx[CpuRegister.Rax]);
            var after = new byte[4096];
            Assert.True(memory.TryRead(memoryBase, after));
            Assert.Equal(sentinel, after);
        }
        finally
        {
            KernelRuntimeCompatExports.SysmoduleUnloadModuleInternal(ctx);
        }
    }
}
