// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using System.Runtime.InteropServices;

unsafe class Program
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static int Main()
    {
        Check(WindowsWriteWatch.Requested,"Run a fresh process with SHARPEMU_GUEST_IMAGE_CPU_SYNC=write-watch");
        ulong address=(ulong)HostMemory.Alloc(null,65536,0x3000,4);
        Check(address!=0,"Allocation failed");
        try
        {
            *(uint*)address=7;
            GuestImageWriteTracker.Track(address,8192);
            GuestImageWriteTracker.Track(address+64,64);
            Check(!GuestImageWriteTracker.ConsumeDirty(address),"Baseline marked dirty");
            Check(HostMemory.Query((void*)address,out var info)!=0 && info.Protect==4,"Tracking made pages read-only");
            *(uint*)(address+128)=9; // native pointer write, no NotifyManagedWrite
            Check(GuestImageWriteTracker.PeekDirty(address+64),"Overlapping alias missed write");
            Check(GuestImageWriteTracker.ConsumeDirty(address+64,out var spans,out var genA) && spans is null,"Native page write falsely reported precise spans");
            Check(GuestImageWriteTracker.ConsumeDirty(address,out _,out var genB),"First owner consumed second owner's dirtiness");
            Check(genA>0 && genB>0,"Generations missing");
            Check(!GuestImageWriteTracker.ConsumeDirty(address+64) && !GuestImageWriteTracker.ConsumeDirty(address),"Dirty flags didn't consume");
            for(int i=0;i<100;i++)
            {
                *(uint*)(address+128)=9; // same bytes, new store
                long epoch=GuestImageWriteTracker.DirtyEpoch;
                Check(GuestImageWriteTracker.TryGetWriteGeneration(address,out long next) && next>genB,"Write generation stalled");
                Check(GuestImageWriteTracker.ConsumeDirty(address) && GuestImageWriteTracker.ConsumeDirty(address+64),"Alias fanout failed");
                Check(epoch>0,"Dirty epoch not polled"); genB=next;
            }
            *(uint*)(address+4096)=3;
            Check(GuestImageWriteTracker.ConsumeDirty(address),"Second page missed");
            Check(!GuestImageWriteTracker.ConsumeDirty(address+64),"Non-overlapping page dirtied alias");
            *(uint*)(address+128)=10;
            GuestImageWriteTracker.Track(address+256,64);
            Check(!GuestImageWriteTracker.ConsumeDirty(address+256),"New alias inherited pre-registration writes");
            Check(GuestImageWriteTracker.ConsumeDirty(address) && GuestImageWriteTracker.ConsumeDirty(address+64),"New registration hid an existing owner's write");
            Check(HostMemory.Query((void*)address,out info)!=0 && info.Protect==4,"Pages became protected");
            Check(!HostMemory.Free((void*)(address+4096),0,0x8000),"Invalid release unexpectedly succeeded");
            Check(GuestImageWriteTracker.TryGetWriteGeneration(address,out _),"Failed release discarded live tracking");
            Check(HostMemory.Free((void*)address,0,0x8000),"Valid release failed");
            Check(!GuestImageWriteTracker.TryGetWriteGeneration(address,out _) &&
                  !GuestImageWriteTracker.TryGetWriteGeneration(address+64,out _) &&
                  !GuestImageWriteTracker.TryGetWriteGeneration(address+256,out _),"Released allocation retained an alias");
            address=0;
            Console.WriteLine("PASS: write-watch tracker; writable pages, baseline, 100 generations, shared-page fanout, disjoint-page control, registration and release ownership");
            return 0;
        }
        finally
        {
            if(address!=0)
            {
                GuestImageWriteTracker.Untrack(address); GuestImageWriteTracker.Untrack(address+64);
                GuestImageWriteTracker.Untrack(address+256);
                HostMemory.Free((void*)address,0,0x8000);
            }
        }
    }
}
