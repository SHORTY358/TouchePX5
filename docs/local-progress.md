# Local engineering checkpoint — 2026-10-03

This remains an experimental PS5 emulator. Commercial-game compatibility and GTA VI
playability have not been demonstrated. There is no supported whole-emulator completion
percentage or release-date estimate.

## Latest verified state

- Added **two targeted storage-image atomic cases**, passing without a runtime
  change: `artifacts/compute-image-atomic-direct.log` and
  `artifacts/compute-image-atomic-queue.log`. A GPU R32_UINT image is seeded to 7,
  two workgroups add 3 then 5 to the same texel and return unique old values;
  separate GPU reads verify sums 13/23. Output guards and intentionally stale
  CPU image backing are unchanged. Graphics and sequential ACB owner-7 forms
  each require five actual GPU dispatches and reject CPU replacements.
  Clearing only GLC correctly fails the old-value check with guest 66:
  `artifacts/compute-image-atomic-negative.log`.
  These add native cases 50/51; the latest complete aggregate remains 49/49
  below. Other atomics, layouts and cross-queue image-atomic consumers are not
  established by these targeted checks. The old compute fixture also passes:
  `artifacts/compute-image-atomic-basic-regression.log`.
- Complete **49/49 native GPU regression passed** on clean source `93259af`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20261003-011856-103/report.json`.
  This covers both compute storage-write variants and the distinct-buffer
  fixture repair below, alongside all prior graphics and queue checks.
  The Release runtime still passes all 1,283 unit tests. Storage-image atomics
  and actual window minimize/restore remain separate, unverified work.
- The first 49-case aggregate on `19cbc90` failed in preserved-region sampling
  across queues: `artifacts/native-gpu/20261003-010425-117/report.json`.
  This was a fixture lifetime bug, not a new CPU/GPU color-source failure:
  `window-unavailable` delayed the red flip until after the fixture overwrote
  that same output image blue. Captures were blue/blue/red and the first color
  oracle correctly rejected them. Sample fixtures now register four distinct
  buffers (warmup plus three results) and never redraw a queued result image.
  The runner requires three distinct nonzero presented image addresses as well
  as all original pixel, queue and DMA-upload checks. Preserved sampling,
  both storage-write variants and the prior cross-queue sample pass:
  `artifacts/dma-sample-distinct-buffers.log`,
  `artifacts/dma-distinct-buffers-store.log`,
  `artifacts/dma-distinct-buffers-store-cross.log`,
  `artifacts/dma-distinct-buffers-sample-cross.log`.
  This fixes fixture buffer ownership; it does not establish end-to-end
  minimize/restore handling or change the emulator's flip-status semantics.
- Added single-pixel storage-image write/read coverage without a runtime change:
  `-DmaImageComputeStore` writes yellow at (96,48), then returns that pixel plus
  the existing updated/preserved samples into the guarded output buffer. Three
  GPU dispatches require four storage-image bindings each; readback checks 36
  RGBA values and 12 guard words total. The prior sampled frames remain exact.
  Direct and cross-queue forms pass:
  `artifacts/dma-compute-image-store.log`,
  `artifacts/dma-compute-image-store-cross-queue.log`.
  Writing row 49 while checking row 48 is rejected by the store readback check
  (guest 96 after one real GPU dispatch):
  `artifacts/dma-compute-image-store-negative.log`,
  `artifacts/compute-image-store-wrong-pixel/triangle.log`.
  The original read-only variant also passes after the shared-fixture changes:
  `artifacts/dma-compute-image-store-read-regression.log`.
  These add cases 48/49 and pass in the complete aggregate above,
  with unchanged runtime binaries. This establishes one pixel's store
  and read-after-write in these dispatches, not storage atomics, other layouts,
  all untouched source pixels or unobserved native CPU stores.
- Complete **47/47 native GPU regression passed** on clean source `24435a4`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20261003-004902-315/report.json`.
  This includes both new compute-image readback scenarios. The Release runtime
  remains the one from the ordered image-sync fix; its 1,283 unit tests pass.
- Added GPU compute-image readback coverage without a runtime change:
  `-DmaImageComputeRead` reads updated (80,45) and preserved (80,112) texels with
  integer image fetches, returning RGBA float values into a separate buffer.
  The guest checks red/red, blue/red and green/red after ordered completion,
  plus eight unchanged guard words on every pass. CPU backing in the preserved
  region remains gray. Three actual Vulkan dispatches, observed global writes,
  no CPU semantic replacement, ordered image uploads and all sampled-frame
  pixel checks are required. Both direct and cross-queue forms pass:
  `artifacts/dma-compute-image-read.log`,
  `artifacts/dma-compute-image-read-cross-queue.log`.
  A separate wrong-row negative fixture is rejected at the second compute
  readback (guest code 94 after two real GPU dispatches):
  `artifacts/dma-compute-image-read-negative.log`,
  `artifacts/compute-image-read-wrong-row/triangle.log`.
  These add native cases 46/47 and are covered by the complete aggregate above;
  runtime binaries are unchanged from the preceding 45-case result.
  Storage-image writes/atomics, other layouts and unobserved native stores are
  not established by these point reads; the newer store cases above cover only
  their stated pixel write.
- Complete **45/45 native GPU regression passed** on clean source `1092ac8`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20261003-000241-248/report.json`.
  This verifies both complete-snapshot selection and ordered partial-image
  publication fixes alongside the existing compute, texture/mip, depth,
  blending, indexed draw and queue scenarios. Commercial games, unobserved
  native CPU stores, arbitrary layouts and compute-image reads remain outside
  that aggregate; the two newer targeted cases above cover the stated reads.
- The first 45-case aggregate on `608ee75` exposed an intermittent dependent
  blend failure: `artifacts/native-gpu/20261002-235139-018/report.json`.
  Its second frame contained a blue prefix without the expected purple blend;
  an immediate rerun passed. Partial DMA had no queued image mirror, so its
  dirty CPU spans could upload only at the next frame, after the dependent draw
  and overwrite that draw. Vulkan now drains dirty image stores immediately
  after each ordered PM4 side effect, before later queued work. The dirty-epoch
  fast path still skips unchanged stores. Dependent draw/sample probes also
  require each partial upload to share the DMA action's work sequence, before
  the action completes; pixel results alone cannot hide delayed uploads.
  Direct blending, cross-queue blending and both preserved-region sampling
  variants pass on the repaired runtime:
  `artifacts/dma-ordered-image-sync-draw.log`,
  `artifacts/dma-ordered-image-sync-draw-cross.log`,
  `artifacts/dma-ordered-image-sync-sample.log`,
  `artifacts/dma-ordered-image-sync-sample-cross.log`.
  Clean build and all 1,283 unit tests pass:
  `artifacts/dma-ordered-image-sync-build.log`,
  `artifacts/dma-ordered-image-sync-tests.log`.
  The complete aggregate above passes. Metal and unobserved native writes
  have not been changed or validated by this repair.
- Fixed historical CPU snapshot selection after partial DMA: an uploaded
  generation alone does not establish full CPU contents. The Vulkan image now
  records complete CPU snapshots explicitly; partial uploads and GPU writes
  invalidate that state, full CPU uploads establish it. Existing complete CPU
  historical snapshots and newer observed write refreshes retain their paths.
  The new `-DmaImageSamplePreserved` samples the lower GPU-only red triangle
  after copying green into the upper half. CPU backing below remains gray,
  independently detecting stale CPU selection and wrong sampling coordinates.
  Before repair the final triangle was gray:
  `artifacts/dma-sample-preserved-before.log`,
  `artifacts/dma-sample-preserved-before-guest.log`.
  All three frames now pass directly and across queues:
  `artifacts/dma-sample-preserved-after.log`,
  `artifacts/dma-sample-preserved-cross-queue.log`.
  These add native cases 44/45. Clean Release build has no warnings/errors;
  all **1,283 unit tests pass**, including incomplete-mirror policy regressions:
  `artifacts/dma-sample-preserved-build.log`,
  `artifacts/dma-sample-preserved-tests.log`.
  The complete 45-case aggregate above covers these cases; the 43-case result
  below predates this fix. Scope remains linear RGBA8 at the stated coordinates, not arbitrary
  subresources, unobserved native CPU stores or compute-image reads.
- Complete **43/43 native GPU regression passed** on clean source `926c1e2`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20260929-124502-959/report.json`.
  This includes all partial-row, dependent blending and texture-consumer cases.
- Fixed a sampled-render-target overwrite when native write-fault tracking is
  disabled but managed write generations remain available. The legacy nonzero
  CPU-texel heuristic now applies only to untracked snapshots (generation -1),
  so it cannot promote a tracked GPU-only render target into a stale CPU source.
  `-DmaImageSample` renders a red source triangle while its CPU backing remains
  gray, samples that source into a distinct output, then samples blue/red after
  prefix DMA fill/copy. Before the fix the first output failed color checks:
  `artifacts/dma-sample-before.log`, `artifacts/dma-sample-before-guest.log`.
  All three captures now pass: `artifacts/dma-sample-native.log`. The cross-queue
  form passes too: `artifacts/dma-sample-cross-queue.log`. It keeps DMA-only ACB
  producers and executes texture draws in the resumed graphics consumers.
  Cases 42/43 are added. Clean build and all 1,279 unit tests pass:
  `artifacts/dma-sample-build.log`, `artifacts/dma-sample-tests.log`.
  Ordinary texturing and cross-queue blending also pass. Native CPU writes with
  fault tracking disabled remain unobserved; this does not establish their
  coherence, arbitrary sampling coordinates/layouts or compute-image reads.
- Cross-queue dependent blending now passes as well, without a runtime change:
  `artifacts/dma-draw-cross-queue.log`. `-DmaImageDraw -DmaImageCrossQueue`
  submits the waiting graphics consumer first, then ACB owner 7 performs the
  prefix DMA and signals readiness. Only the resumed graphics queue executes
  the blend and publishes completion. The runner requires two real waits and
  unforced resumes, the DMA-only producer lengths (21/15 words), graphics
  continuation lengths (23/17 words), three translated draws and the same pixel
  oracle/negative controls. This adds case 41, not a full 41-case aggregate.
  Texture sampling, compute image reads and arbitrary scheduling remain outside
  this result. No emulator fix was required for either dependent-blend variant.
  Existing full-image/prefix/span cross-queue regressions pass after the fixture
  split: `artifacts/dma-draw-split-{full,prefix,span}-regression.log`.
- Added dependent-draw coverage without a runtime change: `-DmaImageDraw`
  fills the first 90 rows blue, immediately blends a half-alpha red triangle in
  the same graphics submission, then repeats with a red DMA copy and blue blend.
  No flip separates DMA from its dependent draw. Expected purple intersections,
  preserved lower geometry and unchanged outside pixels all pass on Vulkan:
  `artifacts/dma-draw-native.log`. Three translated draws and two DMA operations
  are required. Blank, DMA-only (missing draw) and wrong-frame controls fail.
  This adds case 40; it is not a full 40-case aggregate result. It establishes
  destination-image blending after DMA in this queue, not texture sampling,
  compute image reads, all scheduling interleavings or transient label waits.
  Shared-fixture prefix, partial-row span and ordinary blend checks also pass:
  `artifacts/dma-draw-{prefix,span,blend}-regression.log`. Runtime binaries are
  unchanged from the preceding pixel-span fix; no new unit run was necessary.
- Extended preservation to pixel-aligned spans starting/ending inside a row on
  initialized linear RGBA8 UNORM single-mip images. Each span becomes a leading
  row fragment, complete middle rows, and a trailing fragment; GPU pixels outside
  these regions remain untouched. Byte-unaligned writes and other layouts still
  use the historical fallback. Immediate visibility before the CPU-image drain
  remains unverified.
  `-DmaImageSpan` updates pixels [14480,43440), starting at (80,45) and ending
  before (240,135), using both fill and copy. It failed before at (0,0):
  `artifacts/dma-span-before.log`, `artifacts/dma-span-before-guest.log`.
  After repair it passes directly and across queues:
  `artifacts/dma-span-after.log`, `artifacts/dma-span-cross-queue.log`.
  The preservation oracle checks solid/untouched bilinear footprints against
  exact colors/the first verified frame, and validates color/alpha channels at
  mixed boundaries. Constructed positive, blank and full-overwrite controls run
  before actual output. Cases 38/39 are added; the previous complete 37-case
  report below is not a 39-case result. Clean build and 1,279 unit tests pass:
  `artifacts/dma-span-build.log`, `artifacts/dma-span-tests.log`.
  Six targeted GPU cases pass on this runtime: both span variants, both prior
  prefix variants, full-image DMA and ordinary texturing. Evidence is in
  `artifacts/dma-span-{after,cross-queue,prefix-regression,prefix-cross-regression,full-regression,texture-regression}.log`.
- Complete **37/37 native GPU regression passed** on clean source `c9f231c`,
  with unchanged runtime files throughout the run:
  `artifacts/native-gpu/20260929-021256-372/report.json`.
  This includes both formerly failing prefix-preservation variants and all
  existing compute, queue, texture, mip, depth, blend and indexed-draw scenarios.
- **Partial DMA preservation fixed for tested linear RGBA8 row updates.**
  Managed write tracking now retains up to 64 disjoint byte spans, merges adjacent
  chunks, and consumes spans plus their generation atomically. Vulkan uploads
  only those rows for initialized, single-mip/depth linear RGBA8 UNORM images
  with tightly packed, whole-row writes. It does not clear unrelated later writes
  or describe a partial mirror as a complete CPU snapshot. Failed reads re-dirty
  their spans. Native fault tracking, unsupported layouts, partial rows and span
  overflow retain the previous full-upload behavior; their preservation is not
  claimed. Metal has not been changed.
  The formerly failing `-DmaImagePrefix` now passes, as does its cross-queue
  variant: `artifacts/dma-region-prefix-after.log` and
  `artifacts/dma-region-prefix-cross-queue.log`. Both preserve the GPU-only lower
  triangle through blue fill/red copy of the first 90 rows, CPU completion,
  source reuse and presentation. These add suite cases 36/37. This is presentation
  preservation coverage, not proof of immediate visibility to a dependent draw
  before the CPU-image drain, or transient waits on image memory.
  Clean Release build and **1,279 unit tests pass**, including merged/disjoint
  spans, consume/reset, generation advancement and bounded-overflow controls:
  `artifacts/dma-region-build.log`, `artifacts/dma-region-tests.log`.
  Before evidence remains `artifacts/dma-prefix-preservation-before.log` and
  `artifacts/dma-prefix-preservation-before-guest.log`: CPU words were correct,
  but the second capture failed at (0,542) because a 230,400-byte CPU upload
  overwrote untouched GPU pixels. Earlier `dma-prefix-before*` artifacts had an
  ambiguous deliberate CPU write and are superseded by these preservation logs.
- Added and passed `-DmaImageCrossQueue`: `artifacts/dma-image-cross-queue.log`.
  The graphics queue genuinely suspends twice while ACB owner 7 fills/copies an
  existing image, then resumes through separate ready/completion labels. All
  three GPU captures pass the existing pixel oracle; both complete image updates
  and their deferred callbacks execute on ACB. No forced resume or deadlock
  fallback occurred. This adds suite case 35, without changing emulator runtime.
  It verifies full-image transfer completion between queues, not transient waits
  on image data. An initial fixture pointer lacked relocation; materializing it
  at runtime corrected the guest. The runner counts wait diagnostics because
  detailed packet traces are throttled. The full-suite result below remains the
  previous 34-case run; do not report it as a 35-case aggregate.
  Sequential DMA image and ordinary triangle regressions also pass:
  `artifacts/dma-image-sequential-regression.log` and
  `artifacts/dma-image-triangle-regression.log`. The last build/unit result is
  unchanged because this change only extends native fixtures and runners.
- Added and passed a native existing-image DMA baseline, without a runtime fix:
  `artifacts/dma-image-native.log`. A red triangle establishes the image, then
  DMA fills it blue and copies a red source into it. All 57,600 CPU words are
  checked after each completion. The source becomes green after copy completion;
  the final GPU image must remain red. Both full-image captures pass all 2,037,120
  BGRA pixels, with blank/wrong-color negative controls. Logs require two successful
  230,400-byte writes to an existing backend image and one translated draw.
  All **34/34 native GPU cases pass** on clean source `dced885` with unchanged
  runtime files: `artifacts/native-gpu/20260928-103751-009/report.json`.
  This includes the new DMA signal and image cases in one complete run. Sequential mirror
  ordering is established, not transient waits on image memory or a cross-queue
  mirror race. The runtime and last 1,276-test unit result are unchanged.
- Plain-memory DMA copy/fill now preserves a satisfied wait before a later DMA
  resets the label. Two submitted-packet regressions failed before and two
  nonsatisfying controls passed (`artifacts/dma-label-before.log`). The native
  cross-queue fixture also failed before with guest result 78 after losing a
  signal: `artifacts/dma-label-native-before-guest.log`. After the fix it passes
  red/red/blue pixels, six immediate DMA fills, four computes/four draws, final
  reset-label values and no fallback (`artifacts/dma-label-native-after.log`).
  WRITE_DATA and reused-label native regressions pass on this same build:
  `artifacts/dma-label-write-data-regression.log`,
  `artifacts/dma-label-reuse-regression.log`. Release build has zero warnings/errors;
  all **1,276 unit tests pass** (`artifacts/dma-label-build.log`,
  `artifacts/dma-label-tests.log`). Native case 33 is added and passed individually;
  the full 32-case report below predates this DMA fix, and no full 33-case pass is
  claimed. Immediate latching is restricted to destinations without a known
  backend image; image-mirror completion and transient labels remain separate work.
- Rewind valid-bit patches now latch full overlapping label values, using the
  same width-aware path as GPU releases. Previously a 32-bit patch incorrectly
  satisfied a 64-bit equality wait for `0x80000000`, and missed the correct
  `0x1234567880000000` value. Both regressions fail before the change; the existing
  narrow-wait control passes (`artifacts/rewind-label-before.log`). All **1,272
  unit tests pass** after the fix, with zero build warnings/errors:
  `artifacts/rewind-label-build.log`, `artifacts/rewind-label-tests.log`.
  All **32/32 native GPU cases pass** on clean source `3636660`, with unchanged
  runtime files: `artifacts/native-gpu/20260928-075326-613/report.json`.
  Image-backed DMA producers need a
  separate completion-aware design: their CPU copy can enqueue a later GPU image
  mirror, so immediately latching after the copy could wake dependent work early.
- Fixed lost WRITE_DATA satisfaction when a later payload word resets a label
  (same-address writes) or changes the upper half of a wider label. Each successful
  word write now latches matching overlapping waiters before the next word.
  Four submitted-packet cases failed before; two nonsatisfying controls passed.
  All six pass after: `artifacts/write-data-direct-{before,after}.log`.
  The direct assertion runner was used after two transient VSTest socket 10013
  errors; the normal full runner subsequently recovered without system changes.
  Release builds with zero warnings/errors and **1,269 unit tests pass**:
  `artifacts/write-data-wait-build.log`, `artifacts/write-data-wait-tests.log`.
  The new native WRITE_DATA cross-queue probe passes red/red/blue pixel checks,
  four computes/four draws and three signal/reset operations without fallback:
  `artifacts/write-data-cross-queue-native.log`. The padded ABGR/AGC-fence native
  regression passes too (`artifacts/write-data-fence-native.log`). Case 32 is
  added to the aggregate; it has been tested individually, not in a full 32-case run.
  An initial fixture incorrectly reset the second signal before its consumer
  registered; it correctly remained suspended. That signal is now retained,
  and transient resets test only already-registered waiters. Evidence retained
  in `artifacts/write-data-native-fixture-ordering.log`.
- Fixed partial RELEASE_MEM writes falsely satisfying a wider waiter. A real
  submitted 32-bit release wrote `0x1234567800000001`, but previously latched a
  64-bit equality wait for `1` using only the packet payload. The before test
  fails with the waiter incorrectly removed: `artifacts/partial-release-before.log`.
  Both standard and Gen5 releases now inspect complete overlapping labels at
  the ordered write point. Upper-word waiters and narrow writes completing a
  wide value retain satisfaction across a later reset; unreadable/unrelated
  labels do not latch. Six regressions cover these cases. Release build has
  zero warnings/errors and all **1,263 unit tests pass**:
  `artifacts/partial-release-build.log`, `artifacts/partial-release-tests.log`.
  All **31/31 native GPU cases pass** on clean source `87af501`:
  `artifacts/native-gpu/20260928-010710-924/report.json`. The runner verified
  unchanged runtime files throughout. A subsequent interactive recovery attempt
  could not operate the window (`coordinate input geometry is unavailable`, then
  `FrameArrived timed out`); its strict diagnostic correctly failed for absent
  deferral/restoration markers. Evidence: `artifacts/window-recovery-87af501.log`
  and `artifacts/window-recovery-87af501-guest.log`. Recovery remains unverified.
- On the preceding `c4e0d7a` runtime, the five cases left unfinished by the
  interrupted aggregate passed individually: BC1, BC1 2D, BC3, index16 and
  index32/scissor (`artifacts/remaining-*.log`). The legacy padded ABGR/fence
  scenario also passed all 2,037,120 Vulkan pixels and 57,600 guest pixels:
  `artifacts/window-guard-fence.log`. These individual results do not turn the
  earlier failed aggregate into a passed run, or verify minimize/restore.
- The full 31-case run on `db78dfa` **failed after 26 passed cases**, at BC1
  presentation: `artifacts/native-gpu/20260927-124329-243/report.json`.
  Its guest log explicitly reports `windowState=Minimized`, a 0x0 surface and
  `ErrorOutOfDateKhr`; two captures are missing. Both cross-queue cases passed
  in that run. Do not describe this as 31/31 passed.
- Work in progress: defer taking a queued presentation while the native window
  is minimized or has a zero-size framebuffer, continue guest GPU work, and
  recreate the swapchain before taking the next presentation after restoration.
  Build passes (`artifacts/window-recovery-build.log`), all 1,257 unit tests pass
  (`artifacts/window-recovery-tests.log`). The window diagnostic has reproduced
  the original missing capture (`artifacts/window-recovery-before-guest.log`).
  A later run observed deferral but could not complete restoration through the
  UI tool (repeated user-input conflict), so end-to-end recovery remains unverified.
  An earlier interrupted run observed deferral/restoration but its window closed
  before the fixture completed: `artifacts/window-recovery-interrupted-guest.log`.
  This guard does not yet address minimization racing with an already-taken frame,
  or retaining every historical image when guest buffers are reused while hidden.
- Fixed premature cross-queue wakeup after CPU reset/reuse of a completion label.
  `CollectDeadlockBroken` now requires satisfaction latched for the current waiter;
  an older producer value cannot release a later waiter just because 500 ms elapsed.
  The native before case returned 77 after `agc.deadlock_break` resumed graphics
  before its new compute producer was submitted:
  `artifacts/cross-queue-reuse-before-guest.log`. After the fix, both ordinary
  and reused-label tests pass red/red/blue pixels, four GPU computes/four draws,
  genuine suspension/resume and no fallback: `artifacts/cross-queue-after.log`
  and `artifacts/cross-queue-reuse-after.log`. The consumer is submitted first;
  the reused-label case withholds producers for 750 ms and checks that completion
  does not advance. Release builds passed and all **1,257 unit tests** passed
  (`artifacts/cross-queue-build.log`, `artifacts/cross-queue-tests.log`). The new
  unit regression also verifies a fresh signal survives a subsequent label reset.
  Native suite now contains 31 cases; the interrupted full run is recorded above.
  This verifies one compute queue feeding graphics, not general queue concurrency.
- All **28/28 native GPU cases** and **27/27 general engineering cases** passed
  on clean source `d0c0584`:
  `artifacts/native-gpu/20260926-095224-350/report.json` and
  `artifacts/engineering/20260926-094941-679/report.json`.
  The 248-entry GPU runtime manifest exactly matches the general run's
  `runtime-after.json`; that snapshot also matches one taken during the general
  run after its build completed. The build/unit stage again passed all 1,256 tests.
- After those full runs, the fixture gained a separate ACB compute-queue mode.
  Five submissions to owner 7 pass the direct atomic baseline and four indirect
  dimension transitions, with exact GPU execution counts, completion and guards:
  `artifacts/indirect-dimensions-acb-after.log`. The ordinary compute, absolute
  graphics-queue and base-relative variants were all rerun after the shared import
  extension (`artifacts/indirect-dimensions-final-{compute,absolute,base}.log`).
  No emulator/runtime change was needed. This is newly integrated case 29, tested
  individually; the full 28-case report above predates the fixture extension.
  Cross-queue producer/consumer dependencies and concurrency are not established.
- Latest Release build: no warnings/errors (`artifacts/indirect-dimensions-build.log`).
- All 1,256 unit tests passed (`artifacts/indirect-dimensions-tests.log`).
- Indirect compute now synchronizes its 12-byte dimension buffer before CPU
  interpretation. The native atomic counter baseline passed before the fix, but
  the following GPU producer changed X from 1 to 2 while the consumer still ran
  once (guest result 91): `artifacts/indirect-dimensions-before-guest.log`.
  The corrected fixture passes 1->2, 2->1, 0->2 and 2->0, including completion
  and adjacent-buffer guards: `artifacts/indirect-dimensions-zero-after.log`.
  The unused retry-address output and misleading zero-means-pending comments
  were removed; a synchronized zero emits no compute work. Base-relative address
  addition is overflow checked. All **27/27** native cases passed on clean source
  `501aeeb`: `artifacts/native-gpu/20260926-094145-880/report.json`.
  Readback performance is not optimized.
- A subsequent fixture extension tests the base-relative three-dword indirect
  packet with a nonzero 16-byte offset, poison prefix dimensions, and GPU changes
  to X, Y and Z. Both extended variants passed individually without runtime
  changes: `artifacts/indirect-dimensions-base-after.log` and
  `artifacts/indirect-dimensions-expanded-after.log`. This adds native case 28;
  the later full result is recorded above. Separate compute-queue submission is
  tested by the subsequent ACB fixture above, not by either graphics variant.
- GPU-produced argument records are now synchronized even when CPU counts are
  already nonzero. The before probe issued all four computes and four draws but
  sampled stale depth (last frame red): `artifacts/indirect-gpu-args-before-guest.log`.
  Fresh ordered argument-span readback fixes the same probe to red/red/blue:
  `artifacts/indirect-gpu-args-after.log`. Completion is checked, range overflow
  rejected, and the old empty-read throttling/1024-record partial synchronization
  removed. All **26/26** native cases passed on clean source `a5626f9`:
  `artifacts/native-gpu/20260926-041323-524/report.json`. Synchronous readback cost
  is not yet optimized; this establishes one controlled compute-to-draw path.
- GPU-produced multi-draw counts now use a fresh ordered four-byte readback on
  every counted packet, with checked completion. Reusing the argument helper's
  throttling previously consumed stale counts after later compute dispatches.
  Native before: four computes but only two raster draws
  (`artifacts/indirect-gpu-count-before-guest.log`). Native after: four computes,
  four raster draws and correct red/red/blue pixels
  (`artifacts/indirect-gpu-count-after.log`). All **25/25** native cases passed
  on clean source `ee2af67`: `artifacts/native-gpu/20260926-040303-359/report.json`.
  GPU-produced argument-record coverage is added separately above.
- Indexed indirect multi-draw now uses the hardware 10-dword packet, correct
  stride offset, direct/memory-count limits and shader register destinations.
  The native two-record probe failed before the fix and passes afterward:
  `artifacts/indirect-multi-before-guest.log`, `artifacts/indirect-multi-after.log`.
  Seven unit cases verify count/stride/register behavior and exact builder bounds,
  including literal zero stride reusing one record. All **24/24** native cases
  passed on clean source `84ae366`:
  `artifacts/native-gpu/20260926-035337-215/report.json`.
  All 27 general cases passed on `abd5b6a` before the final
  zero-stride refinement: `artifacts/engineering/20260925-225914-436/report.json`.
  VGPR destination mode remains unsupported; GPU-produced count synchronization
  is covered separately by the new fixture above.
- The preceding general run `artifacts/engineering/20260925-225645-871/report.json`
  failed on its two-frame test. This time diagnostics explicitly reported
  `windowState=Minimized`, framebuffer/surface 0x0 and ErrorOutOfDateKhr, losing
  the second capture. A targeted diagnostic passed afterward
  (`artifacts/indirect-multi-two-frames-diagnostic-guest.log`), then the fresh full
  general run above passed. The failed report is retained; minimization handling
  is not fixed by the indirect-draw changes.
- Single indirect draws now copy argument values into the packet-selected shader
  registers. A native depth probe failed before this fix (third frame remained red)
  and passed afterward (red/red/blue): `artifacts/indirect-register-before-guest.log`
  and `artifacts/indirect-register-after.log`. Unit checks cover both single packet
  variants, nonzero argument offsets and a signed indexed base-vertex bit pattern.
  All **23/23** native cases passed on clean source `38a14ab`:
  `artifacts/native-gpu/20260925-224622-652/report.json`. The 22-case and general
  runs below predate this fix. General base-instance semantics remain separate.
- The first post-fix probe hit the old runner's 20-second log deadline although
  its guest later completed successfully. Preserved log:
  `artifacts/indirect-register-after-timeout-guest.log`. The runner now waits for
  its exact child process with a 60-second bound, then checks exit/log/pixels.
- Initial cache revision passed all 15 native Vulkan cases:
  `artifacts/native-gpu/20260925-005443-656/report.json`.
- Final cache ownership/trace revision passed all three mip view cases
  (`artifacts/mip-cache-final-Mip*.log`) and the new pressure case
  (`artifacts/mip-cache-pressure.log`). Pressure verified 43 draws, 42 uploads,
  one cache hit and one deferred eviction of 32 entries, with correct pixels.
- Earlier expanded aggregate attempts
  `20260925-005926-364` and `20260925-010003-464` under `artifacts/native-gpu`
  failed on missing captures in ordinary triangle / blend respectively. Logs show
  `vkQueuePresentKHR: ErrorOutOfDateKhr` and a zero surface extent. They remain
  failed evidence, not retries silently converted to success.
- Three subsequent blend diagnostic runs passed (`artifacts/present-diagnostic-blend-*.log`)
  without triggering recreation. The diagnostic logs now distinguish surface extent,
  framebuffer size, window state and shutdown requests. No presentation fix is claimed.
- A nonuniform 2x2 lower mip under a 4x4 base passed native red/blue/red checks,
  including second-row texel selection and rewriting (`artifacts/mip-2d-native.log`).
  All **17/17** aggregate cases subsequently passed together on clean source
  `b25a8ce`: `artifacts/native-gpu/20260925-010649-774/report.json`.
  This successful run does not establish that the intermittent surface issue is fixed.
- The runner now records and compares a manifest of EXE/DLL files (including
  subdirectories), deps and runtime configuration, instead of checking only the
  app host EXE. The helper was checked with unchanged files, a same-length DLL
  modification and added runtime config; all behaved correctly. The local runtime
  manifest contains 248 files (`artifacts/runtime-manifest-current.json`). This runner
  enhancement was subsequently exercised by the 18-case run below.
- A 5x3 base with odd-sized mip storage passed native red/blue/red checks
  (`artifacts/mip-odd-native.log`). Green unused storage texels ensure the shader
  cannot accidentally sample the ceiling-rounded physical extent. It is now the
  eighteenth aggregate case. All **18/18** passed together on clean source `ec8cc06`:
  `artifacts/native-gpu/20260925-011332-977/report.json`, including verification of
  all 248 runtime manifest entries.
- Native sRGB mip test reproduced a wrong first-level sample (blue=55 instead of
  red=55, alpha=128). Allowing RGBA8 number type 9 through the full-chain reader
  fixed the same probe: `artifacts/mip-srgb-before.log`, `mip-srgb-before-guest.log`,
  `mip-srgb-after.log`. Three frames verify RGB decoding (128 -> 55, 64 -> 13 on
  UNORM output), alpha remaining 128, cache reuse and lower-level-only changes.
  All **19/19** aggregate cases passed on clean source `551cc2f`:
  `artifacts/native-gpu/20260925-082252-052/report.json`.
- BC1 UNORM native mip sampling failed before the block-aware reader and passed
  afterwards (`artifacts/mip-bc1-before.log`, `mip-bc1-before-guest.log`,
  `mip-bc1-after.log`). The new case requires red/blue/red, correct opaque alpha,
  one unchanged reuse and two uploads. All **20/20** passed on source `844915f`:
  `artifacts/native-gpu/20260925-083835-186/report.json`.
- All **27/27** general engineering cases passed on source `844915f`:
  `artifacts/engineering/20260925-083517-069/report.json`. Its saved
  `runtime-after.json` exactly matches all 248 runtime entries in the subsequent
  20-case GPU report above. An earlier GPU run preceded this rebuild and is not
  used to claim identical binaries.
- The new BC1 multi-row probe passed independently:
  `artifacts/mip-bc1-2d-native.log`. A 16x16 base follows an 8x8 lower level;
  sampling its bottom-right block gives blue, then red after a rewrite.
  All **21/21** passed together on source `4398a1c`:
  `artifacts/native-gpu/20260925-134214-928/report.json`.
- BC3 UNORM reproduced blue instead of red at the base level before the fix:
  `artifacts/mip-bc3-before-evidence.json` and `mip-bc3-before-guest.log`.
  Extending the block-aware reader to 16-byte BC3 blocks fixed the native probe:
  `artifacts/mip-bc3-after.log`. Frames verify red/blue/red with alpha 128/128/64,
  one cache hit and two uploads. Unit cases cover odd dimensions, rows above
  256 bytes, truncation and unsupported BC3 sRGB. All **22/22** native cases passed
  on clean source `47ec2fc`: `artifacts/native-gpu/20260925-223516-947/report.json`.
  The general engineering run also passed all 27 cases:
  `artifacts/engineering/20260925-135039-796/report.json`. A runtime manifest saved
  at resumption matches all 248 entries of the new native GPU report.

The native cases cover compute, repeated raster draws, clipping, blending, depth,
texture CPU rewrites, 2D texel selection, interpolated UVs, perspective correction,
bilinear filtering, explicit mip selection, restricted mip views, and 16/32-bit indices.
Pixel oracles reject blank output and wrong colors; perspective and filtering also
reject the corresponding incorrect affine/nearest-neighbor expectations.

## Actual runtime fixes

1. DRAW_INDEX_2 now honors its inline index address and maximum count instead of stale
   queue state (1f19e83).
2. First observation of an untracked texture no longer incorrectly certifies stale GPU
   content as current; a native CPU red-to-blue rewrite is visible (3dd8fbd).
3. CPU-authored linear RGBA8 mip chains now upload all levels to Vulkan subresources
   (b372dd2), and restricted views select the correct base/count. Native tests verify
   red/blue/red after changing only the lower level, base-only red/red/red, and
   lower-only blue/blue/red.
4. Identical full CPU mip snapshots reuse a Vulkan upload; any changed level,
   layout, view or sampler gets a separate resource. CPU snapshots remain fresh.
   Cache admission is capped at 32 entries / 16 MiB of CPU payload; GPU and staging
   allocations are additional. Retirement waits for previously submitted GPU work.
5. CPU RGBA8 sRGB mip chains now use the full-chain path instead of interpreting
   the allocation's lowest mip as the base texture. Vulkan performs sRGB sampling.
6. CPU BC1 UNORM mip chains calculate storage in compressed blocks, retain texel
   dimensions for sampling and provide texel-unit row lengths to Vulkan copies.
7. CPU BC3 UNORM mip chains use the same layout with 16-byte blocks and preserve
   independently encoded alpha during sampling and lower-level rewrites.

Mip layout follows AMD AddrLib GFX10: smallest level first, ceiling-rounded storage
sizes and 256-byte rows. Primary-source links and the supported subset are in
`tools/native-probes/README.md`. The earlier experimental base-first mip result in
`artifacts/native-gpu/20260924-115830-882/report.json` is superseded; it must not be
used as evidence for correct GFX10 layout.

## Scope and next work

Full-chain Vulkan upload currently supports CPU-authored linear 2D RGBA8 UNORM/sRGB or BC1/BC3 UNORM with implicit
width-derived pitch and no existing backend image at the allocation address. It uses
per-draw CPU snapshots with exact-content Vulkan reuse. Tiled/array/storage/GPU-written
chains and broad commercial-game compatibility remain separate work.

Next tasks: validate minimize/restore with a functioning window-control path,
extend partial-DMA coverage to byte-unaligned and non-linear layouts, and go beyond
the passing sequential and cross-queue
full-image transfers before changing its completion semantics. A transient-image
wait regression must distinguish the CPU copy, queued GPU image update, and waiter
release, including a reset between them and failed/unreadable writes.
Plain-memory DMA and rewind patches now have separate regressions above.
The bounded row-upload path now preserves separate managed-write spans. Do not
clear a dirty flag to suppress DMA writes: that could discard independent CPU
writes. Dependent destination-image blending before presentation now passes.
Texture sampling, read-only compute fetches and single-pixel storage writes now
have separate direct/cross-queue regressions above. Single-texel R32_UINT atomic
add now has independent graphics/ACB checks. Further work should cover other
atomics, other layouts and failed/unreadable writes without
inferring those capabilities from the passing point-read/store cases.
Minimized 0x0 surfaces are confirmed in the earlier failed
aggregate; recovery remains unverified. Do not weaken pixel/count checks. Next texture work
can cover additional formats, with an independent layout reference before broadening
the supported CPU upload subset. Native odd-sized mip sampling is now covered.

Use `tools/native-probes/Test-TouchePX5-NativeGpu.ps1` for GPU checks. Keep sources and
builds under `C:\Dev\TouchePX5`; the old OneDrive triangle runner is obsolete. General
engineering checks still use the OneDrive `Test-TouchePX5-Engineering.ps1` runner.
Physical controller operation and commercial games have not been verified by these
controlled fixtures.
