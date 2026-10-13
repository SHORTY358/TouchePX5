// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers;
using System.Runtime.InteropServices;

namespace SharpEmu.HLE;

/// <summary>Experimental Windows allocation-time write tracking. No page protection.</summary>
public static unsafe class WindowsWriteWatch
{
    public static bool Requested { get; } = OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("SHARPEMU_GUEST_IMAGE_CPU_SYNC") == "write-watch";
    public const uint AllocationFlag = 0x200000;
    private const ulong MaxQueryBytes = 128UL << 20;
    [DllImport("kernel32.dll")]
    private static extern uint GetWriteWatch(uint flags,nint address,nuint bytes,
        nint* pages,ref nuint count,out uint granularity);

    /// <summary>One query/reset operation. Caller fans results out to all aliases.</summary>
    internal static bool TryConsume(ulong address,ulong byteCount,out ulong[] pages)
    {
        pages=[];
        if(!Requested || byteCount==0 || byteCount>MaxQueryBytes) return false;
        int capacity=checked((int)((byteCount+4095)/4096));
        nint[] buffer=ArrayPool<nint>.Shared.Rent(capacity);
        try
        {
            nuint count=(nuint)capacity;
            fixed(nint* native=buffer)
            {
                uint status=GetWriteWatch(1,(nint)address,(nuint)byteCount,native,ref count,out uint granularity);
                if(status!=0 || count>(nuint)capacity || granularity!=4096) return false;
                if(count==0) return true;
                pages=new ulong[(int)count];
                for(int i=0;i<pages.Length;i++) pages[i]=(ulong)native[i];
                return true;
            }
        }
        finally { ArrayPool<nint>.Shared.Return(buffer); }
    }
}
