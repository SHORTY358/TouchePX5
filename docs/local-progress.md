# Local engineering checkpoint — 2026-09-24

This remains an experimental PS5 emulator. Commercial-game compatibility and GTA VI
playability have not been demonstrated. There is no supported whole-emulator completion
percentage or release-date estimate.

## Latest verified state

- Release build: no warnings/errors (`artifacts/mip-views-final-build.log`).
- All 1,235 unit tests passed (`artifacts/mip-views-final-tests.log`).
- All 15 native Vulkan cases passed together:
  `artifacts/native-gpu/20260924-121116-745/report.json`.
- All 27 general engineering cases passed after the mip-upload fix:
  `artifacts/engineering/20260924-120558-722/report.json` (source 64ec821).
  This general run predates the final mip-view extension; that extension was validated
  by the latest unit tests and 15-case GPU run above.

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

Mip layout follows AMD AddrLib GFX10: smallest level first, ceiling-rounded storage
sizes and 256-byte rows. Primary-source links and the supported subset are in
`tools/native-probes/README.md`. The earlier experimental base-first mip result in
`artifacts/native-gpu/20260924-115830-882/report.json` is superseded; it must not be
used as evidence for correct GFX10 layout.

## Scope and next work

Full-chain upload currently supports CPU-authored linear 2D RGBA8 UNORM with implicit
width-derived pitch and no existing backend image at the allocation address. It uses
per-draw snapshots/uploads for correctness. Tiled/array/storage/GPU-written chains,
cache optimization and broad commercial-game compatibility remain separate work.

Next useful task: measure and safely cache unchanged full mip-chain uploads, preserving
lower-level-only CPU updates and per-draw snapshots. Extend tests before optimizing.
Alternatively, validate another texture format/layout against a primary reference.

Use `tools/native-probes/Test-TouchePX5-NativeGpu.ps1` for GPU checks. Keep sources and
builds under `C:\Dev\TouchePX5`; the old OneDrive triangle runner is obsolete. General
engineering checks still use the OneDrive `Test-TouchePX5-Engineering.ps1` runner.
Physical controller operation and commercial games have not been verified by these
controlled fixtures.