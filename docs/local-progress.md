# Local engineering checkpoint — 2026-09-26

This remains an experimental PS5 emulator. Commercial-game compatibility and GTA VI
playability have not been demonstrated. There is no supported whole-emulator completion
percentage or release-date estimate.

## Latest verified state

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
  addition is overflow checked. This adds native case 27; the full combined run
  for this revision is pending. Only the absolute indirect packet and X changes
  are covered by this new native fixture; readback performance is not optimized.
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

Next task: use the expanded diagnostics if presentation recreation recurs. Diagnose
why the surface intermittently becomes zero-sized and loses captures before
attributing it to minimization. Do not weaken pixel/count checks. Next texture work
can cover additional formats, with an independent layout reference before broadening
the supported CPU upload subset. Native odd-sized mip sampling is now covered.

Use `tools/native-probes/Test-TouchePX5-NativeGpu.ps1` for GPU checks. Keep sources and
builds under `C:\Dev\TouchePX5`; the old OneDrive triangle runner is obsolete. General
engineering checks still use the OneDrive `Test-TouchePX5-Engineering.ps1` runner.
Physical controller operation and commercial games have not been verified by these
controlled fixtures.
