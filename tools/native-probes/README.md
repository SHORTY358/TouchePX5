# Native GPU checks

Run from the source checkout:

```powershell
.\tools\native-probes\Test-TouchePX5-NativeGpu.ps1
```

The runner uses the existing Release executable and installed LLVM tools; it does not install
anything or rebuild. Reports are saved under `artifacts/native-gpu/<timestamp>/report.json`,
with the source revision, executable hash, individual results and copied guest logs.

- Compute: a synthetic gfx10 program runs twice on Vulkan with different inputs and reused
  memory. Checks FMA, signed multiplication, EXEC masking, ordered completion and CPU readback.
- Raster: synthetic gfx10 vertex/pixel programs draw a triangle, then redraw the same target
  through the same shader with changed scalar inputs: red becomes blue. Both Vulkan captures
  are checked for color, shape and area. The checker requires exact interior/exterior colors
  outside a two-source-pixel band accounting for bilinear presentation scaling. Every pixel
  is checked for valid color channels. Blank and wrong-color negative controls must fail.

Verified on GTX 1060 3 GB at 1920x1061: 2,037,120 pixels per raster capture, 563,461 fully
colored triangle pixels in each. These are controlled compute/raster tests, not commercial
game compatibility. Textures and more complex geometry remain
separate next steps.

Raster outputs: `artifacts/smoke-triangle/triangle-1-red.png`, `triangle-2-blue.png`,
`triangle.log` and `verification.json`; unique raw capture directories are retained too.
Compute log: `artifacts/smoke-compute/compute.log`.

The initial triangle fixture omitted PS input masks and shader user-SGPR resource registers.
The corrected fixture supplies those registers explicitly. No emulator implementation change
was required for the original non-indexed probes; the indexed parser fix is described below.

These files are kept here because Windows denied writes to the original OneDrive fixture
folder. Security settings were not changed. The old OneDrive triangle script remains an
unfinished earlier version; use these runners.
## Indexed and clipped geometry

`Test-TouchePX5-Triangle.ps1 -Scissor` intersects raster coverage with [80,240) x [45,135)
while retaining an explicit full-surface viewport. `-IndexBits 16` or `-IndexBits 32` uses
indices 0,1,3 to draw the mirrored triangle, which cannot pass if the index buffer is ignored.
The second indexed draw first sets a deliberately wrong INDEX_BASE and requires the inline
DRAW_INDEX_2 base to replace it. The combined GPU runner covers ordinary and clipped draws,
16-bit indices and 32-bit indices with clipping.

The indexed probe failed before the DRAW_INDEX_2 fix: the parser read the draw count but
ignored the inline index base and maximum count. The corrected parser validates these fields
before updating queue state. The 16-bit real-GPU probe then passed for both colors; all 1,225
unit tests also passed after rebuilding. Full combined-run results live in the timestamped report.

`-Blend` draws half-transparent blue over opaque red with separate alpha factors. The second
capture must be purple (red/blue 127 or 128, alpha 255 in the interior); the old red target
contents must survive for blending to succeed. The checker rejects incorrect channels,
coverage and area, and permits only the narrow filtered edge band. This passed on the real
Vulkan device. The combined runner includes this as its sixth case.

## Depth ordering

`-Depth` uses a D32 attachment cleared to 1.0, with LESS comparison and depth writes.
An ordinary red draw at 0.25 follows the clear draw. Blue at 0.75 must remain hidden;
blue at 0.125 must replace red. The three checked captures must therefore be red, red,
blue, including complete pixel coverage checks. The combined runner now has seven cases.

The vertex shader reads depth from s8: merged ES user data begins there, whereas PS
color inputs begin at s0. Clear mode initializes the attachment without writing geometry
depth, so the separate ordinary red draw is necessary. Correcting these fixture mistakes
made the real GPU depth probe pass; no emulator depth implementation change was needed.
Depth outputs are under `artifacts/smoke-triangle-depth/`, with numbered PNGs and JSON.
Depth and blend are deliberately separate scenarios and cannot be combined in this runner.

## CPU-updated sampled texture

`-Texture` samples a 1x1 RGBA8 UNORM image with `image_sample_lz`, using a real Vulkan
texture binding. The guest first writes red, then rewrites the same address with blue;
the descriptor and shader stay unchanged. A third draw repeats blue without a rewrite.
The checked captures must be red, blue, blue. This tests upload freshness and retention,
not UV interpolation, multiple texels, mipmaps, or filtering correctness.

Before the fix, the first red capture passed but the blue capture stayed red. The first
untracked-image content probe incorrectly returned "unchanged" before it had any baseline
for the uploaded bytes. It now requests one refresh to establish that baseline. The
two-frame native reproducer passed after this runtime fix. The aggregate runner includes
the extended three-frame regression as its eighth case.

`-TextureSelect` uses an unchanged 2x2 RGBA8 image with rows at a 256-byte pitch:
red/green on the first row and green/blue on the second. Both sampling coordinates
change from 0.25 to 0.75, then repeat unchanged. Red/blue/blue captures verify selection
of different texels and the padded second row. Green traps reject a missing coordinate
change. This passed on the real device without an additional runtime change and is the
ninth aggregate case. It still does not establish interpolated UV or mipmap correctness.

`-TextureUv` exports UV coordinates from the vertex shader and interpolates them in the
pixel shader before sampling a fixed 2x2 red/blue/green/white texture. The ordinary triangle
contains red, blue and green regions; white lies outside its diagonal. Its separate oracle
checks each region's position, solid colors, alpha, background and total area, excluding
only narrow filtered edge bands. Blank output and swapped red/blue expectations must fail.
Three captures passed, and the PNG was visually inspected. This is the tenth aggregate
case; mipmaps and nontrivial perspective correction are still separate future checks.

`-Perspective` preserves screen geometry but uses clip W=1,2,1. The CPU oracle computes
perspective UVs as `(u/2, v)/(1-u/2)` from screen barycentric coordinates, independently
of the shader code. The blue boundary moves from screen x=160 to x=200 and the green
boundary becomes slanted. The ordinary affine oracle must reject this output. This is
the eleventh aggregate case; the first native run passed all three captures.

`-LinearFilter` uses affine UVs and clamp-to-edge linear filtering on the same 2x2 image.
The independent oracle mixes its four texels with `tx=clamp(2u-0.5)` and
`ty=clamp(2v-0.5)`. Interior RGB may differ by at most 3/255 for texture quantization
and presentation resampling; alpha and the outside background remain exact. The nearest
oracle must reject the filtered output. Three native captures passed and the gradient PNG
was visually inspected. This is aggregate case twelve; mip selection remains untested.

`-Mip` supplies a red 2x2 base level and a blue 1x1 lower level in a linear RGBA8 chain,
with the lower level first in memory and the base level starting at byte offset 256.
Explicit LOD changes from 0 to 1; the third draw rewrites only the lower level to red.
Expected captures are red/blue/red. The initial reproducer failed at LOD 1 because the
standalone Vulkan upload allocated only one mip. The fix transports independent CPU
snapshots for the lower levels and uploads each to its own image subresource.

Scope: CPU-authored linear 2D RGBA8 UNORM, implicit width-derived pitch, with no existing backend
image at that address. Tiled, array, storage and GPU-written mip
chains retain their existing behavior. Full chains retain per-draw CPU snapshots,
but exact matches reuse an immutable Vulkan upload. The separate cache compares
all levels, layout, view and sampler; it does not rely on write-tracker generations
or a base-level hash. Admission is bounded to 32 entries and 16 MiB of snapshot
payload (GPU allocations and staging storage are additional). On saturation, a
work boundary flushes the current batch and defers destruction to its completed
submission timeline. Oversized chains retain transient uploads. Payload
accounting includes lower levels. Ten added unit cases cover padded/odd dimensions,
independent snapshots, invalid/truncated layouts, address overflow and payload size.
The aggregate's thirteenth case adds the lower-level-only rewrite check.

Layout reference: AMD's [GFX10 AddrLib implementation](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/imported/addrlib/src/gfx10/gfx10addrlib.cpp),
`HwlComputeSurfaceInfoLinear`, and `GetMipSize` in the sibling header. Levels are packed
from smallest to largest; storage dimensions use ceiling division and rows align to 256
bytes. Vulkan mip extents still use floor division. The first experimental fixture and
reader both assumed base-first order; the mip result in
`artifacts/native-gpu/20260924-115830-882/report.json` is superseded and must not be used
as evidence for GFX10 mip layout correctness. Both were corrected after source validation.

`-MipBaseOnly` exposes only level 0 of the two-level resource and requires red/red/red,
even when the shader requests LOD 1. `-MipLowerView` exposes only level 1 and requires
blue/blue/red: LOD 0 is relative to the view, and a lower-level CPU rewrite remains visible.
Both initial native tests passed. Full-chain Vulkan views now honor BaseMipLevel and
MipLevels; legacy single-level uploads retain a zero-based host view. These are cases
fourteen and fifteen in the aggregate runner.

The three mip cases also require one cache hit and exactly two Vulkan upload
commands: changing only the shader LOD reuses the image, while changing the lower
level creates another immutable version. `-MipCachePressure` adds 40 distinct
padding-only writes with submission completion between writes. It requires 43
draws, 42 uploads, one cache hit and at least one deferred eviction of 32 entries,
followed by the same red/blue/red pixel checks. This is the sixteenth aggregate
case. Padding changes deliberately count as content changes; this conservative
cache does not try to infer which texels a future shader might access.
