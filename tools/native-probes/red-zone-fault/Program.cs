// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;

// Isolated Windows experiment. Neither SharpEmu nor managed code runs in the
// exception handler. The native handler snapshots the faulting stack before
// its first call, unprotects one owned page, and requests instruction retry.
unsafe class Program
{
    [DllImport("kernel32.dll")] static extern nint VirtualAlloc(nint p, nuint n, uint type, uint protect);
    [DllImport("kernel32.dll")] static extern bool VirtualFree(nint p, nuint n, uint type);
    [DllImport("kernel32.dll")] static extern bool VirtualProtect(nint p, nuint n, uint protect, out uint old);
    [DllImport("kernel32.dll")] static extern nint AddVectoredExceptionHandler(uint first, nint handler);
    [DllImport("kernel32.dll")] static extern uint RemoveVectoredExceptionHandler(nint handler);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(nint process, nint p, nuint n);
    static void Require(bool ok) { if (!ok) throw new InvalidOperationException("Native operation failed"); }

    static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Windows x64 only");
        nint page = VirtualAlloc(0, 4096, 0x3000, 4);
        nint state = VirtualAlloc(0, 4096, 0x3000, 4);
        nint code = VirtualAlloc(0, 4096, 0x3000, 4);
        nint veh = 0;
        Require(page != 0 && state != 0 && code != 0);
        try
        {
            var bytes = new List<byte>();
            void Emit(params byte[] b) => bytes.AddRange(b);
            void U64(ulong v) => bytes.AddRange(BitConverter.GetBytes(v));
            void U32(uint v) => bytes.AddRange(BitConverter.GetBytes(v));
            // rcx=EXCEPTION_POINTERS. Only handle write AV at our exact page.
            Emit(0x48,0x8b,0x01, 0x81,0x38); U32(0xc0000005);
            Emit(0x0f,0x85); int notAv = bytes.Count; U32(0);
            Emit(0x83,0x78,0x18,0x02, 0x0f,0x82); int tooShort = bytes.Count; U32(0);
            Emit(0x48,0x83,0x78,0x20,0x01, 0x0f,0x85); int notWrite = bytes.Count; U32(0);
            Emit(0x49,0xb9); U64((ulong)page);
            Emit(0x4c,0x39,0x48,0x28, 0x0f,0x85); int notPage = bytes.Count; U32(0);
            Emit(0x48,0x8b,0x41,0x08, 0x4c,0x8b,0x90); U32(152); // r10=Context.Rsp
            Emit(0x49,0xb9); U64((ulong)state);
            Emit(0x49,0xff,0x01); // increment handler entries
            Emit(0x4d,0x89,0x91); U32(400); // record actual fault-context RSP
            Emit(0x4d,0x03,0x91); U32(408); // optional guarded-store RSP bias
            for (int i=0; i<16; i++)
            {
                Emit(0x49,0x8b,0x42,unchecked((byte)(-8*(i+1))));
                Emit(0x49,0x89,0x81); U32((uint)(16+i*8));
            }
            // No push/call has occurred before the snapshot above.
            Emit(0x48,0x83,0xec,0x28, 0x48,0xb9); U64((ulong)page);
            Emit(0xba); U32(4096);
            Emit(0x41,0xb8); U32(4);
            Emit(0x49,0xb9); U64((ulong)state+8); // old protection
            nint kernel = NativeLibrary.Load("kernel32.dll");
            Emit(0x48,0xb8); U64((ulong)NativeLibrary.GetExport(kernel,"VirtualProtect"));
            Emit(0xff,0xd0, 0x48,0x83,0xc4,0x28);
            // If protection failed, let the exception continue searching.
            Emit(0x85,0xc0,0x74,0x06,0xb8,0xff,0xff,0xff,0xff,0xc3);
            int pass = bytes.Count; Emit(0x31,0xc0,0xc3);
            foreach (int j in new[]{notAv,tooShort,notWrite,notPage})
                BitConverter.GetBytes(pass-(j+4)).CopyToPatch(bytes,j);
            byte[] handler = bytes.ToArray();
            Marshal.Copy(handler,0,code,handler.Length);
            var rows = new List<object>();
            const ulong marker = 0x1122334455667700;
            bool reproduced = false;
            foreach (string mode in new[]{"red-zone","reserved-frame","guarded-store"}) foreach(bool fault in new[]{false,true})
            {
                bool reserve=mode=="reserved-frame", guard=mode=="guarded-store";
                bytes.Clear();
                if(reserve) { Emit(0x48,0x81,0xec); U32(128); }
                for(int i=0;i<16;i++)
                {
                    Emit(0x48,0xb8); U64(marker+(uint)i);
                    Emit(0x48,0x89,0x44,0x24,unchecked((byte)(reserve?i*8:-8*(i+1))));
                }
                // Guard only the store; leaf locals remain in the SysV red zone.
                // LEA leaves flags untouched. This is a probe, not a decoder or
                // a general runtime patch for arbitrary guest instructions.
                if(guard) Emit(0x48,0x8d,0xa4,0x24,0x80,0xff,0xff,0xff);
                Emit(0xc7,0x01); U32(0x12345678); // the sole potentially faulting store
                if(guard) Emit(0x48,0x8d,0xa4,0x24,0x80,0,0,0);
                for(int i=0;i<16;i++)
                {
                    Emit(0x48,0x8b,0x44,0x24,unchecked((byte)(reserve?i*8:-8*(i+1))));
                    Emit(0x48,0x89,0x42,(byte)(i*8));
                }
                if(reserve) { Emit(0x48,0x81,0xc4); U32(128); }
                Emit(0xc3);
                Require(VirtualProtect(code,4096,4,out _));
                byte[] leaf=bytes.ToArray(); Marshal.Copy(leaf,0,code+1024,leaf.Length);
                Require(VirtualProtect(code,4096,0x20,out _));
                Require(FlushInstructionCache(-1,code,4096));
                if(veh==0) { veh=AddVectoredExceptionHandler(1,code); Require(veh!=0); }
                new Span<byte>((void*)state,4096).Clear();
                *(ulong*)(state+408)=guard?128ul:0ul;
                Require(VirtualProtect(page,4096,fault?2u:4u,out _));
                ulong* result=(ulong*)(state+256);
                ((delegate* unmanaged<nint,nint,void>)(code+1024))(page,(nint)result);
                var changed = Enumerable.Range(0,16).Where(i=>result[i]!=marker+(uint)i).Select(i=>new{
                    Offset=reserve?i*8:-8*(i+1), Expected=$"{marker+(uint)i:X16}", Actual=$"{result[i]:X16}",
                    AtHandlerEntry=fault&&!reserve?$"{((ulong*)(state+16))[i]:X16}":null }).ToArray();
                if(fault && mode=="red-zone")
                {
                    reproduced=changed.Length>0 && Enumerable.Range(0,16)
                        .All(i=>result[i]==((ulong*)(state+16))[i]);
                }
                rows.Add(new { Mode=mode, FaultEnabled=fault, HandlerEntries=*(ulong*)state,
                    StoreCompleted=*(uint*)page==0x12345678, ChangedSlots=changed });
                Require(*(ulong*)state==(fault?1ul:0ul) && *(uint*)page==0x12345678);
                if(reserve || guard || !fault) Require(changed.Length==0);
            }
            Require(reproduced);
            string json=JsonSerializer.Serialize(new{Scope="Windows x64 native VEH only; no emulator handler",
                Os=RuntimeInformation.OSDescription, Runtime=RuntimeInformation.FrameworkDescription,
                AssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))),
                NativeHandlerSha256=Convert.ToHexString(SHA256.HashData(handler)),
                CorruptionAlreadyPresentBeforeHandlerCall=reproduced, Rows=rows},new JsonSerializerOptions{WriteIndented=true});
            Console.WriteLine(json);
            if(args.Length==1) File.WriteAllText(args[0],json);
            return 0;
        }
        finally
        {
            if(veh!=0) RemoveVectoredExceptionHandler(veh);
            VirtualFree(code,0,0x8000); VirtualFree(state,0,0x8000); VirtualFree(page,0,0x8000);
        }
    }
}

static class PatchExtensions
{
    public static void CopyToPatch(this byte[] value,List<byte> bytes,int offset)
    { for(int i=0;i<value.Length;i++) bytes[offset+i]=value[i]; }
}
