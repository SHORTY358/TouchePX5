# Experimental Windows native write-watch tracker

The runtime mode is selected before process startup with
`SHARPEMU_GUEST_IMAGE_CPU_SYNC=write-watch`. It is **not** the Windows default.
`HostMemory` and `WindowsHostMemory` reserve pages with MEM_WRITE_WATCH in that
mode. Existing ordinary allocations cannot be upgraded. Tracker queries reset
Windows page state once and fan changes out to every overlapping owner under
the tracker lock. DirtyEpoch, peek, consume and generation queries poll; Track
establishes a new owner's baseline without discarding old owners' changes.
Release is serialized with polling and removes aliases only after successful
VirtualFree. Native pages remain writable; no protected-page fallback is used.

Run the fresh-process HLE regression from the repository root:

```powershell
$previous = $env:SHARPEMU_GUEST_IMAGE_CPU_SYNC
try {
    $env:SHARPEMU_GUEST_IMAGE_CPU_SYNC = 'write-watch'
    C:\Dev\dotnet10\dotnet.exe run --project tools/native-probes/write-watch-tracker/WriteWatchTracker.csproj -c Release
} finally {
    $env:SHARPEMU_GUEST_IMAGE_CPU_SYNC = $previous
}
```

It verifies baseline cleanliness, writable protection, 100 same-value stores
with increasing generations, dirty fanout across shared pages, an unaffected
disjoint page, registration ownership and successful/failed release cleanup.
It also verifies that resizing a dirty range keeps its outstanding invalidation
and requests a full upload rather than stale byte spans.

For concurrent native stores and tracker polling, use the same fresh-process
environment setup and add:

```powershell
C:\Dev\dotnet10\dotnet.exe run --project tools/native-probes/write-watch-tracker/WriteWatchTracker.csproj -c Release -- --concurrent artifacts/external-homebrew/ps5link-cube/write-watch-concurrent-resize-fix.json
```

Four dedicated Windows workers execute native leaf stores to distinct words on
two shared pages; two polling workers concurrently read epochs, peek dirty flags
and query generations. A coordinator checks three owners in each of 200 rounds.
Every native call preserves 16 SysV-style red-zone markers. The probe requires
observed native write completions during polling windows, not merely multiple
threads existing. Writers join before a separate poll/free race, so it never
intentionally writes released memory. A 45-second cancellation budget and join
guard prevent freeing pages while a writer could still be running. JSON records
HLE/native-leaf hashes, observed overlap windows and counts.

This validates 25,600 stores, 409,600 marker checks and 600 owner checks in a
bounded pattern. Dirty events coalesce within rounds: the expected generation
is 200, not one generation per individual store. It exercises Windows host
workers and the actual HLE tracker, not the PS5 guest thread scheduler. Timing
is diagnostic only and is not a benchmark.
The isolated OS-only probe is separate:

```powershell
C:\Dev\dotnet10\dotnet.exe run --project tools/native-probes/red-zone-fault/RedZoneFault.csproj -c Release -- --write-watch artifacts/external-homebrew/ps5link-cube/windows-write-watch-probe.json
```

That probe preserves 16 native red-zone markers per store over 100 store/poll
cycles, detects same-value writes and checks a 1-GiB reservation with only three
committed pages. It confirms that ordinary allocations and commit-time attempts
to add write watch are unsupported on this Windows build. A shared-page reset
negative control demonstrates why aliases need centralized fanout.

For the real native cube, use the **existing unguarded red-zone input**:

```powershell
.\tools\external-homebrew\Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureCubeSlotsPatternRedZone -SeedRegisteredDisplayBuffers -UseWriteWatch -CaptureEvery 100
```

The smoke runner scopes/restores the environment and rejects unsupported-range
diagnostics. Its report records the tracking mode and HLE hash. Run the separate
patterned-background checker on its returned folder to validate pixels.

Limitations: Windows x64 / 4-KiB pages only were tested; individual queries are
bounded to 128 MiB. Ordinary CRT/mapped/external memory is not automatically
covered. Tracking is page-granular and conservatively dirties overlapping byte
ranges; it is not precise per store or a GPU/CPU synchronization primitive.
Other concurrent patterns, actual guest thread scheduling, other exception sources, additional image ownership paths,
allocation overhead and throughput need independent validation. In particular,
avoiding tracked-image write faults does not make the SysV red zone universally
safe on Windows. Default/native-fault modes are retained for explicit controls.

Primary API contract: [Microsoft GetWriteWatch](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-getwritewatch).
The implementation uses its query/reset flag instead of a separate reset call,
which would introduce a documented missed-write interval with concurrent writers.
