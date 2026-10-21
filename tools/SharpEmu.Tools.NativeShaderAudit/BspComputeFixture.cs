// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

internal static class BspComputeFixture
{
    public static void Consume(string directory)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"fixture-report.json")));
        using var execution=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"gpu-result.json")));
        if(!execution.RootElement.GetProperty("NumericalOutputsVerified").GetBoolean()) throw new InvalidOperationException("GPU proof required");
        foreach(var file in new[] {"actual.json","expected.json","original-bsp-compute.spv","fixture-report.json","storage-fixture.json","initial.json","limits.json"})
            if(execution.RootElement.GetProperty("Hashes").GetProperty(file).GetString()!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,file)))))
                throw new InvalidOperationException("Completed GPU proof hash differs: "+file);
        var actual=JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(directory,"actual.json")))!;
        var count=fixture.RootElement.GetProperty("VertexCount").GetInt32();
        var slots=fixture.RootElement.GetProperty("OutputSlots").GetUInt32();
        var first=fixture.RootElement.GetProperty("PrimitiveFirstWord").GetInt32();
        if(count is < 3 or > 132 || count%3!=0 || first!=(int)slots*12 || actual.Length<first+(int)slots*5)
            throw new InvalidOperationException("Unexpected bounded BSP transport extent");
        var indices=new List<uint>();
        for(var groupFirst=0;groupFirst<count;groupFirst+=126)
        {
            var live=(uint)Math.Min(126,count-groupFirst);
            var outputBase=(uint)(groupFirst/126*128);
            for(uint local=0;local<live;local+=3)
            {
                var record=first+(int)(outputBase+local/3)*5;
                if(!Gen5NggTriangleRemap.TryRemap(actual.AsSpan(record,5),live,outputBase,slots,out var remapped) ||
                    remapped!=(outputBase+local,outputBase+local+1,outputBase+local+2))
                    throw new InvalidOperationException("Wrong completed BSP primitive connectivity");
                indices.Add(remapped.Item1);indices.Add(remapped.Item2);indices.Add(remapped.Item3);
            }
        }
        Write(directory,"remapped-indices.json",indices);
        Write(directory,"remap-report.json",new {ConnectivityRemapVerified=true,IndexCount=indices.Count,RenderingVerified=false,
            ActualSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"actual.json"))))});
        Console.WriteLine($"PASS consumed {indices.Count} checked BSP indices from completed original GS output");
    }

    public static void Create(string manifestPath, string output)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(manifestPath)!, "bsp_textured.gs.bin"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (hash != "6B15DB4A9C9316F80D7DBDAB67DCB66D0FB9410CDA45C4E1608D62B668BE714E" ||
            !hash.Equals(manifest.RootElement.GetProperty("stages").GetProperty("pre_raster_gs").GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture requires the unchanged pinned BSP GS");
        const ulong shaderAddress = 0x20000000, tableAddress = 0x21000000, streamAddress = 0x22000000;
        if (!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ReadOnlyShaderMemory(shaderAddress, bytes), Generation.Gen5),
            shaderAddress, out var program, out var error)) throw new InvalidOperationException(error);
        // Source-derived inputs, NOT captured title state. Original GLSL declares
        // a column-major mat4, a vec4 control, and 32-byte position/baseUV/lightUV.
        float[][] triangle = [ [-0.8f,-0.6f,0.25f,0.125f,0.25f,0.75f,0.875f,-99],
            [0.7f,-0.4f,0.75f,0.625f,0.25f,0.25f,0.75f,-99],
            [-0.2f,0.9f,0.5f,0.25f,0.875f,0.75f,0.125f,-99] ];
        float[] control = [0.2f,0.4f,0.6f,1];
        var cases = new Dictionary<string, float[]> {
            ["identity"] = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1],
            ["projective"] = [0.8f,0.13f,-0.2f,0.15f, -0.17f,1.1f,0.09f,-0.05f, 0.21f,-0.11f,0.6f,0.03f, 0.09f,-0.17f,0.2f,1]
        };
        cases.Add("two-groups",cases["projective"]);
        cases.Add("background",cases["identity"]);
        foreach (var (name, matrix) in cases)
        {
            var caseControl = name=="background" ? new float[] {0.1f,0.2f,0.3f,0} : control;
            var vertices = name != "two-groups" ? triangle : Enumerable.Range(0,132).Select(v => new float[] {
                (v%11-5)*0.13f,(v%7-3)*0.17f,(v%5-2)*0.11f,
                (v%13+0.5f)/13,(v%17+0.25f)/17,(v%19+0.125f)/19,(v%23+0.375f)/23,-99
            }).ToArray();
            var directory = Path.Combine(Path.GetFullPath(output), name);
            Directory.CreateDirectory(directory);
            var initial = new uint[256];
            for (var i = 0; i < 16; i++) initial[9+i] = Bits(matrix[i]);
            for (var i = 0; i < 4; i++) initial[25+i] = Bits(caseControl[i]);
            initial[29] = (uint)tableAddress;
            uint[] descriptor = [(uint)streamAddress,32u<<16,(uint)vertices.Length,0xFAC];
            var stream = vertices.SelectMany(v => v.Select(Bits)).ToArray();
            var batch = Gen5NggPassthroughBatch.CreateNonindexedTriangleList((uint)vertices.Length);
            var slots = batch.OutputSlotCapacity;
            var primitiveFirst = checked((int)slots*12);
            var descriptorFirst = primitiveFirst+checked((int)slots*5);
            var sourceFirst = descriptorFirst+batch.GroupCount*4;
            var auxiliary = Enumerable.Repeat(0xCAFEBABEu,sourceFirst+batch.SourceWords.Length+16).ToArray();
            batch.Descriptors.Span.CopyTo(auxiliary.AsSpan(descriptorFirst));
            batch.SourceWords.Span.CopyTo(auxiliary.AsSpan(sourceFirst));
            var expected = (uint[])auxiliary.Clone();
            var floatWords = new List<int>();
            for (var vertex = 0; vertex < vertices.Length; vertex++)
            {
                var slot = vertex/126*128+vertex%126;
                // Independent GLSL mat4*vec4 reference; never use translated
                // instructions, emitted SPIR-V or observed GPU output here.
                for (var row = 0; row < 4; row++)
                {
                    double value = matrix[12+row];
                    for (var column = 0; column < 3; column++) value += (double)matrix[column*4+row]*vertices[vertex][column];
                    expected[slot*12+row] = Bits((float)value);
                    floatWords.Add(slot*12+row);
                }
                for (var component = 0; component < 4; component++)
                {
                    expected[slot*12+4+component] = Bits(vertices[vertex][3+component]);
                    expected[slot*12+8+component] = Bits(caseControl[component]);
                }
            }
            for(var first=0;first<vertices.Length;first+=126)
                for(uint local=0;local<Math.Min(126,vertices.Length-first);local+=3)
                {
                    uint[] primitive = [local|((local+1)<<10)|((local+2)<<20),local,local+1,local+2,1];
                    primitive.CopyTo(expected,primitiveFirst+(first/126*128+(int)local/3)*5);
                }
            var finalScalars = (uint[])initial.Clone();
            descriptor.CopyTo(finalScalars,36);
            Gen5GlobalMemoryBinding[] bindings = [
                new(0,tableAddress,[0x64],Bytes(descriptor),descriptor.Length*4,false) {WriteBackToGuest=false},
                new(36,streamAddress,[0x78,0x80],Bytes(stream),stream.Length*4,false) {WriteBackToGuest=false},
                new(0,0x23000000,[],Bytes(auxiliary),auxiliary.Length*4,false) {Writable=true,WriteBackToGuest=false}
            ];
            var state = new Gen5ShaderState(program,initial.Skip(8).Take(24).ToArray(),null,UserDataScalarRegisterBase:8);
            var evaluation = new Gen5ShaderEvaluation(initial,finalScalars,[],bindings);
            if (!Gen5SpirvTranslator.TryCompileComputeShader(state,evaluation,128,1,1,out var shader,out error,
                totalGlobalBufferCount:bindings.Length,exportOutput:new(2,slots,2),
                triangleOutput:new(2,(uint)primitiveFirst,slots,0),
                nggBatchInputs:new(2,(uint)descriptorFirst,2,(uint)sourceFirst,(uint)batch.GroupCount,128,32)))
                throw new InvalidOperationException($"{name}: {error}");
            File.WriteAllBytes(Path.Combine(directory,"original-bsp-compute.spv"),shader.Spirv);
            Write(directory,"initial.json",auxiliary); Write(directory,"expected.json",expected);
            Write(directory,"limits.json",new uint[] {slots,1,1});
            Write(directory,"storage-fixture.json",new {Buffers=new[] {descriptor,stream,auxiliary},OutputBinding=2,GroupsX=batch.GroupCount,
                FloatWords=floatWords,FloatTolerance=0.00002});
            Write(directory,"fixture-report.json",new {
                Scope="Source-derived BSP GLSL reference and explicit NGG transport; not captured title state or hardware scheduling",
                OriginalGsSha256=hash,MatrixColumnMajor=matrix,Control=caseControl,Vertices=vertices,
                VertexCount=vertices.Length,GroupCount=batch.GroupCount,SourceFirstWord=sourceFirst,
                OutputSlots=slots,PrimitiveFirstWord=primitiveFirst,ShaderCompiled=true,GpuExecuted=false,RenderingVerified=false
            });
            Console.WriteLine($"Prepared original BSP compute fixture: {directory}");
        }
    }
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static byte[] Bytes(uint[] words)
    {
        var bytes=new byte[words.Length*4];
        for(var i=0;i<words.Length;i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i*4),words[i]);
        return bytes;
    }
    private static void Write(string directory,string file,object value) =>
        File.WriteAllText(Path.Combine(directory,file),JsonSerializer.Serialize(value,new JsonSerializerOptions {WriteIndented=true}));
}
