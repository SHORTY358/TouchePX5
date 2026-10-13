// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using SharpEmu.Libs.Gpu;
using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class VulkanDisplayRegistrationTests
{
    [Fact]
    public void RegistrationIsFlipAvailableButFailedPublicationDoesNotClaimGpuContents()
    {
        const ulong address = 0xFEDC000001000000;
        const uint format = 0x80000000u | (10u << 8);
        var targets = new[] { new GuestRenderTarget(address, 64, 64, 10, 0) };
        VulkanVideoPresenter.RegisterKnownDisplayBuffer(address, format);
        Assert.True(VulkanVideoPresenter.IsGpuGuestImageAvailable(address, 10, 0));
        Assert.True(VulkanVideoPresenter.IsRegistrationOnlyDisplayBuffer(address));

        Assert.False(VulkanVideoPresenter.TryCommitGuestWorkPublication(
            0, targets, VulkanVideoPresenter.PublishGuestImages));
        Assert.True(VulkanVideoPresenter.IsRegistrationOnlyDisplayBuffer(address));

        Assert.True(VulkanVideoPresenter.TryCommitGuestWorkPublication(
            1, targets, VulkanVideoPresenter.PublishGuestImages));
        Assert.False(VulkanVideoPresenter.IsRegistrationOnlyDisplayBuffer(address));

        // Re-registering a live target must not reopen initial CPU seeding and
        // overwrite pixels produced by previously queued GPU work.
        VulkanVideoPresenter.RegisterKnownDisplayBuffer(address, format);
        Assert.False(VulkanVideoPresenter.IsRegistrationOnlyDisplayBuffer(address));
    }
}
