// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Gpu;
using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class CpuMipSnapshotTests
{
    private static GuestDrawTexture Snapshot() => new(
        0x30000, 2, 2, 10, 0, new byte[512], false, false,
        MipLevels: 2, ResourceMipLevels: 2, Pitch: 64,
        AdditionalMips: [new(1, 1, 64, new byte[256])]);

    [Fact]
    public void IndependentSnapshotsMatchDespiteTrackerGeneration()
    {
        Assert.True(CpuMipSnapshot.Matches(Snapshot(), Snapshot() with { WriteGeneration = 12 }));
        Assert.Equal(768, CpuMipSnapshot.ByteCount(Snapshot()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangesInAnyLevelRejectReuse(bool lower)
    {
        var old = Snapshot();
        var changed = Snapshot();
        (lower ? changed.AdditionalMips![0].Pixels : changed.RgbaPixels)[0] = 255;
        Assert.False(CpuMipSnapshot.Matches(old, changed));
        Assert.True(CpuMipSnapshot.Matches(old, Snapshot()));
    }

    [Fact]
    public void ViewSamplerAndLayoutChangesRejectReuse()
    {
        var source = Snapshot();
        GuestDrawTexture[] variants =
        [
            source with { BaseMipLevel = 1 },
            source with { MipLevels = 1 },
            source with { ResourceMipLevels = 3 },
            source with { Sampler = new(1, 0, 0, 0) },
            source with { DstSelect = 0 },
            source with { Pitch = 128 },
            source with { AdditionalMips = [new(1, 1, 128, new byte[256])] },
            source with { AdditionalMips = [new(2, 1, 64, new byte[256])] },
            source with { AdditionalMips = null },
        ];
        Assert.All(variants, candidate => Assert.False(CpuMipSnapshot.Matches(source, candidate)));
    }
}
