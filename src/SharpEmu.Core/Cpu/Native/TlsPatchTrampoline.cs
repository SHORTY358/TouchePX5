// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;

namespace SharpEmu.Core.Cpu.Native;

internal static class TlsPatchTrampoline
{
    public const int PrefixSize = 32;
    public const int Size = PrefixSize + 32;

    public static byte[]? CreatePrefix(ulong address, ulong continuation)
    {
        var relative = (Int128)continuation - ((Int128)address + 26);
        if (relative < int.MinValue || relative > int.MaxValue) { return null; }
        var code = new byte[PrefixSize];
        Array.Fill(code, (byte)0x90);
        // Enter through JMP: even a CALL would overwrite the guest's [RSP-8].
        // Reserve the complete SysV red zone before any helper pushes or calls.
        byte[] reserve = [0x48, 0x8D, 0xA4, 0x24, 0x80, 0xFF, 0xFF, 0xFF];
        reserve.CopyTo(code, 0); // lea rsp, [rsp-128], preserving flags
        code[8] = 0xE8;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(9), PrefixSize - 13);
        byte[] restore = [0x48, 0x8D, 0xA4, 0x24, 0x80, 0, 0, 0];
        restore.CopyTo(code, 13); // lea rsp, [rsp+128]
        code[21] = 0xE9;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(22), (int)relative);
        return code;
    }
}
