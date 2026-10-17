// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.Libs.Gpu;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class GuestPresentationCadenceTests
{
    [Fact]
    public void CompletedBacklogCannotBurst_AndDeferralDoesNotCompleteOrSlideDeadline()
    {
        var notifications = 0;
        var ticket = new GuestPresentationCompletion((_, _) => Interlocked.Increment(ref notifications), 100);
        try
        {
            var cadence = new GuestPresentationCadence();
            Assert.True(cadence.CanPresent(ticket, 1000));
            cadence.Presented(1000);
            Assert.False(cadence.CanPresent(ticket, 1001));
            Assert.False(cadence.CanPresent(ticket, 1099));
            Assert.True(cadence.CanPresent(ticket, 1100));
            Assert.Equal(0, notifications);
            // Out-of-date/retry leaves cadence untouched. Only the successful
            // retry advances it; a slow frame earns no multi-frame catch-up.
            Assert.True(cadence.CanPresent(ticket, 1700));
            cadence.Presented(1700);
            Assert.False(cadence.CanPresent(ticket, 1701));
            Assert.True(cadence.CanPresent(ticket, 1800));
        }
        finally { ticket.Cancel(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnpacedTicketsAndHostFramesDoNotWait(long interval)
    {
        var ticket = new GuestPresentationCompletion((_, _) => { }, interval);
        try
        {
            var cadence = new GuestPresentationCadence();
            cadence.Presented(1000);
            Assert.True(cadence.CanPresent(ticket, 1001));
            Assert.True(cadence.CanPresent(null, 1001));
        }
        finally { ticket.Cancel(); }
    }
}
