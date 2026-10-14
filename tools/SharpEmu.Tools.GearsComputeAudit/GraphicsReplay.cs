// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.Libs.Gpu;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

internal static class GraphicsReplay
{
    public static void RunFrame(string computeFrameReport, string outputDirectory)
    {
        using var frame = JsonDocument.Parse(File.ReadAllText(computeFrameReport));
        var draws = frame.RootElement.GetProperty("Draws").EnumerateArray()
            .Where(d => d.GetProperty("DrawCount").GetUInt32() > 3).OrderBy(d => d.GetProperty("Sequence").GetUInt64()).ToArray();
        if (draws.Length != 3 || draws[0].GetProperty("Sequence").GetUInt64() != 2 ||
            draws[1].GetProperty("Sequence").GetUInt64() != 3 || draws[2].GetProperty("Sequence").GetUInt64() != 4)
            throw new InvalidOperationException("Frame audit requires the first three consecutive native gear draws");
        foreach (var draw in draws)
            Run(draw.GetProperty("CaptureDirectory").GetString()!, draw.GetProperty("ComputeDirectory").GetString()!, outputDirectory, deferPresentation: true);
        if (!GuestGpu.Current.TrySubmitGuestImage(0x400100000, 640, 360, 640))
            throw new InvalidOperationException("Combined mesh image was not queued");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !Directory.EnumerateFiles(outputDirectory, "present-*.bgra").Any()) Thread.Sleep(100);
        var images = Directory.EnumerateFiles(outputDirectory, "present-*.bgra").ToArray();
        if (images.Length == 0) throw new InvalidOperationException("No combined mesh image captured");
        File.WriteAllText(Path.Combine(outputDirectory, "frame-graphics-report.json"), JsonSerializer.Serialize(new {
            Scope = "Independent first native frame mesh replay; three verified compute readbacks, original PS and transport vertex bridge; background initialized black; not native runtime animation",
            DrawCount = draws.Length, ComputeFrameReport = Path.GetFullPath(computeFrameReport),
            ComputeFrameReportSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(computeFrameReport))),
            CapturedImages = images.Select(f => new { File = f, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) }).ToArray(),
            NativeFrameRenderingVerified = false, SharedDepth = true
        }));
        Console.WriteLine("Captured independent three-gear frame replay: " + images[0]);
        Environment.Exit(0);
    }

    public static void Run(string captureDirectory, string computeDirectory, string outputDirectory, bool deferPresentation = false)
    {
        Directory.CreateDirectory(outputDirectory);
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_IMAGES", "1");
        Environment.SetEnvironmentVariable("SHARPEMU_TRACE_GUEST_IMAGE_ADDRS", "0x400100000");
        Environment.SetEnvironmentVariable("SHARPEMU_LOG_AGC_SHADER", "1");
        Environment.SetEnvironmentVariable("SHARPEMU_GUEST_IMAGE_DUMP_DIR", Path.GetFullPath(outputDirectory));
        var gsFile = Path.Combine(captureDirectory, "0000000200001000.replay.json");
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(computeDirectory, "execution-report.json")));
        if (!proof.RootElement.GetProperty("NumericalOutputsVerified").GetBoolean() ||
            !proof.RootElement.GetProperty("ConnectivityRemapVerified").GetBoolean() ||
            proof.RootElement.GetProperty("ReplaySha256").GetString() != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gsFile))))
            throw new InvalidOperationException("Completed verified compute replay does not match this capture");
        foreach (var name in new[] { "actual.json", "remapped-indices.json" })
        {
            var entry = proof.RootElement.GetProperty("Hashes").EnumerateArray().Single(e => e.GetProperty("File").GetString() == name);
            if (entry.GetProperty("Sha256").GetString() != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(computeDirectory, name)))))
                throw new InvalidOperationException("Completed readback differs from verified report: " + name);
        }
        using var gsEnvelope = JsonDocument.Parse(File.ReadAllText(gsFile));
        using var psEnvelope = JsonDocument.Parse(File.ReadAllText(Path.Combine(captureDirectory, "0000000200001200.replay.json")));
        var draw = psEnvelope.RootElement.GetProperty("DrawContext").Deserialize<Gen5ShaderReplayDrawContext>()!;
        var gsDraw = gsEnvelope.RootElement.GetProperty("DrawContext").Deserialize<Gen5ShaderReplayDrawContext>()!;
        if (JsonSerializer.Serialize(draw) != JsonSerializer.Serialize(gsDraw)) throw new InvalidOperationException("Stage draw contexts differ");
        var snapshot = psEnvelope.RootElement.GetProperty("Snapshot").Deserialize<Gen5ShaderReplaySnapshot>()!;
        var stagePath = "artifacts/external-homebrew/gears-toolchain/gears-native-windows/shaders/gears_lit.ps.bin";
        var original = File.ReadAllBytes(stagePath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(stagePath)!, "gears_lit.manifest.json")));
        if (!Convert.ToHexString(SHA256.HashData(original)).Equals(manifest.RootElement.GetProperty("stages").GetProperty("pixel").GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Original pixel stage hash mismatch");
        foreach (var instruction in snapshot.Instructions)
            for (var word = 0; word < instruction.Words.Length; word++)
                if (BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(checked((int)instruction.Pc + word * 4))) != instruction.Words[word])
                    throw new InvalidOperationException("Captured pixel words differ from original");
        if (!Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ShaderMemory(snapshot.ShaderAddress, original), Generation.Gen5), snapshot.ShaderAddress, out var program, out var error))
            throw new InvalidOperationException(error);
        if (snapshot.GlobalMemoryBindings.Length != 0 || snapshot.ImageBindings.Length != 0)
            throw new InvalidOperationException("This graphics audit supports the original resource-free Gears pixel stage only");
        var gpu = GuestGpu.Current;
        var pixelState = new Gen5ShaderState(program!, snapshot.UserData, snapshot.Metadata, snapshot.ComputeSystemRegisters, snapshot.UserDataScalarRegisterBase);
        var pixelEvaluation = new Gen5ShaderEvaluation(snapshot.InitialScalarRegisters, snapshot.ScalarRegisters, [], [],
            snapshot.EvaluationComputeSystemRegisters, snapshot.RuntimeScalarRegisters?.ToHashSet());
        if (!gpu.TryCompilePixelShader(pixelState, pixelEvaluation, [new(0, 0, Gen5PixelOutputKind.Float)], out var pixel, out error,
            totalGlobalBufferCount: 1, scalarRegisterBufferIndex: 0, pixelInputEnable: draw.PixelInputEnable,
            pixelInputAddress: draw.PixelInputAddress, pixelInputCntl: draw.PixelInputControl)) throw new InvalidOperationException(error);
        var actual = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(computeDirectory, "actual.json")))!;
        var batch = MeshReplayReference.Partition(draw);
        var vertices = new byte[checked((int)batch.OutputSlotCapacity * 48)];
        for (var word = 0; word < vertices.Length / 4; word++) BinaryPrimitives.WriteUInt32LittleEndian(vertices.AsSpan(word * 4), actual[word]);
        var instructions = new List<Gen5ShaderInstruction>();
        var views = new List<Gen5VertexInputBinding>();
        for (uint location = 0; location < 3; location++)
        {
            var pc = location * 4;
            instructions.Add(new(pc, Gen5ShaderEncoding.Mubuf, "BufferLoadFormatXyzw", [], [], [],
                new Gen5BufferMemoryControl(4, 0, location * 4, 0, 0, true, false, false, false)));
            views.Add(new(pc, location, 4, 14, 7, 0x400000000, 48, location * 16, vertices, vertices.Length, false));
        }
        // Transport bridge only: copies completed GPU positions/packed parameters;
        // it does not recalculate guest geometry or replace the original pixel stage.
        instructions.Add(new(12, Gen5ShaderEncoding.Exp, "Exp", [], [Gen5Operand.Vector(0), Gen5Operand.Vector(1), Gen5Operand.Vector(2), Gen5Operand.Vector(3)], [], new Gen5ExportControl(12, 15, false, false, false)));
        instructions.Add(new(16, Gen5ShaderEncoding.Exp, "Exp", [], [Gen5Operand.Vector(4), Gen5Operand.Vector(5), Gen5Operand.Vector(6), Gen5Operand.Vector(7)], [], new Gen5ExportControl(32, 15, false, false, false)));
        instructions.Add(new(20, Gen5ShaderEncoding.Exp, "Exp", [], [Gen5Operand.Vector(8), Gen5Operand.Vector(9), Gen5Operand.Vector(10), Gen5Operand.Vector(11)], [], new Gen5ExportControl(33, 3, false, true, false)));
        instructions.Add(new(24, Gen5ShaderEncoding.Sopp, "SEndpgm", [], [], [], null));
        if (!gpu.TryCompileVertexShader(new(new(0x400001000, instructions), [], null),
            new(new uint[256], new uint[256], [], [], VertexInputs: views), out var vertex, out error,
            totalGlobalBufferCount: 1, requiredVertexOutputCount: 2)) throw new InvalidOperationException(error);
        var indices = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(computeDirectory, "remapped-indices.json")))!;
        var indexBytes = new byte[indices.Length * 4];
        for (var i = 0; i < indices.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(indexBytes.AsSpan(i * 4), indices[i]);
        var scalarBytes = new byte[256 * 4];
        for (var i = 0; i < snapshot.InitialScalarRegisters.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(scalarBytes.AsSpan(i * 4), snapshot.InitialScalarRegisters[i]);
        gpu.EnsureStarted(640, 360);
        const ulong targetAddress = 0x400100000;
        gpu.SubmitOffscreenTranslatedDraw(pixel!, [], [new(0x400002000, scalarBytes, scalarBytes.Length, false)], 2,
            [new(targetAddress, 640, 360, 10, 0)], vertex!,
            vertexCount: (uint)indices.Length, indexBuffer: new(indexBytes, indexBytes.Length, true, false),
            vertexBuffers: views.Select(v => new GuestVertexBuffer(v.Location, v.ComponentCount, v.DataFormat, v.NumberFormat,
                v.BaseAddress, v.Stride, v.OffsetBytes, vertices, vertices.Length, false)).ToArray(),
            // First-use attachment initialization clears to ClearDepth. Guest
            // ClearEnable means a DB clear draw and intentionally disables testing.
            renderState: GuestRenderState.Default with { Depth = new(true, true, 1) },
            depthTarget: new(0x400200000, 0x400200000, 640, 360, 3, 0, 1f, false),
            shaderAddress: snapshot.ShaderAddress);
        if (deferPresentation) return;
        if (!gpu.TrySubmitGuestImage(targetAddress, 640, 360, 640)) throw new InvalidOperationException("Offscreen result was not queued for presentation");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !Directory.EnumerateFiles(outputDirectory, "present-*.bgra").Any()) Thread.Sleep(100);
        var images = Directory.EnumerateFiles(outputDirectory, "present-*.bgra").ToArray();
        var nonblack = 0;
        if (images.Length > 0)
        {
            var bytes = File.ReadAllBytes(images[0]);
            for (var i = 0; i < bytes.Length; i += 4) if ((bytes[i] | bytes[i + 1] | bytes[i + 2]) != 0) nonblack++;
        }
        File.WriteAllText(Path.Combine(outputDirectory, "graphics-report.json"), JsonSerializer.Serialize(new {
            Scope = "Independent completed GPU mesh replay via transport vertex bridge and original guest pixel shader; not native Gears runtime",
            CapturedImages = images, NativeGearsRenderingVerified = false, ImageCaptured = images.Length > 0,
            NonblackPixels = nonblack, PixelShaderSha256 = Convert.ToHexString(SHA256.HashData(original)),
            ImageHashes = images.Select(f => new { File = f, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) }).ToArray(),
            LightingAndDepthVerified = false
        }));
        if (images.Length == 0 || nonblack < 1000) throw new InvalidOperationException("No nonempty completed graphics image captured");
        Console.WriteLine("Captured independent original-pixel mesh replay: " + images[0]);
        Environment.Exit(0);
    }
}
