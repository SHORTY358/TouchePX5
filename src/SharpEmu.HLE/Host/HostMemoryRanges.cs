// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.HLE.Host;

public static class HostMemoryRanges
{
    public static bool TryProtect(IHostMemory host, ulong address, ulong size, HostPageProtection protection)
    {
        if (size == 0 || address > ulong.MaxValue - size)
            return false;

        // VirtualProtect cannot cross separate reservations. Preflight the entire
        // committed range before changing permissions on any part of it.
        var end = address + size;
        var chunks = new List<(ulong Address, ulong Size)>();
        for (var cursor = address; cursor < end;)
        {
            if (!host.Query(cursor, out var region) || region.State != HostRegionState.Committed ||
                region.BaseAddress > cursor || region.RegionSize == 0 ||
                region.BaseAddress > ulong.MaxValue - region.RegionSize)
                return false;
            var next = Math.Min(end, region.BaseAddress + region.RegionSize);
            if (next <= cursor)
                return false;
            chunks.Add((cursor, next - cursor));
            cursor = next;
        }

        foreach (var chunk in chunks)
            if (!host.Protect(chunk.Address, chunk.Size, protection, out _))
                return false;
        return true;
    }

}
