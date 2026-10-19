# Bounded native Gears resource observation — 2026-10-19

`tools/external-homebrew/Measure-GearsNativeNgg.ps1 -PreserveFlipPayloads
-TimeoutSeconds 120 -WarmupSeconds 10 -SampleResources` measures actual host
presentation-completion times without diagnostic screenshot readback and samples
the emulator's process resources once per second. Durations up to 600 seconds
are supported; sampling remains optional and off by default.

On Windows, the executable starts a small launcher and a mitigated emulator
worker. Samples must belong to one direct child of that exact launcher, with the
internal `--sharpemu-mitigated-child` argument. Worker PID, launcher PID and
separate launcher memory are retained. No process is selected by name alone.
The timing wrapper consumes the exact invocation's returned report path instead
of guessing which directory is newest. Only that launched process tree is
terminated at the requested deadline.

## Two-minute result

`artifacts/gears-native-timing/20261019-120118-196/timing-report.json`:

- 6077 actual presented tickets; 106.739 measured seconds after presentation warmup.
- Mean completion rate 51.930/s; p95 interval 27.961 ms.
- 110 worker samples after the process warmup; first/last disjoint 20-second
  windows have private-memory medians 715315200 / 711057408 bytes.
- Private-memory median delta -4257792 bytes; observed maximum 727412736 bytes.
- Maximum working set 328724480 bytes; median handle counts 631 / 630.
- No observed native frame-loop failure or diagnostic screenshot readback.

The initial attempt (`20261019-115902-132`) sampled only the launcher. Its
`resource-qualification.json` explicitly rejects those memory figures as
emulator-resource evidence. Presentation timing from that attempt is separate.

## Five-minute result

`artifacts/gears-native-timing/20261019-120700-318/timing-report.json` passes with
the exact invocation identity and worker selection:

- 15256 presented tickets; 286.746 measured seconds after presentation warmup.
- Mean completion rate 51.432/s; p95 28.884 ms, p99 36.364 ms, maximum 113.052 ms.
- 287 post-warmup worker samples; first/last 20-second private-memory medians
  740968448 / 731303936 bytes, delta -9664512 bytes.
- Observed maximum private bytes 752390144; maximum working set 350224384 bytes.
- Median handles 631 / 626; maximum observed threads 34.
- No native frame-loop failure or diagnostic screenshot readback observed.

Both bounded runs show no positive first-to-last private-memory median growth.
They do not establish indefinite stability or a universal memory bound.

These are host snapshots, not GPU-local allocation accounting or proof that no
leak exists. The statistics do not identify the cause of allocation changes.
One-second observation adds host work; these runs are not calibrated benchmarks.
Resource warmup is relative to process launch; presentation warmup starts at the
first completed presentation. Full raw samples and runtime/input hashes remain
in the exact native invocation report.

All runs here retain pinned shader validation and require opt-in NGG, runtime
scalars and payload-preserving flip events. Default event semantics, general
games and GTA VI readiness are not certified. Image fidelity requires the
separate native numerical/pixel audit.
