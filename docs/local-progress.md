# Local engineering checkpoint — 2026-09-24

This checkout is an experimental emulator. Commercial PS5 game compatibility and GTA VI
playability have not been demonstrated. No supported overall completion percentage or
completion date can be inferred from the tests below.

## Verified

- DRAW_INDEX_2 now reads its inline index-buffer address and maximum count, rather than
  accidentally using stale queue state. Both 16-bit and 32-bit native indexed draws pass.
- The rebuilt executable passed 1,225 unit tests after that parser fix:
  `artifacts/draw-index2-tests.log`.
- Seven native Vulkan scenarios passed together:
  `artifacts/native-gpu/20260924-064315-212/report.json`.
  They cover repeated compute dispatches, repeated red/blue raster draws, scissor clipping,
  alpha blending, depth ordering, 16-bit indices, and 32-bit indices with clipping.
- Depth ordering checks three complete output frames: near red remains visible over far
  blue, then nearer blue replaces it. The fixture required a separate draw after depth
  clear and the correct merged-ES user register (s8). No depth runtime fix was needed.

The seven-scenario run records the existing executable hash and uncommitted fixture
changes against source revision 3d9ff63. Committing those fixtures does not rebuild or
change the tested executable. Each case retains its guest log and pixel-verification JSON.

## Continue here

Use `tools/native-probes/Test-TouchePX5-NativeGpu.ps1` for these checks. The earlier
OneDrive triangle script is obsolete. Source and builds remain under `C:\Dev\TouchePX5`.

Next useful graphics milestone: an original guest fixture sampling a small known texture,
with a pixel oracle, followed by changed texture contents at the same guest address.
Inspect `tests/SharpEmu.ShaderCompiler.Tests/Gen5ImageTests.cs` and texture decoding in
`src/SharpEmu.Libs/Agc/AgcExports.cs` before choosing the descriptor and shader encoding.
This milestone is planned, not implemented or passed.

The older 27-case engineering report predates the indexed-parser fix; it must not be
described as revalidated by this seven-case GPU run. Physical controller operation and
commercial game execution also remain separate, unverified milestones.
