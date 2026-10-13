# Guest-produced texture frame slots

This is a local adaptation of the pinned GPL-3.0 ps5link-sdk graphics sample,
following the existing two-slot, two-plane texture fixture. It adds two native
guest `scePthreadCreate` workers. Each owns one mutable texture slot and processes
300 request/completion semaphore handoffs. Main retires that slot's GPU fence
before requesting another fill, waits for CPU completion, and submits both draws.
The immutable first texture is never written by the workers. Final joins verify
distinct full-width return pointers and 300 completed jobs per worker; both
semaphores are deleted after join. The generated source preserves upstream
licensing; startup and shaders are unchanged. The adaptation explicitly compiles
with `-mno-red-zone`, so this is not an arbitrary Windows red-zone compatibility
claim.

Build existing frame-slot parents first if absent, then:

```powershell
& tools/external-homebrew/Build-PS5LinkGuestProducerFrameSlots.ps1
& tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureGuestProducers -UseWriteWatch -CaptureEvery 100
& tools/external-homebrew/Build-PS5LinkGuestProducerFrameSlots.ps1 -ReverseObjects
& tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureGuestProducersReverse -UseWriteWatch -CaptureEvery 100
```

Run `Check-PS5LinkIndependentTextureUpdates.py <capture-folder>
--require-complete-first-frame` separately on each captured output. This checks
64 known cell centers per object independently of emulator detiling/shading.
The expected second palette sequence at the current capture cadence is
1,1,2,2,0,1,1, with the first object's palette stable throughout. Matched full
captures from forward/reverse runs should also be byte-identical.

Negative control:

```powershell
& tools/external-homebrew/Build-PS5LinkGuestProducerFrameSlots.ps1 -FreezeProducerPalette
& tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureGuestProducersFrozenStatic -UseWriteWatch -CaptureEvery 100
```

The control must complete the same 600 frame submissions, 300 jobs per worker
and full-width joins, but the independent update checker must exit 1 because
the second palette stays 1 in every capture. Successful execution alone is
therefore not accepted as visible update correctness.

Scope: two slot owners, semaphore handoff, native resumed-thread execution and
CPU-authored sampled-texture updates in this scene. The default has one CPU job
outstanding at a time; all GPU submission occurs on main. This does not demonstrate two
simultaneous CPU writers, concurrent guest GPU submission, general image
ownership or commercial PS5 compatibility. Write-watch is the selected runtime
mode, not a proven prerequisite for sampled-texture updates in this fixture.

## Paired producer jobs

Build with `-BatchProducerRequests` and smoke profiles `TextureGuestProducersBatch`
and `TextureGuestProducersBatchReverse` (the latter also needs `-ReverseObjects`).
Main drains both GPU slots before issuing both requests. Workers advertise ready
generations; main releases the start gate only when both have started. Halfway
through filling, each worker waits for the peer's matching half-filled generation.
Only after both completion signals may main submit the two frames. This repeats
300 batches, verified by 300 ready/half checks per worker and both full-width joins.
The capture checks and forward/reverse whole-frame comparison remain unchanged.
Final captures also match the prior sequential producer fixture byte-for-byte.

`-BatchProducerRequests -FreezeProducerPalette` and profile
`TextureGuestProducersBatchFrozenStatic` repeat all batch/half-fill assertions but
must fail the independent update checker with seven constant palette-1 captures.
The paired variant validates two outstanding jobs and interleaved half-fill
progress on disjoint texture allocations. The barriers do not measure simultaneous
CPU instructions or throughput, and draining both GPU slots limits overlap with
rendering. Concurrent guest GPU submission remains unvalidated.
