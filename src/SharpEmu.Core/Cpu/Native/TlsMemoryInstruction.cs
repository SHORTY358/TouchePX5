// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;

namespace SharpEmu.Core.Cpu.Native;

internal readonly record struct TlsMemoryInstruction(int Length, int OperandRegister, bool Is64Bit, int Displacement, bool IsStore, bool IsXor = false, bool Is16Bit = false)
{
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out TlsMemoryInstruction instruction)
    {
        instruction = default;
        if (bytes.Length < 8) { return false; }
        var offset = 0;
        var fs = false;
        var narrow = false;
        // Legacy segment and operand-size prefixes may occur in either order.
        while (offset < bytes.Length && bytes[offset] is 0x64 or 0x66)
        {
            if (bytes[offset] == 0x64) { if (fs) { return false; } fs = true; }
            else { if (narrow) { return false; } narrow = true; }
            offset++;
        }
        if (!fs || offset >= bytes.Length) { return false; }
        byte rex = 0;
        if (bytes[offset] is >= 0x40 and <= 0x4F) { rex = bytes[offset++]; }
        // Only absolute FS:[disp32], without an index or base register.
        if (bytes.Length < offset + 7 || (rex & 3) != 0 || bytes[offset] is not (0x8B or 0x89 or 0x33) ||
            (bytes[offset + 1] & 0xC7) != 0x04 || bytes[offset + 2] != 0x25)
        {
            return false;
        }
        var destination = ((bytes[offset + 1] >> 3) & 7) | ((rex & 4) != 0 ? 8 : 0);
        // A call-based patch cannot directly read/write the guest's original RSP.
        if (destination == 4) { return false; }
        instruction = new(offset + 7, destination, (rex & 8) != 0,
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 3, 4)), bytes[offset] == 0x89, bytes[offset] == 0x33,
            narrow && (rex & 8) == 0);
        return true;
    }

    public byte[]? CreateHelperBytes(ulong address, ulong handlerAddress)
    {
        var relative = (Int128)handlerAddress - ((Int128)address + 6);
        if (relative < int.MinValue || relative > int.MaxValue) { return null; }
        var code = new byte[32];
        Array.Fill(code, (byte)0x90);
        code[0] = 0x50; // Save RAX even when it is the destination: keep call-stack alignment.
        code[1] = 0xE8;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(2), (int)relative);
        var offset = 6;
        var register = OperandRegister;
        if ((IsStore || IsXor || Is16Bit) && register == 0)
        {
            // The handler returns the thread pointer in RAX. Recover the guest's
            // saved RAX through RCX, preserving the guest's RCX in turn.
            code[offset++] = 0x51;
            code[offset++] = 0x48;
            code[offset++] = 0x8B;
            code[offset++] = 0x4C;
            code[offset++] = 0x24;
            code[offset++] = 0x08;
            register = 1;
        }
        var rex = (byte)(0x40 | (Is64Bit ? 8 : 0) | (register >= 8 ? 4 : 0));
        if (Is16Bit) { code[offset++] = 0x66; }
        if (rex != 0x40) { code[offset++] = rex; }
        code[offset++] = IsStore ? (byte)0x89 : IsXor ? (byte)0x33 : (byte)0x8B;
        code[offset++] = (byte)(0x80 | ((register & 7) << 3));
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(offset), Displacement);
        offset += 4;
        if (!IsStore && (IsXor || Is16Bit) && OperandRegister == 0)
        {
            // RCX holds the result, including the guest's original upper bits for
            // a 16-bit operation. MOV and POP preserve the arithmetic flags.
            code[offset++] = 0x48;
            code[offset++] = 0x89;
            code[offset++] = 0xC8;
        }
        if ((IsStore || IsXor || Is16Bit) && OperandRegister == 0) { code[offset++] = 0x59; }
        if (!IsStore && OperandRegister == 0)
        {
            // Discard saved RAX without changing the flags or the loaded result.
            code[offset++] = 0x48;
            code[offset++] = 0x8D;
            code[offset++] = 0x64;
            code[offset++] = 0x24;
            code[offset++] = 0x08;
        }
        else { code[offset++] = 0x58; }
        code[offset] = 0xC3;
        return code;
    }
}
