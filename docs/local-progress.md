# Local engineering checkpoint — 2026-09-25

This remains an experimental PS5 emulator. Commercial-game compatibility and GTA VI
playability have not been demonstrated. There is no supported whole-emulator completion
percentage or release-date estimate.

## Latest verified state

- Release build: no warnings/errors (`artifacts/mip-cache-final-build.log`).
  The later presentation-diagnostic-only build also passed with no warnings/errors
  (`artifacts/present-diagnostic-build.log`).
- All 1,239 unit tests passed (`artifacts/mip-cache-final-tests.log`).
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
  enhancement postdates the 17-case run; no newer whole-suite pass is claimed.
- A 5x3 base with odd-sized mip storage passed native red/blue/red checks
  (`artifacts/mip-odd-native.log`). Green unused storage texels ensure the shader
  cannot accidentally sample the ceiling-rounded physical extent. It is now the
  eighteenth aggregate case; a combined 18-case run is pending.
- All 27 general engineering cases passed after the mip-upload fix:
  `artifacts/engineering/20260924-120558-722/report.json` (source 64ec821).
  This general run predates the mip-view/cache extensions.

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

Mip layout follows AMD AddrLib GFX10: smallest level first, ceiling-rounded storage
sizes and 256-byte rows. Primary-source links and the supported subset are in
`tools/native-probes/README.md`. The earlier experimental base-first mip result in
`artifacts/native-gpu/20260924-115830-882/report.json` is superseded; it must not be
used as evidence for correct GFX10 layout.

## Scope and next work

Full-chain upload currently supports CPU-authored linear 2D RGBA8 UNORM with implicit
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
