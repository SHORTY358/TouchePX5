// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;

static unsafe class ConcurrentProbe
{
    const int Rounds=200, Writers=4, Readers=2, StoresPerRound=32;
    const ulong Marker=0x1122334455667700;
    static void Check(bool value,string detail) { if(!value) throw new Exception(detail); }
    static Task Dedicated(Action action) => Task.Factory.StartNew(action,CancellationToken.None,
        TaskCreationOptions.LongRunning,TaskScheduler.Default);

    public static int Run(string? output)
    {
        Check(RuntimeInformation.ProcessArchitecture==Architecture.X64,"Native leaf requires x64");
        Check(WindowsWriteWatch.Requested,"Select write-watch before starting this process");
        ulong address=(ulong)HostMemory.Alloc(null,65536,0x3000,4);
        ulong code=(ulong)HostMemory.Alloc(null,4096,0x3000,4);
        Check(address!=0 && code!=0,"Allocation failed");
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var phases=new Barrier(Writers+Readers+1);
        var workers=new List<Task>();
        var results=new nint[Writers];
        int completedStores=0;
        int[] overlappingPollWindows=new int[Readers];
        Stopwatch elapsed=Stopwatch.StartNew();
        bool workersFinished=false;
        try
        {
            // Windows argument registers, SysV-style leaf storage below RSP.
            // No guest/runtime exception handler or instruction adaptation.
            var bytes=new List<byte>();
            void Emit(params byte[] b)=>bytes.AddRange(b);
            for(int i=0;i<16;i++)
            {
                Emit(0x48,0xb8); bytes.AddRange(BitConverter.GetBytes(Marker+(uint)i));
                Emit(0x48,0x89,0x44,0x24,unchecked((byte)(-8*(i+1))));
            }
            Emit(0xc7,0x01,0x78,0x56,0x34,0x12);
            for(int i=0;i<16;i++)
            {
                Emit(0x48,0x8b,0x44,0x24,unchecked((byte)(-8*(i+1))));
                Emit(0x48,0x89,0x42,(byte)(i*8));
            }
            Emit(0xc3);
            Marshal.Copy(bytes.ToArray(),0,(nint)code,bytes.Count);
            Check(HostMemory.Protect((void*)code,4096,0x20,out _),"Code protection failed");
            HostMemory.FlushInstructionCache((void*)code,4096);
            ulong[] owners=[address,address+64,address+4096+64];
            GuestImageWriteTracker.Track(owners[0],8192);
            GuestImageWriteTracker.Track(owners[1],64);
            GuestImageWriteTracker.Track(owners[2],64);
            long[] previous=new long[owners.Length];
            foreach(ulong owner in owners) Check(!GuestImageWriteTracker.ConsumeDirty(owner),"Baseline dirty");
            for(int worker=0;worker<Writers;worker++)
            {
                int index=worker;
                results[index]=(nint)NativeMemory.Alloc(128);
                Check(results[index]!=0,"Result allocation failed");
                workers.Add(Dedicated(()=>
                {
                    try
                    {
                        ulong target=address+(ulong)(index%2)*4096+128+(ulong)index*4;
                        for(int round=0;round<Rounds;round++)
                        {
                            phases.SignalAndWait(stop.Token);
                            for(int store=0;store<StoresPerRound;store++)
                            {
                                ((delegate* unmanaged<nint,nint,void>)code)((nint)target,results[index]);
                                for(int slot=0;slot<16;slot++)
                                    Check(((ulong*)results[index])[slot]==Marker+(uint)slot,$"Writer {index} red-zone slot {slot} corrupted");
                                Interlocked.Increment(ref completedStores);
                                Thread.Yield();
                            }
                            phases.SignalAndWait(stop.Token);
                        }
                    }
                    catch { stop.Cancel(); throw; }
                }));
            }
            for(int reader=0;reader<Readers;reader++)
            {
                int readerIndex=reader;
                workers.Add(Dedicated(()=>
                {
                    try
                    {
                        long[] seen=new long[owners.Length];
                        for(int round=0;round<Rounds;round++)
                        {
                            phases.SignalAndWait(stop.Token);
                            for(int poll=0;poll<16;poll++)
                            {
                                int before=Volatile.Read(ref completedStores);
                                _=GuestImageWriteTracker.DirtyEpoch;
                                if(Volatile.Read(ref completedStores)>before)
                                    overlappingPollWindows[readerIndex]++;
                                for(int i=0;i<owners.Length;i++)
                                {
                                    Check(GuestImageWriteTracker.TryGetWriteGeneration(owners[i],out long generation),"Owner disappeared during writes");
                                    Check(generation>=seen[i],"Write generation regressed");
                                    seen[i]=generation;
                                    _=GuestImageWriteTracker.PeekDirty(owners[i]);
                                }
                            }
                            phases.SignalAndWait(stop.Token);
                        }
                    }
                    catch { stop.Cancel(); throw; }
                }));
            }
            for(int round=0;round<Rounds;round++)
            {
                phases.SignalAndWait(stop.Token);
                phases.SignalAndWait(stop.Token);
                // Writers are now parked until the next round. No consuming
                // reader can steal the dirty flag used for these assertions.
                for(int i=0;i<owners.Length;i++)
                {
                    Check(GuestImageWriteTracker.ConsumeDirty(owners[i],out var spans,out long generation),$"Round {round} missed owner {i}");
                    Check(spans is null && generation>previous[i],"Generation/page-granularity contract failed");
                    previous[i]=generation;
                    Check(!GuestImageWriteTracker.ConsumeDirty(owners[i]),"Consumed owner stayed dirty without a new write");
                }
            }
            Task.WaitAll(workers.ToArray(),stop.Token);
            workersFinished=true;
            Check(completedStores==Writers*Rounds*StoresPerRound,"Native store accounting failed");
            Check(overlappingPollWindows.Sum()>0,"No native write completed during a polling window; concurrency not demonstrated");
            for(int i=0;i<Writers;i++)
                Check(*(uint*)(address+(ulong)(i%2)*4096+128+(ulong)i*4)==0x12345678,"Writer output missing");

            // Native writers have joined. Only a managed poller may race free;
            // no test intentionally writes into released memory.
            using var pollStop=new CancellationTokenSource();
            using var polling=new ManualResetEventSlim();
            int polls=0;
            Task releasePoller=Dedicated(()=>
            {
                while(!pollStop.IsCancellationRequested)
                {
                    _=GuestImageWriteTracker.DirtyEpoch;
                    foreach(ulong owner in owners) _=GuestImageWriteTracker.TryGetWriteGeneration(owner,out _);
                    if(Interlocked.Increment(ref polls)>=100) polling.Set();
                }
            });
            try
            {
                Check(polling.Wait(TimeSpan.FromSeconds(5)),"Release poller didn't start");
                Check(HostMemory.Free((void*)address,0,0x8000),"Release failed");
                address=0;
                foreach(ulong owner in owners) Check(!GuestImageWriteTracker.TryGetWriteGeneration(owner,out _),"Released owner remains tracked");
            }
            finally
            {
                pollStop.Cancel();
                Check(releasePoller.Wait(TimeSpan.FromSeconds(5)),"Release poller didn't stop");
            }
            string json=JsonSerializer.Serialize(new{
                Scope="Windows host worker threads executing native leaves with real HLE write-watch tracker; not guest thread scheduler or GPU",
                Passed=true,WriterThreads=Writers,PollingThreads=Readers,Rounds,
                Os=RuntimeInformation.OSDescription,
                RuntimeHleSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GuestImageWriteTracker).Assembly.Location))),
                NativeLeafSha256=Convert.ToHexString(SHA256.HashData(bytes.ToArray())),
                NativeStores=Writers*Rounds*StoresPerRound,RedZoneMarkerChecks=Writers*Rounds*StoresPerRound*16,
                OwnerChecks=Rounds*owners.Length,FinalGenerations=previous,
                NativeWriteOverlappingPollWindows=overlappingPollWindows,
                PollReleaseChecks=polls,ElapsedMilliseconds=elapsed.ElapsedMilliseconds
            },new JsonSerializerOptions{WriteIndented=true});
            Console.WriteLine(json); if(output!=null) File.WriteAllText(output,json);
            return 0;
        }
        finally
        {
            stop.Cancel();
            // Never free backing pages while a native writer could still run.
            if(!workersFinished)
                try { workersFinished=Task.WaitAll(workers.ToArray(),TimeSpan.FromSeconds(5)); }
                catch(AggregateException) { workersFinished=workers.All(t=>t.IsCompleted); }
            if(workersFinished)
            {
                if(address!=0) HostMemory.Free((void*)address,0,0x8000);
                HostMemory.Free((void*)code,0,0x8000);
                foreach(nint p in results) if(p!=0) NativeMemory.Free((void*)p);
            }
        }
    }
}
