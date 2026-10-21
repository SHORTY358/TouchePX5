// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.Libs.Agc;
using SharpEmu.Libs.Gpu;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

internal static class NativeBspTextures
{
    public static void Run(string computeRoot,string output)
    {
        computeRoot=Path.GetFullPath(computeRoot);output=Path.GetFullPath(output);
        foreach(var name in new[]{"native-clear","native-world"}) NativeBspCompute.Consume(Path.Combine(computeRoot,name));
        using var fixture=Read(Path.Combine(computeRoot,"native-world/fixture-report.json"));
        var runRoot=fixture.RootElement.GetProperty("NativeRun").GetString()!;
        var reportPath=Path.Combine(runRoot,"execution-report.json");
        if(Hash(reportPath)!=fixture.RootElement.GetProperty("RunReportSha256").GetString()) throw new InvalidOperationException("Native run identity changed");
        using var report=Read(reportPath);
        foreach(var file in report.RootElement.GetProperty("EvidenceFiles").EnumerateArray())
            if(Hash(Path.Combine(runRoot,file.GetProperty("Path").GetString()!))!=file.GetProperty("Sha256").GetString()) throw new InvalidOperationException("Native input changed");
        var mapPath=Path.Combine(report.RootElement.GetProperty("NativeBuild").GetProperty("App").GetString()!,"map.ps5bsp");
        if(Hash(mapPath)!=fixture.RootElement.GetProperty("MapSha256").GetString()) throw new InvalidOperationException("Source bundle changed");
        var map=File.ReadAllBytes(mapPath);var memory=new ReadOnlyShaderMemory(0x200000000,map);
        var replayPath=Path.Combine(runRoot,"replay/draw-000002/0000000210001200.replay.json");
        using var replay=Read(replayPath);
        var snapshot=replay.RootElement.GetProperty("Snapshot").Deserialize<Gen5ShaderReplaySnapshot>()!;
        var draw=replay.RootElement.GetProperty("DrawContext").Deserialize<Gen5ShaderReplayDrawContext>()!;
        var inputRoot=report.RootElement.GetProperty("NativeBuild").GetProperty("InputRoot").GetString()!;
        var original=File.ReadAllBytes(Path.Combine(inputRoot,"shaders/bsp_textured.ps.bin"));
        if(Convert.ToHexString(SHA256.HashData(original))!="B0079DE8518FD561DEE3FE3243C0F1D668FB69075855558C3C6254D0B4DD0351") throw new InvalidOperationException("Original native BSP PS required");
        foreach(var ins in snapshot.Instructions) for(var w=0;w<ins.Words.Length;w++)
            if(BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(checked((int)ins.Pc+w*4)))!=ins.Words[w]) throw new InvalidOperationException("Native PS word differs");
        if(!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ReadOnlyShaderMemory(snapshot.ShaderAddress,original),Generation.Gen5),snapshot.ShaderAddress,out var program,out var error)) throw new InvalidOperationException(error);
        if(program!.Instructions.Count!=snapshot.Instructions.Length || snapshot.ImageBindings.Length!=2) throw new InvalidOperationException("Unexpected native PS coverage/resources");
        var textures=new List<GuestDrawTexture>();var levels=new List<GuestTextureMip[]>();var descriptors=new List<Gen5ImageDescriptor>();
        foreach(var binding in snapshot.ImageBindings)
        {
            if(!Gen5ImageDescriptor.TryDecode(binding.ResourceDescriptor,out var d,out error) || d.DataFormat!=10 || d.NumberFormat!=0 ||
                d.ResourceType!=9 || d.SwizzleMode!=0 || d.BaseLevel!=0 || d.DstSelect!=0xFAC || d.Depth!=1) throw new InvalidOperationException("Unexpected native texture descriptor: "+error);
            GuestTextureMip[] mips;
            if(d.LastLevel>0)
            {
                if(!AgcExports.TryReadLinearMipChain(memory,d.BaseAddress,d.Width,d.Height,d.Pitch,d.LastLevel+1,10,out mips)) throw new InvalidOperationException("Native source bundle mip chain unreadable");
            }
            else
            {
                var pixels=new byte[checked((int)(d.Pitch*d.Height*4))];
                if(!memory.TryRead(d.BaseAddress,pixels)) throw new InvalidOperationException("Native source lightmap unreadable");
                mips=[new(d.Width,d.Height,d.Pitch,pixels)];
            }
            var s=binding.SamplerDescriptor;
            textures.Add(new(d.BaseAddress,d.Width,d.Height,10,0,mips[0].Pixels,false,false,
                MipLevels:d.LastLevel+1,ResourceMipLevels:d.LastLevel+1,Pitch:mips[0].Pitch,DstSelect:d.DstSelect,
                Sampler:new(s[0],s[1],s[2],s[3]),AdditionalMips:mips[1..]));
            levels.Add(mips);descriptors.Add(d);
        }
        if(descriptors[0].Width!=64 || descriptors[0].Height!=64 || levels[0].Length!=7 ||
            descriptors[1].Width!=64 || descriptors[1].Height!=7 || levels[1].Length!=1) throw new InvalidOperationException("Unexpected synthetic base/lightmap extents");
        int ChunkOffset(string name)
        {
            var count=BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(48));
            for(var i=0;i<count;i++)
            {
                var header=map.AsSpan(64+i*32,32);
                if(System.Text.Encoding.ASCII.GetString(header[..4])==name) return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]));
            }
            throw new InvalidOperationException("Missing original bundle chunk "+name);
        }
        var textureMeta=ChunkOffset("TEXM");var texturePixels=ChunkOffset("TEXP");var lightPixels=ChunkOffset("LMPX");
        var textureOffset=BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(textureMeta));
        if(descriptors[0].BaseAddress!=0x200000000UL+(uint)texturePixels+textureOffset ||
            descriptors[1].BaseAddress!=0x200000000UL+(uint)lightPixels) throw new InvalidOperationException("Native descriptor addresses differ from original bundle chunks");
        // Independent tiny_bsp palette equation validates ALL base texels,
        // rather than sharing the production mip reader's output as oracle.
        byte[] BaseReference(int x,int y)
        {
            var index=(y*64+x)&255;
            return [(byte)index,(byte)((index+1)&255),(byte)(255-index),(byte)(index==255?0:255)];
        }
        for(var y=0;y<64;y++) for(var x=0;x<64;x++)
            if(!levels[0][0].Pixels.AsSpan(checked((int)((y*levels[0][0].Pitch+x)*4)),4).SequenceEqual(BaseReference(x,y)))
                throw new InvalidOperationException("Production base-level transfer differs from independent source palette");
        const ulong target=0x400200000;const uint width=640,height=360;
        Directory.CreateDirectory(output);
        foreach(var (key,value) in new[]{("SHARPEMU_TRACE_GUEST_IMAGES","0"),("SHARPEMU_TRACE_GUEST_WRITES",$"0x{target:X}"),
            ("SHARPEMU_TRACE_GUEST_WRITE_ORDINAL","1:2"),("SHARPEMU_TRACE_GUEST_IMAGE_ADDRS",$"0x{target:X}"),
            ("SHARPEMU_GUEST_IMAGE_DUMP_DIR",output),("SHARPEMU_OVERLAY","0")}) Environment.SetEnvironmentVariable(key,value);
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_WRITE_TRIGGER_FILE",null);
        var gpu=GuestGpu.Current;
        var globals=snapshot.GlobalMemoryBindings;
        if(!gpu.TryCompilePixelShader(new(program,snapshot.UserData,snapshot.Metadata,snapshot.ComputeSystemRegisters,snapshot.UserDataScalarRegisterBase),
            new(snapshot.InitialScalarRegisters,snapshot.ScalarRegisters,snapshot.ImageBindings,globals,snapshot.EvaluationComputeSystemRegisters),
            [new(0,0,Gen5PixelOutputKind.Float)],out var pixel,out error,totalGlobalBufferCount:globals.Length,
            pixelInputEnable:draw.PixelInputEnable,pixelInputAddress:draw.PixelInputAddress,pixelInputCntl:draw.PixelInputControl)) throw new InvalidOperationException(error);
        var clear=JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(computeRoot,"native-clear/actual.json")))!;
        var world=JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(computeRoot,"native-world/actual.json")))!;
        var cells=new List<object>();var tileWords=new List<uint>();var expected=new List<byte[]>();
        byte[] Texel(int t,int x,int y)=>t==0?BaseReference(x,y):map.AsSpan(lightPixels+(y*64+x)*4,4).ToArray();
        var clearColor=new byte[]{ToByte(clear[8]),ToByte(clear[9]),ToByte(clear[10]),255};
        for(var cell=0;cell<16;cell++)
        {
            var bx=cell==15?63:1+(cell%4)*16;var by=cell==15?3:5+(cell/4)*12;
            var lx=1+cell%5;var ly=1+cell/5;
            float[] uv=[(bx+0.5f)/64+(cell==14?1:0),(by+0.5f)/64,(lx+0.5f)/64,(ly+0.5f)/7];
            var b=Texel(0,bx,by);var l=Texel(1,lx,ly);
            var reference=b[3]<128?clearColor.ToArray():new byte[]{(byte)Math.Round(b[0]*l[0]/255d),(byte)Math.Round(b[1]*l[1]/255d),(byte)Math.Round(b[2]*l[2]/255d),255};
            expected.Add(reference);
            var x0=-1+cell%4*0.5f;var x1=x0+0.5f;var y0=-1+cell/4*0.5f;var y1=y0+0.5f;
            foreach(var xy in new[]{(x0,y0),(x1,y0),(x1,y1),(x0,y0),(x1,y1),(x0,y1)})
                tileWords.AddRange(new[]{Bits(xy.Item1),Bits(xy.Item2),Bits(0.5f),Bits(1)}.Concat(uv.Select(Bits)).Concat(world.Skip(8).Take(4)));
            cells.Add(new{Cell=cell,BaseX=bx,BaseY=by,LightX=lx,LightY=ly,UV=uv,Base=b,Light=l,Expected=reference,Discard=b[3]<128});
        }
        gpu.EnsureStarted(width,height);
        void Submit(uint[] words)
        {
            var bytes=Bytes(words);var instructions=new List<Gen5ShaderInstruction>();var views=new List<Gen5VertexInputBinding>();
            for(uint location=0;location<3;location++)
            {
                instructions.Add(new(location*4,Gen5ShaderEncoding.Mubuf,"BufferLoadFormatXyzw",[],[],[],new Gen5BufferMemoryControl(4,0,location*4,0,0,true,false,false,false)));
                views.Add(new(location*4,location,4,14,7,0x27000000,48,location*16,bytes,bytes.Length,false));
                var first=location*4;
                instructions.Add(new(12+location*4,Gen5ShaderEncoding.Exp,"Exp",[],[Gen5Operand.Vector(first),Gen5Operand.Vector(first+1),Gen5Operand.Vector(first+2),Gen5Operand.Vector(first+3)],[],new Gen5ExportControl(location==0?12u:31+location,15,false,false,false)));
            }
            instructions.Sort((a,b)=>a.Pc.CompareTo(b.Pc));instructions.Add(new(24,Gen5ShaderEncoding.Sopp,"SEndpgm",[],[],[],null));
            if(!gpu.TryCompileVertexShader(new(new(0,instructions),[],null),new(new uint[256],new uint[256],[],[],VertexInputs:views),out var vs,out error,totalGlobalBufferCount:globals.Length,requiredVertexOutputCount:2)) throw new InvalidOperationException(error);
            gpu.SubmitOffscreenTranslatedDraw(pixel!,textures,globals.Select(g=>new GuestMemoryBuffer(g.BaseAddress,g.Data,g.DataLength,false)).ToArray(),2,
                [new(target,width,height,10,0)],vs!,vertexCount:(uint)words.Length/12,
                vertexBuffers:views.Select(v=>new GuestVertexBuffer(v.Location,v.ComponentCount,v.DataFormat,v.NumberFormat,v.BaseAddress,v.Stride,v.OffsetBytes,bytes,bytes.Length,false)).ToArray(),
                renderState:GuestRenderState.Default with{Viewport=new(0,0,width,height,0,1)},shaderAddress:snapshot.ShaderAddress);
        }
        Submit(clear.Take(36).ToArray());Submit(tileWords.ToArray());
        var sequence=gpu.SubmitOrderedGuestAction(()=>{},"native BSP texture qualification",GuestGpuWriteBackMode.None);
        if(sequence<=0 || !gpu.WaitForGuestWork(sequence,20000)) throw new InvalidOperationException("Native texture diagnostic completion deadline");
        var captures=Directory.GetFiles(output,$"*-0x{target:X16}-*.rgba").OrderBy(f=>f,StringComparer.Ordinal).ToArray();
        if(captures.Length<2) throw new InvalidOperationException("Both controlled target passes required");
        var actual=File.ReadAllBytes(captures[^1]);var mismatch=0;var worst=0;var checkedPixels=0;
        for(var cell=0;cell<16;cell++) for(var y=cell/4*90+4;y<(cell/4+1)*90-4;y++) for(var x=cell%4*160+4;x<(cell%4+1)*160-4;x++)
        {
            checkedPixels++;var bad=false;
            for(var c=0;c<4;c++){var delta=Math.Abs(actual[(y*640+x)*4+c]-expected[cell][c]);worst=Math.Max(worst,delta);bad|=delta>2;}
            if(bad)mismatch++;
        }
        var missingLightmap=0;var wrongDiscard=0;var swappedChannels=0;
        for(var cell=0;cell<16;cell++)
        {
            var x=cell%4*160+80;var y=cell/4*90+45;var offset=(y*640+x)*4;
            var bx=cell==15?63:1+cell%4*16;var by=cell==15?3:5+cell/4*12;var b=BaseReference(bx,by);
            if(cell<15 && Enumerable.Range(0,3).Any(c=>Math.Abs(actual[offset+c]-b[c])>2)) missingLightmap++;
            if(cell==15 && Enumerable.Range(0,3).Any(c=>actual[offset+c]>2)) wrongDiscard++;
            if(Math.Abs(actual[offset]-expected[cell][2])>2 || Math.Abs(actual[offset+2]-expected[cell][0])>2) swappedChannels++;
        }
        Write(output,"texture-report.json",new{
            Scope="Original native PS, captured shader tables/descriptors/samplers with hash-checked source-bundle texels; constant-UV controlled tiles at base LOD and native clear/control colors; not native scene geometry/rendering",
            NativeRun=runRoot,ComputeRoot=computeRoot,NativePsReplaySha256=Hash(replayPath),OriginalPsSha256=Convert.ToHexString(SHA256.HashData(original)),
            SourceMapSha256=Hash(mapPath),NativePixelInputEnable=draw.PixelInputEnable,NativePixelInputAddress=draw.PixelInputAddress,
            Descriptors=descriptors,MipCounts=levels.Select(l=>l.Length),Cells=cells,ClearColor=clearColor,CheckedInteriorPixels=checkedPixels,
            MismatchedPixels=mismatch,WorstChannelError=worst,Tolerance=2,ConstantUvNativeResourceSamplingVerified=mismatch==0,
            IndependentSourceBaseTexelsVerified=4096,MissingLightmapRejectedCells=missingLightmap,BlackDiscardRejectedCells=wrongDiscard,SwappedRedBlueRejectedCells=swappedChannels,
            FullMipSamplingVerified=false,NativeSceneRenderingVerified=false,Captures=captures.Select(f=>new{File=f,Sha256=Hash(f)})});
        if(mismatch!=0) throw new InvalidOperationException($"Native texture qualification: {mismatch}/{checkedPixels} pixels differ, worst {worst}");
        if(missingLightmap<10 || wrongDiscard!=1 || swappedChannels<10) throw new InvalidOperationException("Native texture negative models did not reject");
        Console.WriteLine($"PASS native original BSP PS/resources: {checkedPixels} controlled interior pixels, worst error {worst}");
        Environment.Exit(0);
    }
    public static void VerifyCapture(string directory)
    {
        directory=Path.GetFullPath(directory);
        using var document=Read(Path.Combine(directory,"texture-report.json"));var report=document.RootElement;
        var captures=report.GetProperty("Captures").EnumerateArray().ToArray();
        var actual=File.ReadAllBytes(captures[^1].GetProperty("File").GetString()!);
        if(actual.Length!=640*360*4) throw new InvalidOperationException("Unexpected native texture target extent");
        var mismatches=0;
        foreach(var cell in report.GetProperty("Cells").EnumerateArray())
        {
            var index=cell.GetProperty("Cell").GetInt32();var expected=cell.GetProperty("Expected").GetBytesFromBase64();
            for(var y=index/4*90+4;y<(index/4+1)*90-4;y++) for(var x=index%4*160+4;x<(index%4+1)*160-4;x++)
                if(Enumerable.Range(0,4).Any(c=>Math.Abs(actual[(y*640+x)*4+c]-expected[c])>2)) mismatches++;
        }
        Write(directory,"pixel-verification.json",new{MismatchedPixels=mismatches,InteriorPixels=199424,ActualSha256=Convert.ToHexString(SHA256.HashData(actual)),
            ReportSha256=Hash(Path.Combine(directory,"texture-report.json")),NativeSceneRenderingVerified=false});
        if(mismatches!=0) throw new InvalidOperationException($"Native texture pixel mismatch: {mismatches}");
        foreach(var capture in captures) if(Hash(capture.GetProperty("File").GetString()!)!=capture.GetProperty("Sha256").GetString()) throw new InvalidOperationException("Native texture capture hash changed");
        Console.WriteLine("PASS retained native texture pixel reference and hashes");
    }
    static JsonDocument Read(string p)=>JsonDocument.Parse(File.ReadAllText(p));
    static uint Bits(float v)=>BitConverter.SingleToUInt32Bits(v);
    static byte ToByte(uint v)=>(byte)Math.Clamp((int)Math.Round(BitConverter.UInt32BitsToSingle(v)*255),0,255);
    static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
    static byte[] Bytes(uint[] w){var b=new byte[w.Length*4];for(var i=0;i<w.Length;i++)BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i*4),w[i]);return b;}
    static void Write(string d,string f,object v)=>File.WriteAllText(Path.Combine(d,f),JsonSerializer.Serialize(v,new JsonSerializerOptions{WriteIndented=true}));
}
