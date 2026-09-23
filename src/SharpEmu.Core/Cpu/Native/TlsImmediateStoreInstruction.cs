// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;

namespace SharpEmu.Core.Cpu.Native;

internal readonly record struct TlsImmediateStoreInstruction(int Length, bool Is64Bit, int Displacement, int Immediate, bool Is16Bit = false, bool Is8Bit = false)
{
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out TlsImmediateStoreInstruction instruction)
    {
        instruction = default;
        if (bytes.Length < 9) { return false; }
        var offset = 0;
        var fs = false;
        var narrow = false;
        while (offset < bytes.Length && bytes[offset] is 0x64 or 0x66)
        {
            if (bytes[offset] == 0x64) { if (fs) { return false; } fs = true; }
            else { if (narrow) { return false; } narrow = true; }
            offset++;
        }
        if (!fs || offset >= bytes.Length) { return false; }
        var wide = false;
        if (bytes[offset] is 0x40 or 0x48) { wide = bytes[offset++] == 0x48; }
        // C6 always writes one byte: neither 66 nor REX.W widens it.
        var singleByte = bytes[offset] == 0xC6;
        wide &= !singleByte;
        narrow &= !wide && !singleByte;
        var immediateSize = singleByte ? 1 : narrow ? 2 : 4;
        if (bytes.Length < offset + 7 + immediateSize || bytes[offset] is not (0xC6 or 0xC7) ||
            bytes[offset + 1] != 0x04 || bytes[offset + 2] != 0x25) { return false; }
        instruction = new(offset + 7 + immediateSize, wide,
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 3, 4)),
            singleByte ? bytes[offset + 7] : narrow ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset + 7, 2)) :
                BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 7, 4)), narrow, singleByte);
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
        if (Is16Bit) { code[offset++] = 0x66; }
        if (Is64Bit) { code[offset++] = 0x48; }
        code[offset++] = Is8Bit ? (byte)0xC6 : (byte)0xC7;
        code[offset++] = 0x80;
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(offset), Displacement);
        offset += 4;
        // REX.W C7 sign-extends imm32 to 64 bits; preserve that opcode semantics.
        if (Is8Bit)
        {
            code[offset++] = (byte)Immediate;
        }
        else if (Is16Bit)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(offset), (ushort)Immediate);
            offset += 2;
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(offset), Immediate);
            offset += 4;
        }
        code[offset++] = 0x58;
        code[offset] = 0xC3;
        return code;
    }
}
