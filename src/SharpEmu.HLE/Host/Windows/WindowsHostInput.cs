// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace SharpEmu.HLE.Host.Windows;

/// <summary>
/// Windows input backend: DualSense over raw HID plus XInput controllers for gamepads,
/// user32 for the keyboard-fallback queries. Rumble fans out to every reader; lightbar
/// only exists on the DualSense.
/// </summary>
internal sealed partial class WindowsHostInput : IHostInput
{
    public void EnsureStarted()
    {
        WindowsDualSenseReader.EnsureStarted();
        WindowsXInputReader.EnsureStarted();
    }

    public int GetGamepadStates(Span<HostGamepadState> destination)
    {
        var count = 0;
        if (count < destination.Length && WindowsDualSenseReader.TryGetState(out var dualSense))
        {
            destination[count++] = dualSense;
        }

        if (count < destination.Length && WindowsXInputReader.TryGetState(out var xinput))
        {
            destination[count++] = xinput;
        }

        return count;
    }

    public string? DescribeConnectedGamepad()
    {
        if (WindowsDualSenseReader.TryGetState(out _))
        {
            return "DualSense";
        }

        return WindowsXInputReader.TryGetState(out _) ? "Xbox controller" : null;
    }

    public void SetRumble(byte largeMotor, byte smallMotor)
    {
        WindowsDualSenseReader.SetRumble(largeMotor, smallMotor);
        WindowsXInputReader.SetRumble(largeMotor, smallMotor);
    }

    public void SetTriggerRumble(byte? leftTrigger, byte? rightTrigger) =>
        WindowsXInputReader.SetTriggerRumble(leftTrigger, rightTrigger);

    public void SetLightbar(byte red, byte green, byte blue) =>
        WindowsDualSenseReader.SetLightbar(red, green, blue);

    public void ResetLightbar() => WindowsDualSenseReader.ResetLightbar();

    public bool IsHostWindowFocused()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return false;
        }

        GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            return true;
        }

        // The GUI runs the emulator in an isolated child process. Its native
        // Vulkan surface is a child of the GUI window, so the foreground
        // window belongs to the launcher process rather than this one.
        var embeddedHostWindow = HostSessionControl.EmbeddedHostWindow;
        var hostTopLevelWindow = embeddedHostWindow == 0
            ? 0
            : GetAncestor(embeddedHostWindow, GetAncestorRoot);
        return hostTopLevelWindow != 0 && foregroundWindow == hostTopLevelWindow;
    }

    public bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private bool mousePositionValid;
    private MousePoint mousePosition;
    private bool mouseConfined;
    private readonly object mouseGate = new();
    public void ReleaseMouseCapture()
    {
        lock (mouseGate)
        {
            if (mouseConfined) { _ = ClipCursor(0); mouseConfined = false; }
            mousePositionValid = false;
        }
    }
    public bool TryReadMouse(out int deltaX, out int deltaY, out uint buttons)
    {
        lock (mouseGate) return TryReadMouseCore(out deltaX, out deltaY, out buttons);
    }
    private bool TryReadMouseCore(out int deltaX, out int deltaY, out uint buttons)
    {
        deltaX = deltaY = 0; buttons = 0;
        if (HostSessionControl.IsShutdownRequested || !IsHostWindowFocused() || !GetCursorPos(out var current))
        {
            ReleaseMouseCapture();
            return false;
        }
        if (mousePositionValid)
        {
            deltaX = current.X - mousePosition.X;
            deltaY = current.Y - mousePosition.Y;
        }
        var relative = Environment.GetEnvironmentVariable("SHARPEMU_MOUSE_RELATIVE") == "1";
        if (relative)
        {
            var window = GetForegroundWindow();
            if (GetClientRect(window, out var bounds))
            {
                var start = new MousePoint { X = bounds.Left, Y = bounds.Top };
                var end = new MousePoint { X = bounds.Right, Y = bounds.Bottom };
                if (ClientToScreen(window, ref start) && ClientToScreen(window, ref end) && end.X > start.X && end.Y > start.Y)
                {
                    var screenBounds = new MouseRect { Left = start.X, Top = start.Y, Right = end.X, Bottom = end.Y };
                    mouseConfined = ClipCursorRect(ref screenBounds) || mouseConfined;
                    if (mousePosition.X < start.X || mousePosition.X >= end.X || mousePosition.Y < start.Y || mousePosition.Y >= end.Y)
                        mousePositionValid = false;
                }
            }
        }
        else if (mouseConfined) ReleaseMouseCapture();
        if (relative && mousePositionValid)
        {
            // Opt-in source profile only. Focus/closing releases confinement.
            // If recenter fails, track the real position instead.
            if (!SetCursorPos(mousePosition.X, mousePosition.Y)) mousePosition = current;
        }
        else mousePosition = current;
        mousePositionValid = true;
        buttons = (IsKeyDown(1) ? 1u : 0u) | (IsKeyDown(4) ? 2u : 0u) | (IsKeyDown(2) ? 4u : 0u);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MousePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseRect { public int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll", EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClipCursorRect(ref MouseRect bounds);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClipCursor(nint bounds);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out MouseRect bounds);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint window, ref MousePoint point);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out MousePoint point);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hWnd, uint gaFlags);

    private const uint GetAncestorRoot = 2;
}
