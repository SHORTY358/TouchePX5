// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.Libs.Gpu;
using SharpEmu.ShaderCompiler;

internal static class BspGraphics
{
    public static void Run(string manifestPath,string computeRoot,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        const ulong target=0x400100000,shaderAddress=0x20001000,tableAddress=0x24000000;
        const uint width=640,height=360;
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_IMAGES","0");
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_WRITES",$"0x{target:X}");
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_WRITE_ORDINAL","1:2");
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_WRITE_TRIGGER_FILE",null);
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_IMAGE_ADDRS",$"0x{target:X}");
        Environment.SetEnvironmentVariable("SHARPEMU_GUEST_IMAGE_DUMP_DIR",output);
        Environment.SetEnvironmentVariable("SHARPEMU_OVERLAY","0");
        using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));
        var original=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!,"bsp_textured.ps.bin"));
        var hash=Convert.ToHexString(SHA256.HashData(original));
        if(hash!="B0079DE8518FD561DEE3FE3243C0F1D668FB69075855558C3C6254D0B4DD0351" ||
            !hash.Equals(manifest.RootElement.GetProperty("stages").GetProperty("pixel").GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Original BSP PS hash mismatch");
        if(!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ReadOnlyShaderMemory(shaderAddress,original),Generation.Gen5),shaderAddress,out var program,out var error)) throw new InvalidOperationException(error);
        byte[] basePixels=[200,80,40,255, 30,210,90,0, 60,100,230,255, 220,190,50,255];
        byte[] lightPixels=[128,255,64,255, 255,64,192,255, 80,160,240,255, 192,96,128,255];
        uint[] sampler=[2u|(2u<<3)|(2u<<6),0,0,0];
        uint[] Descriptor(ulong address) => [(uint)(address>>8),(uint)(address>>40)|(56u<<20)|(1u<<30),1u<<14,0x90000FAC,1,0,0,0];
        var resources=new[] {Descriptor(0x25000000),Descriptor(0x26000000)};
        var table=resources[0].Concat(sampler).Concat(resources[1]).Concat(sampler).ToArray();
        var registers=new uint[256];registers[1]=(uint)tableAddress;registers[126]=registers[127]=uint.MaxValue;
        var bindings=program.Instructions.Where(i=>i.Control is Gen5ImageControl).Select((i,index)=>
            new Gen5ImageBinding(i.Pc,i.Opcode,(Gen5ImageControl)i.Control!,resources[index],sampler,0)).ToArray();
        if(bindings.Length!=2) throw new InvalidOperationException("Expected original base and lightmap sampling sites");
        var globals=new Gen5GlobalMemoryBinding[] {new(6,tableAddress,[0x3C,0x44,0x94,0x9C],Bytes(table),table.Length*4,false) {WriteBackToGuest=false}};
        var gpu=GuestGpu.Current;
        if(!gpu.TryCompilePixelShader(new(program,registers.Take(2).ToArray(),null),new(registers,registers,bindings,globals),
            [new(0,0,Gen5PixelOutputKind.Float)],out var pixel,out error,totalGlobalBufferCount:1,
            pixelInputEnable:1,pixelInputAddress:1,pixelInputCntl:[0,1])) throw new InvalidOperationException(error);
        var textures=new GuestDrawTexture[] {
            new(0x25000000,2,2,10,0,basePixels,false,false,Pitch:2,Sampler:new(sampler[0],sampler[1],sampler[2],sampler[3])),
            new(0x26000000,2,2,10,0,lightPixels,false,false,Pitch:2,Sampler:new(sampler[0],sampler[1],sampler[2],sampler[3]))
        };
        gpu.EnsureStarted(width,height);
        var draws=new List<object>();
        foreach(var name in new[] {"background","identity"})
        {
            var directory=Path.Combine(Path.GetFullPath(computeRoot),name);
            BspComputeFixture.Consume(directory); // checks retained GPU proof and input hashes
            var actual=JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(directory,"actual.json")))!;
            using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"fixture-report.json")));
            var slots=fixture.RootElement.GetProperty("OutputSlots").GetUInt32();
            var vertices=Bytes(actual.Take(checked((int)slots*12)).ToArray());
            var indices=JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(directory,"remapped-indices.json")))!;
            var instructions=new List<Gen5ShaderInstruction>();var views=new List<Gen5VertexInputBinding>();
            for(uint location=0;location<3;location++)
            {
                instructions.Add(new(location*4,Gen5ShaderEncoding.Mubuf,"BufferLoadFormatXyzw",[],[],[],new Gen5BufferMemoryControl(4,0,location*4,0,0,true,false,false,false)));
                views.Add(new(location*4,location,4,14,7,0x27000000,48,location*16,vertices,vertices.Length,false));
                var first=location*4;
                instructions.Add(new(12+location*4,Gen5ShaderEncoding.Exp,"Exp",[],[Gen5Operand.Vector(first),Gen5Operand.Vector(first+1),Gen5Operand.Vector(first+2),Gen5Operand.Vector(first+3)],[],new Gen5ExportControl(location==0 ? 12u : 31+location,15,false,false,false)));
            }
            instructions.Sort((a,b)=>a.Pc.CompareTo(b.Pc));
            instructions.Add(new(24,Gen5ShaderEncoding.Sopp,"SEndpgm",[],[],[],null));
            if(!gpu.TryCompileVertexShader(new(new(0,instructions),[],null),new(new uint[256],new uint[256],[],[],VertexInputs:views),out var vertex,out error,totalGlobalBufferCount:1,requiredVertexOutputCount:2)) throw new InvalidOperationException(error);
            gpu.SubmitOffscreenTranslatedDraw(pixel!,textures,[new(tableAddress,Bytes(table),table.Length*4,false)],2,
                [new(target,width,height,10,0)],vertex!,vertexCount:(uint)indices.Length,
                indexBuffer:new(Bytes(indices),indices.Length*4,true,false),
                vertexBuffers:views.Select(v=>new GuestVertexBuffer(v.Location,v.ComponentCount,v.DataFormat,v.NumberFormat,v.BaseAddress,v.Stride,v.OffsetBytes,vertices,vertices.Length,false)).ToArray(),
                renderState:GuestRenderState.Default with {Viewport=new(0,0,width,height,0,1)},shaderAddress:shaderAddress);
            draws.Add(new {Case=name,CompletedGsSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"actual.json")))),
                FixtureSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"fixture-report.json")))),
                GpuProofSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"gpu-result.json"))))});
        }
        var sequence=gpu.SubmitOrderedGuestAction(()=>{},"BSP graphics audit completion",GuestGpuWriteBackMode.None);
        if(sequence==0 || !gpu.WaitForGuestWork(sequence,20000)) throw new InvalidOperationException("BSP graphics completion deadline");
        var captures=Directory.GetFiles(output,$"*-0x{target:X16}-*.rgba").OrderBy(f=>f,StringComparer.Ordinal).ToArray();
        if(captures.Length<2) throw new InvalidOperationException("Need background and textured target readbacks");
        File.WriteAllText(Path.Combine(output,"graphics-report.json"),JsonSerializer.Serialize(new {
            Scope="Original PS consumes independently verified original GS outputs through transport-only vertex bridge; source-derived inputs, not native title execution",
            OriginalPsSha256=hash,Draws=draws,Width=width,Height=height,BasePixels=basePixels,LightPixels=lightPixels,
            Captures=captures.Select(f=>new {File=f,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}).ToArray(),
            GpuDrawsCompleted=true,PixelFidelityVerified=false,NativeTitleExecuted=false
        },new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("Captured original BSP textured graphics: "+captures[^1]);
        Environment.Exit(0);
    }
    private static byte[] Bytes(uint[] words)
    {
        var bytes=new byte[words.Length*4];
        for(var i=0;i<words.Length;i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i*4),words[i]);
        return bytes;
    }
}
