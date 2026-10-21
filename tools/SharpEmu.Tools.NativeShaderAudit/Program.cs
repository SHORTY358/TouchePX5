// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;

if(args.Length==2 && args[0]=="--verify-native-bsp-textures")
{
    NativeBspTextures.VerifyCapture(args[1]);
    return;
}
if(args.Length==3 && args[0]=="--native-bsp-textures")
{
    NativeBspTextures.Run(args[1],args[2]);
    return;
}
if(args.Length==2 && args[0]=="--consume-native-bsp")
{
    NativeBspCompute.Consume(args[1]);
    return;
}
if(args.Length==4 && args[0]=="--native-bsp-compute")
{
    NativeBspCompute.Create(args[1],args[2],args[3]);
    return;
}
if (args.Length == 3 && args[0] == "--bsp-compute")
{
    BspComputeFixture.Create(args[1], args[2]);
    return;
}
if (args.Length == 2 && args[0] == "--consume-bsp")
{
    BspComputeFixture.Consume(args[1]);
    return;
}
if(args.Length==4 && args[0]=="--bsp-graphics")
{
    BspGraphics.Run(args[1],args[2],args[3]);
    return;
}
if(args.Length==3 && args[0]=="--verify-bsp-pixels")
{
    BspPixelReference.Verify(args[1],args[2]);
    return;
}
if (args.Length != 2) throw new ArgumentException("usage: NativeShaderAudit <PAL manifest.json> <output directory> | --bsp-compute <manifest> <output>");
var manifestPath = Path.GetFullPath(args[0]);
var root = Path.GetDirectoryName(manifestPath)!;
var output = Path.GetFullPath(args[1]);
using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
var data = manifest.RootElement;
if (data.GetProperty("target").GetString() != "gfx1013" || !data.GetProperty("no_relocations").GetBoolean())
    throw new InvalidOperationException("Expected resolved GFX1013 PAL stages");
var name = data.GetProperty("name").GetString()!;
if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
    throw new InvalidOperationException("Invalid manifest stage name");
Directory.CreateDirectory(output);
var reports = new List<object>();
var decodedAll = true;
foreach (var (stage, suffix) in new[] { ("pre_raster_gs", "gs"), ("pixel", "ps") })
{
    var path = Path.Combine(root, $"{name}.{suffix}.bin");
    var bytes = File.ReadAllBytes(path);
    var entry = data.GetProperty("stages").GetProperty(stage);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    if (bytes.Length != entry.GetProperty("bytes").GetInt32() || bytes.Length == 0 || bytes.Length > 1024 * 1024 || bytes.Length % 4 != 0 ||
        !hash.Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Original stage extent/hash mismatch: {stage}");
    const ulong address = 0x100000;
    var success = Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ReadOnlyShaderMemory(address, bytes), Generation.Gen5),
        address, out var program, out var error);
    decodedAll &= success;
    var covered = new bool[bytes.Length / 4];
    var instructions = new List<object>();
    if (success)
        foreach (var instruction in program!.Instructions)
        {
            for (var word = 0; word < instruction.Words.Count; word++)
            {
                var offset = checked((int)instruction.Pc + word * 4);
                if (offset < 0 || offset % 4 != 0 || offset + 4 > bytes.Length ||
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != instruction.Words[word])
                    throw new InvalidOperationException("Decoder instruction differs from original bytes");
                covered[offset / 4] = true;
            }
            instructions.Add(new { instruction.Pc, instruction.Encoding, instruction.Opcode, instruction.Words,
                instruction.Sources, instruction.Destinations, ControlKind = instruction.Control?.GetType().Name,
                Control = instruction.Control is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(instruction.Control, instruction.Control.GetType()) });
        }
    var inventory = success ? program!.Instructions.GroupBy(i => i.Opcode).ToDictionary(g => g.Key, g => g.Count()) : [];
    File.WriteAllText(Path.Combine(output, suffix + "-instructions.json"), JsonSerializer.Serialize(instructions, new JsonSerializerOptions { WriteIndented = true }));
    reports.Add(new { Stage = stage, Sha256 = hash, Bytes = bytes.Length, DecoderPassed = success, DecoderError = error,
        Instructions = instructions.Count, CoveredBytes = covered.Count(c => c) * 4, UncoveredBytes = covered.Count(c => !c) * 4,
        FullStageByteCoverage = covered.All(c => c), Inventory = inventory });
    Console.WriteLine($"{stage}: {(success ? "decoded" : "BLOCKED")} {instructions.Count} instructions; {error}");
}
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new {
    Scope = "Original hash-checked PAL stage decode inventory only; not shader translation, GPU execution or title compatibility",
    ManifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))),
    DecoderAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Gen5ShaderTranslator).Assembly.Location))),
    DecoderPassed = decodedAll, Stages = reports, GpuExecuted = false, RenderingVerified = false
}, new JsonSerializerOptions { WriteIndented = true }));
Environment.ExitCode = decodedAll ? 0 : 2;

internal sealed class ReadOnlyShaderMemory(ulong address, byte[] bytes) : ICpuMemory
{
    public bool TryRead(ulong location, Span<byte> destination)
    {
        if (location < address || location - address > (ulong)bytes.Length || (ulong)destination.Length > (ulong)bytes.Length - (location - address)) return false;
        bytes.AsSpan((int)(location - address), destination.Length).CopyTo(destination); return true;
    }
    public bool TryWrite(ulong location, ReadOnlySpan<byte> source) => false;
}
