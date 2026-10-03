# Flip lifecycle audit, 2026-10-03

Read-only investigation; no runtime behavior has been changed by this audit.

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

The broad graphics and general engineering regressions must run on one frozen
runtime after a lifecycle implementation. Current passing counts do not
establish these lifecycle properties or commercial-game compatibility.
