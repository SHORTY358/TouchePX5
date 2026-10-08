// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.Libs.Keyboard;
using Xunit;

namespace SharpEmu.Libs.Tests.Keyboard;

public sealed class KeyboardReplayInputTests
{
    [Fact]
    public void PressAndReleaseUseHalfOpenIntervalsAndExpire()
    {
        long now = 99;
        var replay = new KeyboardReplayInput("100:13:50;200:27:20", () => now);
        Assert.False(replay.IsKeyDown(13));
        now = 100;
        Assert.True(replay.IsKeyDown(13));
        Assert.False(replay.IsKeyDown(27));
        now = 150;
        Assert.False(replay.IsKeyDown(13));
        now = 200;
        Assert.True(replay.IsKeyDown(27));
        now = 60000;
        Assert.False(replay.IsKeyDown(13));
        Assert.False(replay.IsKeyDown(27));
    }
    [Theory]
    [InlineData("-1:13:10")]
    [InlineData("120000:13:10")]
    [InlineData("1:256:10")]
    [InlineData("1:13:0")]
    [InlineData("1:13:5001")]
    [InlineData("bad")]
    public void RejectsUnboundedOrMalformedReplay(string script) =>
        Assert.Throws<ArgumentException>(() => new KeyboardReplayInput(script));

    [Fact]
    public void LongerGameScenarioStillExpiresAtTheBoundedDeadline()
    {
        long now = 60000;
        var replay = new KeyboardReplayInput("60000:13:10;119990:38:10", () => now);
        Assert.True(replay.IsKeyDown(13));
        now = 119999;
        Assert.True(replay.IsKeyDown(38));
        now = KeyboardReplayInput.MaximumReplayMilliseconds;
        Assert.False(replay.IsKeyDown(38));
    }
}
