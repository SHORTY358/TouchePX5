// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.HLE;
using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class VideoOutTiledFrameTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(3, 7, 31)]
    [InlineData(4, 0, 32)]
    [InlineData(0, 8, 1088)]
    [InlineData(16, 16, 2048)]
    [InlineData(511, 127, 64575)]
    [InlineData(512, 0, 65536)]
    [InlineData(0, 128, 245760)]
    [InlineData(1919, 1079, 2206335)]
    public void OffsetMatchesPinnedOpenSdlReference(uint x, uint y, uint expected)
    {
        Assert.Equal(expected, VideoOutCpuDisplayFrames.PixelOffset(x, y, 1920));
    }

    [Fact]
    public void ReadsColorAndTileBoundariesWithPaddedPitch()
    {
        const ulong address = 0x10000;
        var memory = new FakeCpuMemory(address, 1048576);
        // Literal offsets obtained from SDL PS5_TilePixel, not the production helper.
        (uint X, uint Y, uint Offset)[] marked =
            [(0, 0, 0), (16, 16, 2048), (511, 127, 64575),
             (512, 0, 65536), (0, 128, 131072), (512, 128, 196608)];
        foreach (var point in marked)
            Assert.True(memory.TryWrite(address + point.Offset * 4, new byte[] { 15, 31, 63, 0 }));
        var ctx = new CpuContext(memory, Generation.Gen5);
        var info = new VideoOutExports.DisplayBufferInfo(address, 0x8000000022000000, 0, 513, 129, 1024);
        Assert.True(VideoOutExports.TryReadCpuDisplayFrame(ctx, info, out var pixels));
        Assert.Equal(513 * 129 * 4, pixels.Length);
        for (uint y = 0; y < 129; y++)
        for (uint x = 0; x < 513; x++)
        {
            var offset = (int)(y * 513 + x) * 4;
            var present = marked.Any(point => point.X == x && point.Y == y);
            Assert.Equal(present ? (byte)63 : (byte)0, pixels[offset]);
            Assert.Equal(present ? (byte)31 : (byte)0, pixels[offset + 1]);
            Assert.Equal(present ? (byte)15 : (byte)0, pixels[offset + 2]);
            Assert.Equal(255, pixels[offset + 3]);
        }
    }

    [Theory]
    [InlineData(Generation.Gen4, 1048576, 0x8000000022000000UL)]
    [InlineData(Generation.Gen5, 262144, 0x8000000022000000UL)]
    [InlineData(Generation.Gen5, 1048576, 0x80000000UL)]
    public void UnsupportedPlatformFormatOrMissingTailReturnsNoFrame(Generation generation, int size, ulong format)
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x10000, size), generation);
        var info = new VideoOutExports.DisplayBufferInfo(0x10000, format, 0, 513, 129, 1024);
        Assert.False(VideoOutExports.TryReadCpuDisplayFrame(ctx, info, out var pixels));
        Assert.Empty(pixels);
    }
}
