// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharpEmu.Libs.Gpu;

/// <summary>One accepted guest flip; presentation and cancellation are distinct terminal outcomes.</summary>
internal sealed class GuestPresentationCompletion
{
    private static readonly ConcurrentQueue<Action> Notifications = new();
    private static readonly ConcurrentDictionary<GuestPresentationCompletion, byte> Active = new();
    private static readonly object NotificationOrder = new();
    private static int _draining;
    private readonly Action<bool, long> _notify;
    private int _outcome;

    internal GuestPresentationCompletion(Action<bool, long> notify, long minimumPresentIntervalTicks = 0)
    {
        _notify = notify;
        MinimumPresentIntervalTicks = Math.Max(0, minimumPresentIntervalTicks);
        Active.TryAdd(this, 0);
    }

    internal long MinimumPresentIntervalTicks { get; }

    public bool IsCancelled => Volatile.Read(ref _outcome) == 2;

    public void Presented() => Resolve(1);
    public void Cancel() => Resolve(2);

    // There is one process-wide guest GPU session. Called with its submission
    // gate held during teardown/reset; callbacks never acquire that gate.
    internal static void CancelSession()
    {
        foreach (var completion in Active.Keys)
            completion.Cancel();
    }

    private void Resolve(int outcome)
    {
        lock (NotificationOrder)
        {
            if (Interlocked.CompareExchange(ref _outcome, outcome, 0) != 0)
                return;

            var timestamp = Stopwatch.GetTimestamp();
            Active.TryRemove(this, out _);
            Notifications.Enqueue(() => _notify(outcome == 1, timestamp));
        }
        ScheduleDrain();
    }

    private static void ScheduleDrain()
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(static _ => Drain(), (object?)null);
    }

    private static void Drain()
    {
        // Presenters can resolve tickets under their locks. Deliver outside those
        // locks, serially, so HLE notifications cannot invert the lock order.
        try
        {
            while (Notifications.TryDequeue(out var notify))
            {
                try { notify(); }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"[LOADER][ERROR] Flip notification failed: {exception.Message}");
                }
            }
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
            if (!Notifications.IsEmpty)
                ScheduleDrain();
        }
    }
}

/// <summary>Presentation admission, advanced only after a successful host present.</summary>
internal struct GuestPresentationCadence
{
    private long _lastPresented;

    internal readonly bool CanPresent(GuestPresentationCompletion? completion, long now) =>
        completion is null || completion.MinimumPresentIntervalTicks == 0 ||
        _lastPresented == 0 || now - _lastPresented >= completion.MinimumPresentIntervalTicks;

    internal void Presented(long timestamp) => _lastPresented = timestamp;
}
