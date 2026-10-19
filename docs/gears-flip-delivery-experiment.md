# Native Gears flip delivery experiment — 2026-10-19

This is an opt-in compatibility experiment, not a verified universal PS5 event
contract. Default event coalescing remains unchanged.

## Reproduced failure

Capture-free pinned native Gears fails despite paced presentations:
`artifacts/external-homebrew/gears-toolchain/gears-shader-aperture-windows/runs/20261019-110121-555`
expects token 72567767433230 and receives 72567767433231. A second run after
removing unconditional diagnostic import-stack logging (`20261019-110401-764`)
fails at frame 25 with the same adjacent-token replacement pattern. Therefore
the tracing fix and presentation cadence do not resolve the queue contract.

The original title retains exact-token checks, fences and two-frame ownership.
Its source, shaders and input SELF remain unchanged by this experiment.

## Source evidence and inference

The local pinned Gears source records exact-token hardware runs on one PS5 in
`artifacts/external-homebrew/ps5-agc-gears/docs/HARDWARE_VALIDATION.md`. This does
not alone prove what happens to unread events when the guest stalls.

An independently authored PS5 implementation has an explicit pending payload
queue in [AnyPS5 Event.cpp at 6f284b8](https://github.com/boykopovar/AnyPS5/blob/6f284b82980d63dffe2211662ce1e0326eab2a39/core/libs/prx/libSceVideoOut/src/Event.cpp).
Its display trigger appends a pending event if one is already triggered rather
than replacing the unread payload. This is another project's implementation,
not console evidence. No source text was copied; our bounded ring-deque path
was extended independently. Retaining every presented token is a hypothesis
to test, not a basis for changing all event filters by default.

## Implemented subset

`TOUCHEPX5_EXPERIMENTAL_FLIP_FIFO=1` is captured when a Gen5 VideoOut port opens.
Only flip notifications use the payload-preserving path. Vblank, Gen4 ports and
the default path keep their existing behavior. Each successfully presented
ticket still resolves exactly once; no timer creates completion and no later
token is accepted as an earlier token. Same-ident/filter entries preserve arrival
order. The host bound is 16 unread matching entries, not a claimed console limit;
overflow rejects a new entry, retains every prior unread payload and emits an
error that the native wrappers reject. This bound is not a broad backpressure
or commercial-game implementation. Deleting the kernel queue discards its backlog.

Six regression cases cover default coalescing, opt-in FIFO order, overflow
without replacement, queue deletion, and per-port Gen4/Gen5 policy isolation.
The full library suite passes 1410 tests.

## Initial measured outcome

`Measure-GearsNativeNgg.ps1 -PreserveFlipPayloads -TimeoutSeconds 30` passes at
`artifacts/gears-native-timing/20261019-110810-890/timing-report.json`.
After three seconds of warmup, 1273 presentation tickets span 23.933 seconds:
mean completion rate 53.148/s, median interval 17.077 ms, p95 28.598 ms,
p99 35.510 ms, maximum 73.543 ms. It observes no diagnostic screenshot readback
or native loop failure. These are host presentation-completion times, not GPU
timestamps, calibrated benchmark results or proof of image fidelity.

A 60-second follow-up at `artifacts/gears-native-timing/20261019-111028-223`
also passes: 3047 total tickets, 2887 post-warmup tickets over 53.630 seconds,
mean 53.813/s, median 17.033 ms, p95 27.965 ms, p99 35.507 ms, maximum 66.213 ms.
Separate captured run `artifacts/gears-native-ngg/20261019-110904-927` presents
315 tickets with six swapchain captures; its final image was visually inspected
and retains clean gear shapes. This is not an independent numerical image oracle.

Use `Test-GearsNativeNgg.ps1 -RuntimeScalars -PreserveFlipPayloads` for separate
captured-image evidence. Revalidate retirement and queue semantics before
considering any wider default policy.
