# Flip lifecycle audit, 2026-10-03

The observations below describe the pre-change source. Implementation checkpoint
added 2026-10-04; final regression evidence is recorded in `local-progress.md`.

## Implementation checkpoint, 2026-10-04

- A per-submission terminal ticket carries completion back to its exact port
  instance. Presentation and cancellation resolve once. Notifications are ordered
  on a worker outside GPU locks; close invalidates late callbacks. Flip count,
  current buffer, argument, timestamps and flip events change only on presentation
  acceptance; pending count/IsFlipPending reflect outstanding tickets.
- Direct CPU, direct GPU, AGC ordered capture, translated-draw and software
  fallback paths carry the ticket. CPU/tracked draws join the presentation queue
  instead of replacing earlier accepted frames. Host resource admission is capped
  at 16 pending tickets per port (not a claim about Sony's queue capacity).
- Vulkan preserves the taken presentation and any owned immutable capture across
  Acquire/Present OutOfDate recreation. Frame-slot ownership is detached before
  draining/recreating resources. Old swapchain readback state is discarded too.
  Failed capture/render, port close, session reset and presenter teardown cancel
  tickets without incrementing the flip count or generating false flip events.
- Metal now retains tracked frames while no drawable is available and resolves
  after presentDrawable/commit. Failed source/command creation and thrown work
  cancel their tickets. This path is compiled but not natively tested on Windows.
- New unit checks cover pending/completed/cancelled status, exact-once events,
  capacity, invalid submission, two ports, close/late callback, session cancellation
  and concurrent terminal outcomes. They drive real VideoOut status/event exports
  with the same BeginFlip/ticket mechanism used by production submission.
- `Test-TouchePX5-FlipLifecycle.ps1` adds eight native cases to the GPU aggregate:
  CPU, CPU acquire/present retry, six-frame CPU burst, direct GPU, ordered GPU,
  ordered acquire/present retry. The guest independently asserts pending/status/
  event ordering; the host verifies swapchain colors. Ordered cases overwrite the
  live GPU image after capture and before presentation, so a mutable-source retry
  cannot pass. Retry diagnostics enter actual resource recreation branches.
- Final frozen-runtime validation passed on clean code commit `dfd37f2`:
  **76/76 native GPU cases** in
  `artifacts/native-gpu/20261004-090013-898/report.json` and **27/27 general
  engineering cases / 1,298 unit tests** in
  `artifacts/engineering/20261004-002458-772/report.json`.
  All 248 runtime entries match between the general post-run manifest, native
  aggregate and final runtime. General fixtures are at `8f6e4b7`.
- Human-controlled native minimize/restore passed CPU, direct GPU and ordered
  capture modes, one cycle per mode, on the unchanged runtime. Each guest checked
  pending/no-event behavior during minimization and completion after restoration;
  the host checked recreation and all three output colors. Preserved evidence:
  `artifacts/window-recovery/20261004-manual/report.json` (clean probe `bdf082d`,
  runtime code `dfd37f2`, all 248 files match the earlier aggregate).

### Remaining boundaries

An interactive real-window probe is available via
`tools/native-probes/Test-TouchePX5-FlipLifecycle.ps1 -Mode cpu -WindowRecovery`.
Direct and ordered modes are supported too; burst and injected retries are rejected.
After the first frame appears, minimize the native VideoOut window during the
30-second pause. Keep it minimized for at least 36 seconds after the first frame,
then restore before the guest's 60-second completion wait expires. The guest checks
that frame 2 has no completion/event while unavailable. The runner requires real
defer/resume log markers, swapchain recreation, successful guest assertions and
all three presented colors. The runner does not automate window control and is
intentionally excluded from unattended aggregates. Do not treat a failed or
uncontrolled trial as recovery evidence. Computer-use capture/input failures
prevented automated trials; subsequent human-controlled trials passed in all
three modes. See the report above and `local-progress.md`.

Host presentation acceptance is not physical scanout completion. `gcQueueNum`
remains unchanged until its console semantics have an independent reference.
Repeated native minimize/restore cycles, host movie overlay interaction, Metal execution,
and commercial title behavior remain unverified. Cancelling a flip does not invent
a successful event or delete a caller-owned equeue; an unrelated guest thread
waiting indefinitely on that queue still requires normal queue/session teardown.
Direct GPU flips retain an address and require the caller to respect reuse waits;
immutable versions are provided by the ordered AGC path. The final frozen-runtime
regressions verify the controlled lifecycle cases above, not these remaining
platform and title boundaries.

## Observed implementation boundaries

- `VideoOutExports.SubmitFlip` publishes CurrentBuffer, FlipCount, timestamps
  and FlipArg before attempting backend submission. Direct flips trigger the
  flip event immediately. Ordered flips schedule the event as a guest action,
  which does not establish a successful host presentation.
- `VideoOutGetFlipStatus` always encodes gcQueueNum/flipPendingNum as zero.
  `VideoOutIsFlipPending` always returns zero for a valid handle.
- `IGuestGpuBackend.TrySubmitGuestImage` has no port or submission identity.
  Its ordered counterpart has handle/index but no unique completion token.
- `ReportPresentedFrame` counts FPS only; it cannot identify a completed port
  submission. Vulkan invokes it after successful/suboptimal QueuePresent.
- Direct GPU image flips retain a mutable image address. Ordered AGC flips
  capture immutable versions before later writes and have a separate safe-to-
  render marker. These are different ownership guarantees.
- Vulkan defers taking queued flips for a minimized/zero-size window. After
  taking a flip, an AcquireNextImage OutOfDate result destroys an owned capture
  and advances _presentedSequence; QueuePresent OutOfDate likewise advances it.
  Neither branch reports a successful present. A frame may therefore be lost.
- Vulkan pending GPU image presentations are capped at four and pruned
  without a guest completion/cancellation outcome. Host-session reset clears
  them. Metal uses a separate cap of 64 and retires skipped versions.
- `VideoOutClose` only removes the port from the HLE dictionary; it does not
  cancel backend submissions. A submit can retain a port reference and a
  rented event-registration snapshot after close. Late notifications must
  validate that exact port instance before publishing state or events.

## Public reference and limits of the inference

The public PS5 SDL backend submits a framebuffer and then waits for its flip
event before advancing its two-buffer frame index:
https://github.com/ps5-payload-dev/SDL/blob/release-2.30.x-ps5/src/video/ps5/SDL_ps5video.c

This supports treating the flip event as a synchronization point for homebrew.
It does not establish every PS5 status field, queue capacity or scanout rule.
Changing PS5 status ABI semantics needs an independently observed reference.
Host QueuePresent acceptance is not proof that physical scanout has completed.

## Reviewable implementation sequence

1. Define a backend-neutral token with port generation, submission ID, buffer
   index and flipArg. Separate acceptance, image capture/safe buffer reuse,
   host presentation acceptance and cancellation. Do not conflate these.
2. Record port state while holding only _stateGate, then call the backend
   without that lock. Backend callbacks must leave backend locks before
   touching port state or delivering equeue events.
3. Reject stale completion tokens after close/reset/reopen. Complete/cancel
   each accepted submission at most once; handle duplicate and out-of-order
   notifications and negative/indexless flips explicitly.
4. Audit CPU fallback, Vulkan direct/ordered captures, Metal, host overlays,
   headless startup and queue pruning. Do not strand pending counters on any
   path. Define how host queue pruning is reported without inventing a
   console drop/completion policy.
5. Preserve ownership of the taken frame across swapchain recreation, or
   explicitly cancel it. Exercise both pre-submit Acquire OutOfDate and
   post-submit Present OutOfDate; the latter requires waiting for GPU use
   before releasing resources. Avoid holding an image reference destroyed by
   frame-slot or captured-version retirement.
6. Add delayed fake-backend integration tests through real VideoOut exports:
   before/after presentation, error acceptance, two ports, duplicate callback,
   close/reset during delay and late callbacks. Verify events and status bytes
   independently, preserving the current PS5 structure layout.
   `GuestGpu.Current` is currently a process-wide Lazy with no override seam;
   introducing a test seam must avoid leaking overrides between parallel tests.
7. Native end-to-end fixture: three immutable distinct color frames plus a
   one-buffer reuse case gated by the real flip event. Controlled deferred-
   presentation and swapchain failure hooks must verify no false completion,
   successful retry without color replacement, and unchanged resource guards.
8. Only then run actual window minimize/restore with an available authorized
   window-control surface. Pure diagnostics or unit tests do not demonstrate
   native window recovery.

The broad graphics and general engineering regressions have passed on one frozen
runtime after the lifecycle implementation, as recorded above. These results do
not establish physical scanout or commercial-game compatibility. OS-driven window
recovery has separate human-controlled evidence for one cycle per mode above.
