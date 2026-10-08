// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Iced.Intel;
using SharpEmu.Core.Cpu.Native;
using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Libs.Tests.Cpu;

public sealed class ExceptionHandlerTrampolineTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Handler(nint exceptionPointers);

    [Fact]
    public unsafe void Handler_LocksItsOwnStorageAndSupportsRecursiveEntry()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        var backend = (DirectExecutionBackend)RuntimeHelpers.GetUninitializedObject(typeof(DirectExecutionBackend));
        var lockStorage = Marshal.AllocHGlobal(16);
        new Span<byte>((void*)lockStorage, 16).Clear();
        typeof(DirectExecutionBackend).GetField("_vehManagedEntryLock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, lockStorage);
        Handler? native = null;
        var calls = 0;
        var observedDepths = new List<int>();
        Handler callback = pointers =>
        {
            calls++;
            // The exception pointer is deliberately distinct from the lock.
            Assert.Equal(unchecked((int)0x80000003), Marshal.ReadInt32(Marshal.ReadIntPtr(pointers)));
            Assert.NotEqual(0L, Marshal.ReadInt64(lockStorage));
            observedDepths.Add(Marshal.ReadInt32(lockStorage, 8));
            if (calls == 1) Assert.Equal(0, native!(pointers));
            return 0;
        };
        var create = typeof(DirectExecutionBackend).GetMethod("CreateExceptionHandlerTrampoline", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stub = (nint)create.Invoke(backend, [Marshal.GetFunctionPointerForDelegate(callback)])!;
        Assert.NotEqual(0, stub);
        try
        {
            // Decode both emitted paths independently of byte-prefix comments.
            var bytes = new ReadOnlySpan<byte>((void*)stub, 2048).ToArray();
            var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes));
            var exchanges = new List<Instruction>();
            while (decoder.IP < (ulong)bytes.Length)
            {
                var instruction = decoder.Decode();
                if (instruction.Mnemonic == Mnemonic.Cmpxchg && instruction.HasLockPrefix) exchanges.Add(instruction);
            }
            Assert.Equal(2, exchanges.Count);
            Assert.All(exchanges, instruction =>
            {
                Assert.Equal(Register.R9, instruction.MemoryBase);
                Assert.Equal(Register.R10, instruction.Op1Register);
            });
            native = Marshal.GetDelegateForFunctionPointer<Handler>(stub);
            var record = stackalloc byte[48];
            new Span<byte>(record, 48).Clear();
            *(uint*)record = 0x80000003;
            var pointers = stackalloc nint[2];
            pointers[0] = (nint)record;
            pointers[1] = 0;
            Assert.Equal(0, native((nint)pointers));
            Assert.Equal(2, calls);
            Assert.Equal(new[] { 1, 2 }, observedDepths);
            Assert.Equal(0L, Marshal.ReadInt64(lockStorage));
            Assert.Equal(0, Marshal.ReadInt32(lockStorage, 8));
            Assert.Equal(0x80000003u, *(uint*)record);
            GC.KeepAlive(callback);
        }
        finally
        {
            Assert.True(HostMemory.Free((void*)stub, 0, HostMemory.MEM_RELEASE));
            Marshal.FreeHGlobal(lockStorage);
        }
    }
}
