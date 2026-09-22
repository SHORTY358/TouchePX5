using SharpEmu.HLE;
using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class VideoOutLinearFrameTests
{
    [Theory]
    [InlineData(0x80000000UL, false)]
    [InlineData(0x80002200UL, true)]
    public void ReadsVisiblePixelsWithPaddedRows(ulong format, bool abgr)
    {
        var memory = new FakeCpuMemory(0x10000, 24);
        byte[] row = abgr ? [11, 22, 33, 255, 44, 55, 66, 255] : [33, 22, 11, 255, 66, 55, 44, 255];
        Assert.True(memory.TryWrite(0x10000, row));
        Assert.True(memory.TryWrite(0x10008, new byte[] { 99, 99, 99, 99 }));
        Assert.True(memory.TryWrite(0x1000C, row));
        var ctx = new CpuContext(memory, Generation.Gen5);
        var info = new VideoOutExports.DisplayBufferInfo(0x10000, format, 1, 2, 2, 3);
        Assert.True(VideoOutExports.TryReadLinearDisplayFrame(ctx, info, out var pixels));
        Assert.Equal(new byte[] { 33, 22, 11, 255, 66, 55, 44, 255, 33, 22, 11, 255, 66, 55, 44, 255 }, pixels);
    }

    [Theory]
    [InlineData(0, 2, 2, 0x80000000UL)]
    [InlineData(1, 2, 1, 0x80000000UL)]
    [InlineData(1, 0, 2, 0x80000000UL)]
    [InlineData(1, 2, 2, 0x12345678UL)]
    public void RejectsUnsupportedLayout(uint tile, uint width, uint pitch, ulong format)
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x10000, 32), Generation.Gen5);
        var info = new VideoOutExports.DisplayBufferInfo(0x10000, format, tile, width, 2, pitch);
        Assert.False(VideoOutExports.TryReadLinearDisplayFrame(ctx, info, out var pixels));
        Assert.Empty(pixels);
    }

    [Fact]
    public void UnmappedSecondRowDoesNotReturnPartialFrame()
    {
        var ctx = new CpuContext(new FakeCpuMemory(0x10000, 8), Generation.Gen5);
        var info = new VideoOutExports.DisplayBufferInfo(0x10000, 0x80000000, 1, 2, 2, 2);
        Assert.False(VideoOutExports.TryReadLinearDisplayFrame(ctx, info, out var pixels));
        Assert.Empty(pixels);
    }
}
