// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Reflection;
using System.Security.Cryptography;
using SharpEmu.HLE;
using SharpEmu.Libs.VideoOut;

if (args.Length != 2) throw new ArgumentException("Expected captured tiled source and output HUD BMP.");
var source = File.ReadAllBytes(args[0]);
if (source.Length > 16 * 1024 * 1024) throw new ArgumentException("Source exceeds diagnostic bound.");
var assembly = typeof(VideoOutExports).Assembly;
var descriptor = assembly.GetType("SharpEmu.Libs.VideoOut.VideoOutExports+DisplayBufferInfo", true)!;
var info = Activator.CreateInstance(descriptor, [0x10000000UL, 0x8000000022000000UL, 0u, 1920u, 1080u, 1920u])!;
var decoder = assembly.GetType("SharpEmu.Libs.VideoOut.VideoOutCpuDisplayFrames", true)!
    .GetMethod("TryReadTiled", BindingFlags.Static | BindingFlags.NonPublic)!;
object?[] call = [new CpuContext(new CaptureMemory(source), Generation.Gen5), info, null, null, null];
if (!(bool)decoder.Invoke(null, call)!) throw new InvalidOperationException("Production decoder rejected capture.");
var pixels = (byte[])call[2]!;
var hud = pixels.AsSpan(1920 * 880 * 4, 1920 * 200 * 4).ToArray();
using (var writer = new BinaryWriter(File.Create(args[1])))
{
    writer.Write((ushort)0x4D42); writer.Write(54 + hud.Length); writer.Write(0); writer.Write(54);
    writer.Write(40); writer.Write(1920); writer.Write(-200); writer.Write((ushort)1); writer.Write((ushort)32);
    writer.Write(0); writer.Write(hud.Length); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
    writer.Write(hud);
}
for (var offset = 0; offset < hud.Length; offset += 4) (hud[offset], hud[offset + 2]) = (hud[offset + 2], hud[offset]);
Console.WriteLine("Production decoder RGBA HUD SHA256: " + Convert.ToHexString(SHA256.HashData(hud)));

sealed class CaptureMemory(byte[] bytes) : ICpuMemory
{
    public bool TryRead(ulong address, Span<byte> destination)
    {
        if (address < 0x10000000 || address - 0x10000000 > (ulong)bytes.Length ||
            (ulong)destination.Length > (ulong)bytes.Length - (address - 0x10000000)) return false;
        bytes.AsSpan((int)(address - 0x10000000), destination.Length).CopyTo(destination);
        return true;
    }
    public bool TryWrite(ulong address, ReadOnlySpan<byte> source) => false;
}
