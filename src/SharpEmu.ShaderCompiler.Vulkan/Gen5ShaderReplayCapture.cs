// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Concurrent;
using System.Text.Json;
using SharpEmu.ShaderCompiler;
namespace SharpEmu.ShaderCompiler.Vulkan;

public sealed record Gen5ShaderReplayLayout(int GlobalBufferBase, int TotalGlobalBufferCount,
    int ImageBindingBase, int ScalarRegisterBufferIndex, int RequiredVertexOutputCount, ulong StorageBufferOffsetAlignment);

public sealed record Gen5ShaderReplayDrawContext(uint DrawCount, uint InstanceCount,
    uint PrimitiveType, int BaseVertex, bool Indexed, ulong IndexBufferAddress,
    uint IndexOffset, int IndexStrideBytes, byte[]? IndexData, string IndexCaptureStatus);

public static class Gen5ShaderReplayCapture
{
    private static readonly ConcurrentDictionary<string, byte> Captured = new();
    public static bool IsEnabled => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("TOUCHEPX5_SHADER_REPLAY_DIR"));

    public static void TryWrite(Gen5ShaderState state, Gen5ShaderEvaluation evaluation,
        Gen5ShaderReplayLayout layout, Gen5ShaderReplayDrawContext? drawContext = null)
    {
        var directory = Environment.GetEnvironmentVariable("TOUCHEPX5_SHADER_REPLAY_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var filter = Environment.GetEnvironmentVariable("TOUCHEPX5_SHADER_REPLAY_ADDRESS");
        if (!string.IsNullOrWhiteSpace(filter) &&
            (!ulong.TryParse(filter.Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                System.Globalization.NumberStyles.HexNumber, null, out var address) || address != state.Program.Address)) return;
        string? key = null;
        try
        {
            key = Path.GetFullPath(directory) + ":" + state.Program.Address.ToString("X16");
            if (!Captured.TryAdd(key, 0)) return;
            var snapshot = Gen5ShaderReplaySnapshot.Create(state, evaluation);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{state.Program.Address:X16}.replay.json"),
                JsonSerializer.Serialize(new { Scope = "Owned vertex backend inputs before compilation; no replay or GPU execution implied",
                    Snapshot = snapshot, BackendLayout = layout, DrawContext = drawContext }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (key is not null) Captured.TryRemove(key, out _);
            Console.Error.WriteLine($"[LOADER][WARN] Shader replay capture failed: {exception.Message}");
        }
    }
}
