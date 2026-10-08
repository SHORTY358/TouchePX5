// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.HLE.Host;

// Window-thread virtual cursor events consumed by guest threads. No desktop
// warping or global clipping; the window backend owns relative cursor mode.
public sealed class HostMouseMotion
{
    public static HostMouseMotion Current { get; } = new();
    private readonly object gate = new();
    private bool attached, focused, havePosition;
    private double x, y, pendingX, pendingY;
    public bool IsAttached { get { lock (gate) return attached; } }
    public void Attach() { lock (gate) { attached = true; focused = false; Reset(); } }
    public void Detach() { lock (gate) { attached = focused = false; Reset(); } }
    public void SetFocus(bool value) { lock (gate) { focused = value; Reset(); } }
    public void ClearPending() { lock (gate) Reset(); }
    private void Reset() { havePosition = false; pendingX = pendingY = 0; }
    public void Submit(double nextX, double nextY)
    {
        if (!double.IsFinite(nextX) || !double.IsFinite(nextY)) return;
        lock (gate)
        {
            if (!attached || !focused) return;
            if (havePosition)
            {
                pendingX = Math.Clamp(pendingX + nextX - x, -100000, 100000);
                pendingY = Math.Clamp(pendingY + nextY - y, -100000, 100000);
            }
            x = nextX; y = nextY; havePosition = true;
        }
    }
    public bool TryConsume(out int deltaX, out int deltaY)
    {
        lock (gate)
        {
            deltaX = deltaY = 0;
            if (!attached || !focused) return false;
            deltaX = (int)pendingX; deltaY = (int)pendingY;
            pendingX -= deltaX; pendingY -= deltaY;
            return true;
        }
    }
}
