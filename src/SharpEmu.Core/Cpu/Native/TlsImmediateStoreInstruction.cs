// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;

namespace SharpEmu.Core.Cpu.Native;

internal readonly record struct TlsImmediateStoreInstruction(int Length, bool Is64Bit, int Displacement, int Immediate)
{
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out TlsImmediateStoreInstruction instruction)
    {
        instruction = default;
        if (bytes.Length < 12 || bytes[0] != 0x64) { return false; }
        var offset = 1;
        var wide = false;
        if (bytes[offset] is 0x40 or 0x48) { wide = bytes[offset++] == 0x48; }
        if (bytes.Length < offset + 11 || bytes[offset] != 0xC7 ||
            bytes[offset + 1] != 0x04 || bytes[offset + 2] != 0x25) { return false; }
        instruction = new(offset + 11, wide,
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 3, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 7, 4)));
        return true;
    }

    public byte[]? CreateHelperBytes(ulong address, ulong handlerAddress)
    {
        var relative = (Int128)handlerAddress - ((Int128)address + 6);
        if (relative < int.MinValue || relative > int.MaxValue) { return null; }
        var code = new byte[32];
        Array.Fill(code, (byte)0x90);
        code[0] = 0x50;
        code[1] = 0xE8;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(2), (int)relative);
        var offset = 6;
        if (Is64Bit) { code[offset++] = 0x48; }
        code[offset++] = 0xC7;
        code[offset++] = 0x80;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(offset), Displacement);
        offset += 4;
        // REX.W C7 sign-extends imm32 to 64 bits; preserve that opcode semantics.
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(offset), Immediate);
        offset += 4;
        code[offset++] = 0x58;
        code[offset] = 0xC3;
        return code;
    }
}
