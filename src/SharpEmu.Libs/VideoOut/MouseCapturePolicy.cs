// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.Libs.VideoOut;

// Window-thread policy: activation/title-bar interaction never starts capture.
internal sealed class MouseCapturePolicy
{
    private bool focused;
    internal bool Captured { get; private set; }
    internal void SetFocus(bool value) { focused = value; if (!value) Captured = false; }
    internal void Release() => Captured = false;
    internal bool TryCapture(bool leftButton, double x, double y, int width, int height)
    {
        if (Captured || !focused || !leftButton || x < 0 || y < 0 || x >= width || y >= height ||
            !double.IsFinite(x) || !double.IsFinite(y)) return false;
        Captured = true;
        return true;
    }
}
