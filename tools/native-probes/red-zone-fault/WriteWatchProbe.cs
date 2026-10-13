// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;

// No emulator integration. Explore allocation semantics and native red-zone
// preservation with Windows write watch before selecting a tracker backend.
static unsafe class WriteWatchProbe
{
    [DllImport("kernel32.dll",SetLastError=true)] static extern nint VirtualAlloc(nint p,nuint n,uint flags,uint protection);
    [DllImport("kernel32.dll")] static extern bool VirtualFree(nint p,nuint n,uint flags);
    [DllImport("kernel32.dll")] static extern bool VirtualProtect(nint p,nuint n,uint flags,out uint old);
    [DllImport("kernel32.dll")] static extern uint GetWriteWatch(uint flags,nint p,nuint n,nint* addresses,ref nuint count,out uint granularity);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(nint process,nint p,nuint n);
    static void Require(bool value,string detail) { if(!value) throw new InvalidOperationException(detail); }
    static (uint Status, ulong[] Offsets, uint PageSize) Poll(nint memory,nuint size,bool reset)
    {
        // Only three committed pages can be written in the large reservation.
        nint* pages=stackalloc nint[16]; nuint count=16;
        uint status=GetWriteWatch(reset?1u:0u,memory,size,pages,ref count,out uint granularity);
        Require(count<=16,"Unexpected result capacity");
        ulong[] offsets=status==0 ? new ReadOnlySpan<nint>(pages,(int)count).ToArray()
            .Select(p=>(ulong)(p-memory)).Order().ToArray() : [];
        return(status,offsets,granularity);
    }

    public static int Run(string? output)
    {
        if(!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture!=Architecture.X64)
            throw new PlatformNotSupportedException("Windows x64 only");
        const nuint largeSize=1u<<30;
        const ulong marker=0xA1B2C3D4E5F60700;
        nint ordinary=0, watched=0, sparse=0, code=0;
        try
        {
            ordinary=VirtualAlloc(0,4096,0x3000,4);
            watched=VirtualAlloc(0,65536,0x203000,4);
            sparse=VirtualAlloc(0,largeSize,0x202000,4); // reserve + WRITE_WATCH only
            code=VirtualAlloc(0,4096,0x3000,4);
            Require(ordinary!=0 && watched!=0 && sparse!=0 && code!=0,"Allocation failed");
            var unsupported=Poll(ordinary,4096,false);
            Require(unsupported.Status!=0,"Ordinary allocation unexpectedly supports write watch");
            nint upgrade=VirtualAlloc(ordinary,4096,0x201000,4); // commit + WRITE_WATCH, no reserve
            int upgradeError=Marshal.GetLastWin32Error();
            var afterUpgrade=Poll(ordinary,4096,false);
            Require(afterUpgrade.Status!=0,"Ordinary region was unexpectedly upgraded");
            var bytes=new List<byte>();
            void Emit(params byte[] b)=>bytes.AddRange(b);
            for(int i=0;i<16;i++)
            {
                Emit(0x48,0xb8); bytes.AddRange(BitConverter.GetBytes(marker+(uint)i));
                Emit(0x48,0x89,0x44,0x24,unchecked((byte)(-8*(i+1))));
            }
            Emit(0xc7,0x01,0x78,0x56,0x34,0x12);
            for(int i=0;i<16;i++)
            {
                Emit(0x48,0x8b,0x44,0x24,unchecked((byte)(-8*(i+1))));
                Emit(0x48,0x89,0x42,(byte)(i*8));
            }
            Emit(0xc3);
            Marshal.Copy(bytes.ToArray(),0,code,bytes.Count);
            Require(VirtualProtect(code,4096,0x20,out _),"Code protection failed");
            Require(FlushInstructionCache(-1,code,4096),"Code flush failed");
            ulong* result=stackalloc ulong[16];
            void Store(nint p)
            {
                ((delegate* unmanaged<nint,nint,void>)code)(p,(nint)result);
                Require(*(uint*)p==0x12345678,"Native store failed");
                for(int i=0;i<16;i++) Require(result[i]==marker+(uint)i,$"Red-zone slot {i} changed");
            }
            var clean=Poll(watched,65536,true);
            Require(clean.Status==0 && clean.Offsets.Length==0,"New watched region not clean");
            Store(watched+3*4096+4);
            var first=Poll(watched,65536,false);
            var repeated=Poll(watched,65536,false);
            var reset=Poll(watched,65536,true);
            var empty=Poll(watched,65536,true);
            Require(first.Status==0 && first.PageSize==4096 && first.Offsets.SequenceEqual(new[]{12288ul}),"First native write missing");
            Require(repeated.Offsets.SequenceEqual(first.Offsets) && reset.Offsets.SequenceEqual(first.Offsets),"Non-consuming/reset poll mismatch");
            Require(empty.Status==0 && empty.Offsets.Length==0,"Reset did not consume state");
            Store(watched+3*4096+4); // identical content still counts as a new write
            var sameValue=Poll(watched,65536,true);
            Require(sameValue.Status==0 && sameValue.Offsets.SequenceEqual(first.Offsets),"Repeated same-value store missed");
            var rounds=new List<object>();
            for(int round=0;round<100;round++)
            {
                int page=round%16; Store(watched+page*4096+4);
                var poll=Poll(watched,65536,true);
                Require(poll.Status==0 && poll.Offsets.SequenceEqual(new[]{(ulong)page*4096}),"Repeated store/poll failed");
                rounds.Add(new{Round=round,Page=page,PreservedMarkerCount=16});
            }
            ulong[] sparseOffsets=[0,5*4096,largeSize-4096];
            foreach(ulong offset in sparseOffsets)
            {
                nint address=sparse+(nint)offset;
                Require(VirtualAlloc(address,4096,0x1000,4)==address,"Sparse page commit failed");
                Store(address+4);
            }
            var sparsePoll=Poll(sparse,largeSize,true);
            Require(sparsePoll.Status==0 && sparsePoll.Offsets.SequenceEqual(sparseOffsets),"Reserve-then-commit tracking failed");
            // A caller resetting a shared page consumes state for all aliases.
            Store(watched+4);
            var aliasA=Poll(watched,4096,true);
            var aliasB=Poll(watched,4096,true);
            Require(aliasA.Status==0 && aliasA.Offsets.SequenceEqual(new[]{0ul}) && aliasB.Status==0 && aliasB.Offsets.Length==0,"Shared-page reset control changed");
            string json=JsonSerializer.Serialize(new{
                Scope="Windows native write-watch only; no emulator tracker or GPU",
                Os=RuntimeInformation.OSDescription, Passed=true, NativeMarkerChecksPerStore=16,
                RepeatedStorePollCycles=rounds.Count, OrdinaryAllocationStatus=unsupported.Status,
                UpgradeReturnedNull=upgrade==0,UpgradeLastError=upgradeError,AfterUpgradeStatus=afterUpgrade.Status,
                FirstWritePageOffsets=first.Offsets, SameValueWriteDetected=true,
                SparseReservedBytes=(ulong)largeSize,SparseCommittedBytes=3*4096,
                SparseDirtyOffsets=sparsePoll.Offsets, SharedResetConsumesOtherAlias=true
            },new JsonSerializerOptions{WriteIndented=true});
            Console.WriteLine(json); if(output!=null) File.WriteAllText(output,json);
            return 0;
        }
        finally
        {
            foreach(nint p in new[]{ordinary,watched,sparse,code}) if(p!=0) VirtualFree(p,0,0x8000);
        }
    }
}
