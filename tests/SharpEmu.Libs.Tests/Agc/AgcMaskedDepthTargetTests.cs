// Copyright (C) 2026 Touché PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcMaskedDepthTargetTests
{
    private static Dictionary<uint, uint> Registers(uint colorMask, uint depthControl) => new()
    {
        [0x318] = 0x100000, [0x390] = 0,
        [0x31C] = 0x28, [0x3B0] = ((1920u - 1) << 14) | (1080u - 1),
        [0x3B8] = 0x1000000, [0x8E] = colorMask,
        [0x200] = depthControl, [0x010] = 0x183,
        [0x007] = ((1080u - 1) << 16) | (1920u - 1),
        [0x012] = 0x200000, [0x014] = 0x200000,
        [0x10F] = BitConverter.SingleToUInt32Bits(960f),
        [0x110] = BitConverter.SingleToUInt32Bits(960f),
        [0x111] = BitConverter.SingleToUInt32Bits(-540f),
        [0x112] = BitConverter.SingleToUInt32Bits(540f),
        [0x113] = BitConverter.SingleToUInt32Bits(1f), [0x114] = 0,
    };

    [Fact]
    public void ColorMaskedDepthPass_KeepsBoundAttachmentDimensions()
    {
        var registers = Registers(0, 0xF6);
        var targets = AgcExports.GetRasterizationRenderTargets(registers);
        var target = Assert.Single(targets);
        Assert.Equal(0x10000000UL, target.Address);
        Assert.Equal(1920u, target.Width);
        Assert.Equal(1080u, target.Height);
        var state = AgcExports.CreateRenderState(registers, targets, 0xF);
        Assert.Equal(0u, state.Blend.WriteMask);
        Assert.True(state.Depth.TestEnable);
        Assert.True(state.Depth.WriteEnable);
        Assert.Equal(7u, state.Depth.CompareOp);
        Assert.NotNull(state.Viewport);
        Assert.Equal(1920f, state.Viewport.Value.Width);
    }

    [Fact]
    public void ColorMaskedPassWithoutDepthWork_RemainsTargetless() =>
        Assert.Empty(AgcExports.GetRasterizationRenderTargets(Registers(0, 0)));

    [Fact]
    public void ColorMaskedPassWithoutDepthSurface_RemainsTargetless()
    {
        var registers = Registers(0, 0xF6);
        registers.Remove(0x010);
        Assert.Empty(AgcExports.GetRasterizationRenderTargets(registers));
    }

    [Fact]
    public void OrdinaryColorPass_KeepsItsTarget() =>
        Assert.Single(AgcExports.GetRasterizationRenderTargets(Registers(0xF, 0)));
}
