// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

internal static class NativeBspCompute
{
    public static void Consume(string directory)
    {
        using var proof=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"gpu-result.json")));
        if(!proof.RootElement.GetProperty("NumericalOutputsVerified").GetBoolean()) throw new InvalidOperationException("Completed numerical proof required");
        foreach(var file in proof.RootElement.GetProperty("Hashes").EnumerateObject())
            if(Hash(Path.Combine(directory,file.Name))!=file.Value.GetString()) throw new InvalidOperationException("GPU proof changed: "+file.Name);
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"fixture-report.json")));
        var meta=fixture.RootElement;
        var actual=JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(directory,"actual.json")))!;
        var indices=meta.GetProperty("OriginalIndices").Deserialize<uint[]>()!;
        var vertices=meta.GetProperty("UniqueVertexCount").GetUInt32();
        var slots=meta.GetProperty("OutputSlots").GetUInt32();
        var first=meta.GetProperty("PrimitiveFirstWord").GetInt32();
        if(vertices is <3 or >4 || slots!=128 || first!=1536 || indices.Length is not (3 or 6))
            throw new InvalidOperationException("Unexpected native BSP bounded mesh");
        var remapped=new List<uint>();
        for(var i=0;i<indices.Length/3;i++)
        {
            if(!Gen5NggTriangleRemap.TryRemap(actual.AsSpan(first+i*5,5),vertices,0,slots,out var triangle) ||
                triangle!=(indices[i*3],indices[i*3+1],indices[i*3+2]))
                throw new InvalidOperationException("Completed native BSP primitive connectivity differs");
            remapped.Add(triangle.Item1);remapped.Add(triangle.Item2);remapped.Add(triangle.Item3);
        }
        Write(directory,"remapped-indices.json",remapped);
        Write(directory,"remap-report.json",new{ActualSha256=Hash(Path.Combine(directory,"actual.json")),
            OriginalIndexCount=indices.Length,UniqueVertexCount=vertices,ConnectivityVerified=true,NativeRenderingVerified=false});
        Console.WriteLine($"PASS consumed native captured topology: {vertices} unique vertices, {indices.Length} indices");
    }
    public static void Create(string runRoot, string manifestPath, string output)
    {
        runRoot=Path.GetFullPath(runRoot);output=Path.GetFullPath(output);
        using var run=JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot,"execution-report.json")));
        var report=run.RootElement;
        if(!report.GetProperty("NativeRendererInvoked").GetBoolean() || !report.GetProperty("CaptureInputs").GetBoolean() ||
            report.GetProperty("GameAssetsUsed").GetBoolean() || report.TryGetProperty("EvidenceHashError",out _))
            throw new InvalidOperationException("Hash-checked synthetic native renderer capture required");
        foreach(var file in report.GetProperty("EvidenceFiles").EnumerateArray())
            if(Hash(Path.Combine(runRoot,file.GetProperty("Path").GetString()!))!=file.GetProperty("Sha256").GetString())
                throw new InvalidOperationException("Native evidence hash changed");
        var build=report.GetProperty("NativeBuild");
        if(build.GetProperty("SourcePin").GetString()!="be0731b4fcc4233349bba5e0d3ff33c79428f695")
            throw new InvalidOperationException("Unexpected native source pin");
        var app=build.GetProperty("App").GetString()!;
        var mapPath=Path.Combine(app,"map.ps5bsp");
        if(Hash(mapPath)!=build.GetProperty("AppFiles").EnumerateArray().Single(f=>f.GetProperty("Path").GetString()=="map.ps5bsp").GetProperty("Sha256").GetString())
            throw new InvalidOperationException("Native map changed since build");
        var map=File.ReadAllBytes(mapPath);
        var original=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!,"bsp_textured.gs.bin"));
        if(Convert.ToHexString(SHA256.HashData(original))!="6B15DB4A9C9316F80D7DBDAB67DCB66D0FB9410CDA45C4E1608D62B668BE714E")
            throw new InvalidOperationException("Only unchanged pinned BSP GS is supported");
        using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));
        if(!manifest.RootElement.GetProperty("stages").GetProperty("pre_raster_gs").GetProperty("sha256").GetString()!.Equals(
            Convert.ToHexString(SHA256.HashData(original)),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Original manifest mismatch");
        for(var ordinal=1;ordinal<=2;ordinal++)
        {
            var replay=Path.Combine(runRoot,$"replay/draw-{ordinal:D6}/0000000210001000.replay.json");
            using var envelope=JsonDocument.Parse(File.ReadAllText(replay));
            var snapshot=envelope.RootElement.GetProperty("Snapshot").Deserialize<Gen5ShaderReplaySnapshot>()!;
            var draw=envelope.RootElement.GetProperty("DrawContext").Deserialize<Gen5ShaderReplayDrawContext>()!;
            if(snapshot.Schema!=1 || snapshot.UserDataScalarRegisterBase!=8 || snapshot.ImageBindings.Length!=0 ||
                snapshot.GlobalMemoryBindings.Any(b=>b.Writable) || !draw.Indexed || draw.IndexStrideBytes!=2 ||
                draw.IndexCaptureStatus!="captured" || draw.InstanceCount!=1 || draw.PrimitiveType!=4 || draw.BaseVertex!=0 ||
                draw.DrawCount!=(ordinal==1?3u:6u) || draw.DrawSequence!=(ulong)ordinal || draw.IndexData?.Length!=draw.DrawCount*2)
                throw new InvalidOperationException("Unexpected bounded indexed native draw contract");
            var indices=Enumerable.Range(0,(int)draw.DrawCount).Select(i=>(uint)BinaryPrimitives.ReadUInt16LittleEndian(draw.IndexData.AsSpan(i*2))).ToArray();
            if(!indices.SequenceEqual(ordinal==1?new uint[]{0,1,2}:new uint[]{0,1,2,0,2,3}))
                throw new InvalidOperationException("Native topology differs from synthetic map/clear");
            if(!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ReadOnlyShaderMemory(snapshot.ShaderAddress,original),Generation.Gen5),
                snapshot.ShaderAddress,out var program,out var error)) throw new InvalidOperationException(error);
            if(program!.Instructions.Count!=snapshot.Instructions.Length) throw new InvalidOperationException("Native decode extent differs");
            foreach(var instruction in snapshot.Instructions)
                for(var w=0;w<instruction.Words.Length;w++)
                    if(BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(checked((int)instruction.Pc+w*4)))!=instruction.Words[w])
                        throw new InvalidOperationException("Captured shader word differs from original");
            var views=snapshot.VertexInputs;
            if(views.Length!=2 || views[0].Pc!=0x78 || views[1].Pc!=0x80 || views.Any(v=>v.Stride!=32 || v.PerInstance) ||
                views[0].BaseAddress!=views[1].BaseAddress || views[0].OffsetBytes!=0 || views[1].OffsetBytes!=16 ||
                !views[0].Data.AsSpan(0,views[0].DataLength).SequenceEqual(views[1].Data.AsSpan(0,views[1].DataLength)))
                throw new InvalidOperationException("Original 32-byte BSP vertex stream required");
            var vertexCount=ordinal==1?3:4;
            if(views[0].DataLength!=vertexCount*32) throw new InvalidOperationException("Native stream extent differs");
            if(ordinal==2 && !views[0].Data.AsSpan(0,128).SequenceEqual(Chunk(map,"VERT",32)))
                throw new InvalidOperationException("Captured world vertices differ from baked map");
            if(ordinal==2 && !draw.IndexData.AsSpan().SequenceEqual(Chunk(map,"INDX",2)))
                throw new InvalidOperationException("Captured world indices differ from baked map");
            if(ordinal==1)
            {
                float[][] positions=[[-1,-1,0.999f],[3,-1,0.999f],[-1,3,0.999f]];
                for(var v=0;v<3;v++) for(var c=0;c<8;c++)
                    if(BinaryPrimitives.ReadUInt32LittleEndian(views[0].Data.AsSpan(v*32+c*4))!=Bits(c<3?positions[v][c]:0))
                        throw new InvalidOperationException("Captured clear geometry differs from upstream source");
                for(var i=0;i<16;i++) if(snapshot.InitialScalarRegisters[9+i]!=Bits(i%5==0?1:0))
                    throw new InvalidOperationException("Unexpected native clear matrix");
            }
            var vertices=Enumerable.Range(0,vertexCount).Select(i=>new Gen5NggSourceVertex((uint)i,0)).ToArray();
            var triangles=Enumerable.Range(0,indices.Length/3).Select(i=>new Gen5NggSourceTriangle((uint)i,indices[i*3],indices[i*3+1],indices[i*3+2])).ToArray();
            var batch=Gen5NggPassthroughBatch.Create([(new((uint)vertexCount,(uint)triangles.Length,32,128,0),vertices,triangles)]);
            var slots=batch.OutputSlotCapacity;
            var primitiveFirst=(int)slots*12;var descriptorFirst=primitiveFirst+(int)slots*5;
            var sourceFirst=descriptorFirst+4;
            var auxiliary=Enumerable.Repeat(0xCAFEBABEu,sourceFirst+batch.SourceWords.Length+16).ToArray();
            batch.Descriptors.Span.CopyTo(auxiliary.AsSpan(descriptorFirst));
            batch.SourceWords.Span.CopyTo(auxiliary.AsSpan(sourceFirst));
            var expected=(uint[])auxiliary.Clone();var floats=new List<int>();
            for(var v=0;v<vertexCount;v++)
            {
                // Independent original GLSL equation using retained native
                // matrix/stream bytes. No shader interpretation or GPU output.
                for(var row=0;row<4;row++)
                {
                    double sum=Float(snapshot.InitialScalarRegisters[21+row]);
                    for(var column=0;column<3;column++)
                        sum+=(double)Float(snapshot.InitialScalarRegisters[9+column*4+row])*
                            Float(BinaryPrimitives.ReadUInt32LittleEndian(views[0].Data.AsSpan(v*32+column*4)));
                    if(!double.IsFinite(sum)) throw new InvalidOperationException("Nonfinite native MVP");
                    expected[v*12+row]=Bits((float)sum);floats.Add(v*12+row);
                }
                for(var c=0;c<4;c++)
                {
                    expected[v*12+4+c]=BinaryPrimitives.ReadUInt32LittleEndian(views[0].Data.AsSpan(v*32+(3+c)*4));
                    expected[v*12+8+c]=snapshot.InitialScalarRegisters[25+c];
                }
            }
            for(var i=0;i<triangles.Length;i++)
            {
                var a=indices[i*3];var b=indices[i*3+1];var c=indices[i*3+2];
                new uint[]{a|(b<<10)|(c<<20),a,b,c,1}.CopyTo(expected,primitiveFirst+i*5);
            }
            var bindings=snapshot.GlobalMemoryBindings.ToList();
            foreach(var view in views)
                foreach(var pc in new[]{view.Pc}.Concat(view.AliasPcs??[]).Distinct())
                {
                    var control=(Gen5BufferMemoryControl)program.Instructions.Single(i=>i.Pc==pc).Control!;
                    bindings.Add(new(control.ScalarResource,view.BaseAddress,[pc],view.Data,view.DataLength,false){WriteBackToGuest=false});
                }
            var outputBinding=bindings.Count;
            bindings.Add(new(0,0x300000000,[],Bytes(auxiliary),auxiliary.Length*4,false){Writable=true,WriteBackToGuest=false});
            var state=new Gen5ShaderState(program,snapshot.UserData,snapshot.Metadata,snapshot.ComputeSystemRegisters,snapshot.UserDataScalarRegisterBase);
            var evaluation=new Gen5ShaderEvaluation(snapshot.InitialScalarRegisters,snapshot.ScalarRegisters,[],bindings,
                snapshot.EvaluationComputeSystemRegisters,snapshot.RuntimeScalarRegisters?.ToHashSet(),views);
            if(!Gen5SpirvTranslator.TryCompileComputeShader(state,evaluation,128,1,1,out var compiled,out error,
                totalGlobalBufferCount:bindings.Count,exportOutput:new(outputBinding,slots,2),triangleOutput:new(outputBinding,(uint)primitiveFirst,slots,0),
                nggBatchInputs:new(outputBinding,(uint)descriptorFirst,outputBinding,(uint)sourceFirst,1,128,32)))
                throw new InvalidOperationException(error);
            var dir=Path.Combine(output,ordinal==1?"native-clear":"native-world");Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir,"original-bsp-compute.spv"),compiled.Spirv);
            Write(dir,"expected.json",expected);Write(dir,"initial.json",auxiliary);Write(dir,"limits.json",new uint[]{slots,1,1});
            Write(dir,"storage-fixture.json",new{Buffers=bindings.Select(b=>Words(b.Data,b.DataLength)).ToArray(),OutputBinding=outputBinding,GroupsX=1,
                FloatWords=floats,FloatTolerance=0.00002});
            Write(dir,"fixture-report.json",new{
                Scope="Captured original native indexed BSP inputs, independent GLSL export reference, explicit shared-vertex audit subgroup; no native rendering or hardware scheduler proof",
                NativeRun=runRoot,ReplaySha256=Hash(replay),RunReportSha256=Hash(Path.Combine(runRoot,"execution-report.json")),
                OriginalGsSha256=Convert.ToHexString(SHA256.HashData(original)),MapSha256=Hash(mapPath),DrawContext=draw,
                UniqueVertexCount=vertexCount,PrimitiveCount=triangles.Length,OriginalIndices=indices,
                OutputSlots=slots,PrimitiveFirstWord=primitiveFirst,SourceFirstWord=sourceFirst,OutputBinding=outputBinding,
                CapturedOriginalInputs=true,GpuExecuted=false,NativeRenderingVerified=false});
            Console.WriteLine($"Prepared captured indexed BSP GS: {dir}, {vertexCount} unique vertices / {triangles.Length} primitives");
        }
    }
    static ReadOnlySpan<byte> Chunk(byte[] map,string name,int stride)
    {
        if(map.Length<64 || Encoding.ASCII.GetString(map,0,6)!="PS5BSP" || BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(8))!=3)
            throw new InvalidOperationException("Expected BSP v3 bundle");
        var count=BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(48));
        var headerBytes=BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(12));
        if(count>64 || 64+count*32>headerBytes || headerBytes>map.Length)
            throw new InvalidOperationException("Invalid BSP directory extent");
        // The directory starts at the fixed 64-byte upstream header.
        for(uint i=0;i<count;i++)
        {
            var header=map.AsSpan(checked(64+(int)i*32),32);
            if(Encoding.ASCII.GetString(header[..4])!=name) continue;
            var offset=BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            var bytes=BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            var records=BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            if(BinaryPrimitives.ReadUInt32LittleEndian(header[16..])!=stride || bytes!=records*stride)
                throw new InvalidOperationException("Unexpected BSP chunk stride");
            return map.AsSpan(checked((int)offset),checked((int)bytes));
        }
        throw new InvalidOperationException("Missing BSP chunk "+name);
    }
    static uint Bits(float v)=>BitConverter.SingleToUInt32Bits(v);
    static float Float(uint v)=>BitConverter.UInt32BitsToSingle(v);
    static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static uint[] Words(byte[] b,int length)=>Enumerable.Range(0,length/4).Select(i=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i*4))).ToArray();
    static byte[] Bytes(uint[] w){var b=new byte[w.Length*4];for(var i=0;i<w.Length;i++)BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i*4),w[i]);return b;}
    static void Write(string d,string f,object value)=>File.WriteAllText(Path.Combine(d,f),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
}
