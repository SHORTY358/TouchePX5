// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using SharpEmu.Core.Cpu.Native;
using Xunit;

namespace SharpEmu.Libs.Tests;

public sealed class NativeReturnAddressTests
{
    [Fact]
    public void LowPayloadCode_IsAcceptedWhileNonExecutableAndInvalidAddressesAreRejected()
    {
        // This backend's direct Windows execution is the affected path.
        if (!OperatingSystem.IsWindows()) return;
        var code = VirtualAlloc((nint)0x400000, 0x2000, 0x3000, 0x40);
        Assert.Equal((nint)0x400000, code);
        try
        {
            Assert.True(VirtualProtect(code + 0x1000, 0x1000, 0x04, out _));
            Assert.True(DirectExecutionBackend.IsLikelyReturnAddress(0x400080));
            Assert.False(DirectExecutionBackend.IsLikelyReturnAddress(0x401080));
            Assert.False(DirectExecutionBackend.IsLikelyReturnAddress(0));
            Assert.False(DirectExecutionBackend.IsLikelyReturnAddress(0xFFFE));
            Assert.False(DirectExecutionBackend.IsLikelyReturnAddress(0x0000800000000000));
            Assert.False(DirectExecutionBackend.IsLikelyReturnAddress(ulong.MaxValue));
        }
        finally { Assert.True(VirtualFree(code, 0, 0x8000)); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(nint address, nuint size, uint protection, out uint oldProtection);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(nint address, nuint size, uint freeType);
}
