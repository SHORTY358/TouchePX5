// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Globalization;
using SharpEmu.HLE.Host;

namespace SharpEmu.Libs.Keyboard;

// Opt-in bounded native-game test input. It never sends OS keyboard events.
internal sealed class KeyboardReplayInput : IHostInput
{
    internal const int MaximumReplayMilliseconds = 120000;
    private readonly (int Start, int Key, int Duration)[] steps;
    private readonly Func<long> elapsed;

    internal KeyboardReplayInput(string script, Func<long>? elapsed = null)
    {
        if (script.Length > 4096) throw new ArgumentException("Keyboard replay is too long.");
        var pieces = script.Split(';');
        if (pieces.Length > 64) throw new ArgumentException("Keyboard replay has too many steps.");
        steps = pieces.Select(piece =>
        {
            var values = piece.Split(':');
            if (values.Length != 3 ||
                !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var key) ||
                !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out var duration) ||
                start > MaximumReplayMilliseconds || key is < 1 or > 255 || duration is < 1 or > 5000 || start + duration > MaximumReplayMilliseconds)
                throw new ArgumentException("Keyboard replay step must be start_ms:virtual_key:duration_ms, ending within 120 seconds.");
            return (start, key, duration);
        }).ToArray();
        var clock = Stopwatch.StartNew();
        this.elapsed = elapsed ?? (() => clock.ElapsedMilliseconds);
    }
    public bool IsHostWindowFocused() => true;
    public bool IsKeyDown(int key)
    {
        var now = elapsed();
        return steps.Any(step => step.Key == key && now >= step.Start && now < step.Start + step.Duration);
    }
    public void EnsureStarted() { }
    public int GetGamepadStates(Span<HostGamepadState> destination) => 0;
    public string? DescribeConnectedGamepad() => null;
    public void SetRumble(byte largeMotor, byte smallMotor) { }
    public void SetTriggerRumble(byte? leftTrigger, byte? rightTrigger) { }
    public void SetLightbar(byte red, byte green, byte blue) { }
    public void ResetLightbar() { }
}
