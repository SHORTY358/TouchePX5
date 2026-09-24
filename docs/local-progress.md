# Local engineering checkpoint — 2026-09-24

This checkout is an experimental emulator. Commercial PS5 game compatibility and GTA VI
playability have not been demonstrated. No supported overall completion percentage or
completion date can be inferred from the tests below.

## Latest runtime change

CPU-authored linear 2D RGBA8 UNORM mip chains now reach separate Vulkan image levels.
The native `-Mip` case verifies LOD 0, LOD 1 and a CPU rewrite of only LOD 1 with
red/blue/red captures (`artifacts/texture-mip-addrlib.log`). All 1,235 unit tests passed
after rebuilding (`artifacts/mip-addrlib-tests.log`). All 13 native GPU cases passed
after the layout correction in `artifacts/native-gpu/20260924-120303-210/report.json`,
on clean source revision b372dd2.

Layout was checked against AMD AddrLib GFX10: smallest level first, ceiling-rounded
storage dimensions, 256-byte rows. The initial base-first experimental result in
`artifacts/native-gpu/20260924-115830-882/report.json` is superseded for mip correctness.
See the native-probes README for primary source links and explicit scope limits.
Mip chains currently use per-draw snapshots/uploads, so this is a correctness change,
not a performance claim. Tiled/array/storage/GPU-written chains and nonzero base views
are not newly supported by this change.

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

The sampled-texture milestone now has a passing native reproducer: a 1x1 image changes
from red to blue at the same address, followed by an unchanged blue draw. This exposed
and fixed an incorrect first-observation cache decision in the Vulkan upload-known path.
After rebuilding, all 1,225 unit tests passed again (`artifacts/texture-unit-tests.log`).
All eight cases passed in `artifacts/native-gpu/20260924-070915-639/report.json`,
including the extended three-frame texture case. The executable differs from the older
seven-case checkpoint; its exact hash is recorded in this newer report.

The next native fixture also passed: `-TextureSelect` samples red and blue diagonal
texels of a fixed 2x2 image, with green off-diagonal traps and a padded second row.
Its three frames passed in `artifacts/texture-select-2d.log`. No additional runtime fix
was required. The aggregate runner now includes this as a ninth case.
All nine cases passed together in `artifacts/native-gpu/20260924-071323-129/report.json`.
Inspect `tests/SharpEmu.ShaderCompiler.Tests/Gen5ImageTests.cs` and texture decoding in
`src/SharpEmu.Libs/Agc/AgcExports.cs` before choosing the descriptor and shader encoding.
Interpolated UV coordinates now pass in `artifacts/texture-uv-test.log`: three captures
show red, blue and green regions in their expected positions. A spatial pixel oracle and
visual inspection of `artifacts/smoke-triangle-texture-select-uv/triangle-1-textured.png`
verify this. The tenth aggregate case covers this fixture. No new runtime fix was needed.
All ten cases passed together in `artifacts/native-gpu/20260924-071713-770/report.json`.
The unequal-W perspective fixture also passed all three captures in
`artifacts/texture-perspective-test.log`. Its independent CPU oracle applies reciprocal-W
weighting, and the aggregate runner includes it as case eleven. Filtering and mip
selection remain future checks.
All eleven cases passed in `artifacts/native-gpu/20260924-114233-465/report.json`,
including rejection of the incorrect affine oracle for the perspective captures.
Linear filtering also passed three captures in `artifacts/texture-linear-test.log`.
The oracle checks a bilinear four-texel mix with a 3/255 RGB tolerance and rejects the
wrong nearest-neighbor expectation. The gradient PNG was visually inspected. This adds
case twelve; mip selection is still not covered.
All twelve cases passed in `artifacts/native-gpu/20260924-114628-635/report.json`.

The older 27-case engineering report predates the indexed-parser fix; it must not be
described as revalidated by this seven-case GPU run. Physical controller operation and
commercial game execution also remain separate, unverified milestones.
