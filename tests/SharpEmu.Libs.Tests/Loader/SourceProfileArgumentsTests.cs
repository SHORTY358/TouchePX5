// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text;
using SharpEmu.Core.Cpu;
using SharpEmu.Core.Cpu.Native;
using SharpEmu.Core.Memory;
using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Libs.Tests.Loader;

[CollectionDefinition("Process arguments", DisableParallelization = true)]
public sealed class ProcessArgumentsCollection { }

[Collection("Process arguments")]
public sealed class SourceProfileArgumentsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EntryFrame_RejectsOversizedSourceProfile(bool tooMany)
    {
        var previous = Environment.GetEnvironmentVariable("SHARPEMU_GUEST_ARGS");
        var previousMode = Environment.GetEnvironmentVariable("SHARPEMU_SOURCE_PROFILE_ARGV");
        try
        {
            Environment.SetEnvironmentVariable("SHARPEMU_GUEST_ARGS", tooMany ? string.Join(' ', Enumerable.Repeat("x", 32)) : new string('x', 4097));
            Environment.SetEnvironmentVariable("SHARPEMU_SOURCE_PROFILE_ARGV", "1");
            using var dispatcher = new CpuDispatcher(new VirtualMemory(), new ModuleManager());
            Assert.Throws<ArgumentException>(() => dispatcher.DispatchEntry(0x800200160, Generation.Gen5));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPEMU_GUEST_ARGS", previous);
            Environment.SetEnvironmentVariable("SHARPEMU_SOURCE_PROFILE_ARGV", previousMode);
        }
    }

    [Theory]
    [InlineData(false, "--configdir /temp0 --tedlevel 0 --nowait --normal", 3)]
    [InlineData(true, "--configdir /temp0 --tedlevel 0 --nowait --normal", 7)]
    [InlineData(true, "", 1)]
    [InlineData(true, "--nowait", 2)]
    public void EntryFrame_PreservesDefaultAndPassesOptInArguments(bool extended, string configured, int count)
    {
        var previous = Environment.GetEnvironmentVariable("SHARPEMU_GUEST_ARGS");
        var previousMode = Environment.GetEnvironmentVariable("SHARPEMU_SOURCE_PROFILE_ARGV");
        try
        {
            Environment.SetEnvironmentVariable("SHARPEMU_GUEST_ARGS", configured);
            Environment.SetEnvironmentVariable("SHARPEMU_SOURCE_PROFILE_ARGV", extended ? "1" : null);
            var backend = new ArgumentBackend(configured.Split(' ', StringSplitOptions.RemoveEmptyEntries), count, extended);
            using var dispatcher = new CpuDispatcher(new VirtualMemory(), new ModuleManager(), backend);
            Assert.Equal(OrbisGen2Result.ORBIS_GEN2_OK, dispatcher.DispatchEntry(0x800200160, Generation.Gen5));
            Assert.True(backend.Checked);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPEMU_GUEST_ARGS", previous);
            Environment.SetEnvironmentVariable("SHARPEMU_SOURCE_PROFILE_ARGV", previousMode);
        }
    }

    private sealed class ArgumentBackend(string[] arguments, int count, bool extended) : INativeCpuBackend
    {
        public string BackendName => "argument frame verifier";
        public string? LastError => null;
        public bool Checked { get; private set; }
        public bool TryExecute(CpuContext context, ulong entryPoint, Generation generation,
            IReadOnlyDictionary<ulong, string> importStubs, IReadOnlyDictionary<string, ulong> runtimeSymbols,
            CpuExecutionOptions executionOptions, out OrbisGen2Result result)
        {
            var frame = context[CpuRegister.Rdi];
            Assert.True(context.TryReadUInt32(frame, out var argc));
            Assert.Equal((uint)count, argc);
            for (var index = 0; index < count; index++)
            {
                Assert.True(context.TryReadUInt64(frame + 8UL + (ulong)index * 8, out var pointer));
                var expected = index == 0 ? "eboot.bin" : arguments[index - 1];
                var bytes = new byte[Encoding.UTF8.GetByteCount(expected) + 1];
                Assert.True(context.Memory.TryRead(pointer, bytes));
                Assert.Equal(expected + '\0', Encoding.UTF8.GetString(bytes));
            }
            if (extended)
            {
                Assert.True(context.TryReadUInt64(frame + 8UL + (ulong)count * 8, out var terminator));
                Assert.Equal(0UL, terminator);
            }
            Checked = true;
            result = OrbisGen2Result.ORBIS_GEN2_OK;
            return true;
        }
    }
}
