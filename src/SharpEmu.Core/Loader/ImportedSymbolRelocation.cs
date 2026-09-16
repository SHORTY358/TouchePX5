// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.Core.Memory;

namespace SharpEmu.Core.Loader;

public readonly record struct ImportedSymbolRelocation(
    ulong TargetAddress,
    long Addend,
    string Nid,
    bool IsData,
    int WriteSize = sizeof(ulong),
    bool IsSigned = false)
{
    public bool TryWriteResolvedValue(IVirtualMemory memory, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        switch (WriteSize)
        {
            case sizeof(ulong):
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
                break;
            case sizeof(uint):
                if (IsSigned)
                {
                    var signed = unchecked((long)value);
                    if (signed is < int.MinValue or > int.MaxValue) { return false; }
                    BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)signed);
                }
                else
                {
                    if (value > uint.MaxValue) { return false; }
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
                }
                break;
            default:
                return false;
        }
        return memory.TryWrite(TargetAddress, bytes[..WriteSize]);
    }
}
