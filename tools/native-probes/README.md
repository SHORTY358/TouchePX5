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

Scope: CPU-authored linear 2D RGBA8 UNORM/sRGB or BC1 UNORM, implicit width-derived pitch, with no existing backend
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

`-Mip2D` uses a red 4x4 base at allocation offset 512 and a nonuniform 2x2 lower
level at offset 0, both with 256-byte rows. Fixed UV=(0.75,0.75) selects the
bottom-right lower texel when LOD becomes 1, then that texel changes from blue to
red. Red/blue/red proves lower-level row pitch and coordinates, which the 1x1
lower level could not exercise. This is the seventeenth aggregate case.

`-MipOdd` uses a 5x3 base and a lower mip with physical storage 3x2 but sampled
extent 2x1, following the same AddrLib ceiling-storage/floor-visible distinction.
Unused physical texels are green; sampling UV=(0.75,0.75) must pick the blue
second visible texel, then its red replacement. Selecting the extra physical
column or row fails the color oracle. This is the eighteenth aggregate case.

`-MipSrgb` is the nineteenth case. RGBA8 sRGB (unified format 130) stores red=128
in the base, blue=128 in the lower level, then rewrites the lower level to red=64.
Alpha is always 128. The color oracle uses the [Khronos sRGB transfer function](https://registry.khronos.org/DataFormat/specs/1.4/dataformat.1.4.html)
and [Vulkan sampling rules](https://docs.vulkan.org/spec/latest/chapters/textures.html),
including 8-bit UNORM target quantization and the capture's output encoding. On
the tested UNORM swapchain, the expected color channels are 55, 55 and 13 while
alpha remains 128. It rejects a blank image and the incorrect no-decoding oracle.
The pre-fix probe sampled blue from the allocation's first mip instead of red
from the base: the full-chain AGC path previously excluded sRGB number type 9.

`-MipBc1` is the twentieth case: an 8x8 red base (four BC1 blocks) follows a 4x4
blue lower level, then only that lower block becomes red. Every block uses one
RGB565 endpoint and zero selectors, making the expected colors exact. Rows occupy
256 bytes; the lower level uses offset 0, the base starts at 256, and total storage
is 768 bytes. The old path failed the first frame; the block-aware full-chain
reader passes all three pixel checks and requires one cache hit / two uploads.
BC1 sRGB, other BC formats, arrays and tiled mip chains are outside this change.
Four additional unit cases cover odd 9x9 block storage, texel-unit copy pitch,
independent snapshots and rejection of truncated/unsupported formats.

Compressed layout references: AMD [ComputeSurfaceInfo](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/imported/addrlib/src/core/addrlib2.cpp)
calls [AdjustSurfaceInfo](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/imported/addrlib/src/core/addrelemlib.cpp)
to convert BC1 dimensions into 4x4, 64-bit elements before the GFX10 linear mip
calculation. Vulkan [buffer/image copies](https://docs.vulkan.org/spec/latest/chapters/copies.html)
express buffer row length in texels, so a 256-byte BC1 block row becomes 128 texels.

Aggregate reports include SHA-256 manifests for the runtime's EXE/DLL files,
including nested libraries, and .NET deps/runtimeconfig files. The same manifest
must match at completion; a rebuild that leaves the app-host EXE unchanged but
replaces a managed DLL invalidates the run. Capture/source hashes remain in the
per-case reports. The manifest does not claim to hash external drivers or caches.

`-MipBc1 -Mip2D` is the twenty-first case: a 16x16 base starts at offset 512;
the 8x8 lower level has red/green blocks in the first row and green/blue in the
second. UV=(0.75,0.75) selects the bottom-right blue block, then its red replacement.
This checks block-row pitch and selection that a single-block lower level cannot
distinguish. It retains the one-cache-hit / two-upload requirements.

`-MipBc3` is the twenty-second case. An 8x8 red base follows a 4x4 blue lower
level, then the lower level becomes red. Alpha is 128 for the first two frames
and 64 for the third. Each 16-byte block contains alpha endpoints and selectors
in the first eight bytes, then RGB565 endpoints and color selectors; all selectors
choose endpoint zero. See [Khronos BC3 layout](https://registry.khronos.org/DataFormat/specs/1.4/dataformat.1.4.html)
and [Microsoft BC3 decoding](https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-block-compression).
The 256-byte block row is expressed as 64 texels in Vulkan copies. The pixel oracle
checks exact alpha independently from color and rejects opaque-alpha, wrong-color
and blank-image expectations. The unchanged draw reuses its upload; a lower-level
rewrite creates a fresh upload. This tests endpoint selection, not interpolated
BC3 alpha selectors or every BC3 encoding. BC3 sRGB remains outside this change.

`-Indirect` adds the twenty-third case, using the depth scenario. Four DRAW_INDIRECT
records follow a poison zero-count record; nonzero byte offsets must select each
record. The packet's start-instance destination names the vertex user-data register
used for Z. The test intentionally encodes float depth bits in firstInstance to
isolate raw command-processor register writes; it does not claim first-instance
indexing coverage. A preceding SET_SH_REG poisons Z to 1 before every draw. Expected
frames are red/red/blue; the old parser leaves the third frame red. The single
indexed and non-indexed packet variants also have argument-copy unit coverage.
References: AMD [BuildDrawIndirect](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/hw/gfxip/gfx9/gfx9CmdUtil.cpp)
and the [PFP packet fields](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/hw/gfxip/gfx9/chip/gfx9_plus_merged_f32_pfp_pm4_packets.h).
General base-instance semantics are separate work; the indexed multi case below
adds coverage for its packet controls.

The triangle runner waits for the exact launched app-host process to exit before
reading its log. A 60-second timeout stops only that child and fails the test;
nonzero exit codes and all existing guest/pixel checks still fail. This replaces
the GUI-executable invocation plus 20-second log polling, which could report failure
while a slower guest was still finishing successfully.

`-IndirectMulti` adds the twenty-fourth case. Each 10-dword DRAW_INDEX_INDIRECT_MULTI
packet uses a nonzero offset and two 32-byte records: an empty record followed by
one indexed draw. It reuses the red/red/blue depth oracle. The old parser incorrectly
read stride from dword 6 (count-address low) instead of dword 8 and failed to issue
the expected draws. The builder and size query now also use the hardware 10-dword
layout, preserving both count-address words before stride/draw initiator.
The same AMD packet/header references above define these fields. Unit tests cover
direct zero count (no scanning), memory count clamped to the packet maximum,
start-index/draw-index destinations, and exact builder bounds. The native case
currently uses a direct count; GPU-produced count-buffer synchronization is not
established by it. `use_vgprs` is explicitly unsupported, and the existing 4096-draw
host safety cap remains.

`-IndirectGpuCount` adds the twenty-fifth case. Before each of four multi packets,
a real Vulkan compute dispatch writes count=2; the CPU location is deliberately
initialized to stale count=1. The packet maximum is 3. Three records are empty,
real draw and poison draw respectively, so stale count misses a draw and ignoring
the memory count executes an extra draw. The same red/red/blue depth oracle and
exactly four translated compute and four raster executions are required.
The pre-fix run executed four computes but only two raster draws: count readback
incorrectly reused the empty-argument throttling heuristic. Counted packets now
order a fresh four-byte readback after earlier work and check completion before
reading/clamping the count. Timeout fails that packet instead of consuming stale
CPU data. This validates the compute-to-count path on this fixture; it does not
establish general GPU-produced argument-record synchronization or performance.

`-IndirectGpuArgs` adds the twenty-sixth case. All argument records already have
valid nonzero vertex/instance counts; their raw first-instance field initially
encodes stale Z=1. Four translated compute dispatches overwrite that field with
the intended depths before four single indirect draws. The old nonzero-count
shortcut skipped readback entirely: all draws executed but the last frame stayed
red. The updated path orders and waits for the full selected argument span on
every packet, producing red/red/blue. It no longer throttles empty-record reads
or caps synchronization to the first 1024 records while parsing up to 4096.
Address-span overflow and failed completion reject the packet. These synchronous
readbacks prioritize correct ordering; their performance cost has not been tuned.

`Test-TouchePX5-Compute.ps1 -IndirectDimensions` adds the twenty-seventh case.
A direct atomic increment first establishes a working translated consumer. Four
GPU producers then replace CPU dimension X values: 1->2, 2->1, 0->2 and 2->0.
Each indirect consumer increments its own counter once per invocation. The guest
checks exact counts, dimensions, completion and surrounding sentinel words; the
runner also requires exactly eight Vulkan dispatches (six 1x1x1 and two 2x1x1),
with no CPU semantic substitution. The absolute indirect packet is used here.
The old parser consumed stale nonzero dimensions and failed the first producer
case with guest code 91; ordered 12-byte readback fixes it. A synchronized zero
dimension submits no work. This is narrow compute-to-compute coverage, not a
general game-compatibility claim. The atomic instruction encoding is independently
checked against [LLVM GFX10 MC fixtures](https://github.com/llvm/llvm-project/blob/main/llvm/test/MC/AMDGPU/gfx10_asm_mubuf.s).
The compute runner now uses the same bounded child-process wait as the raster
runner, so log inspection happens only after that process exits.

`Test-TouchePX5-Compute.ps1 -BaseRelative` adds case 28. It uses SET_BASE plus the
three-dword graphics DISPATCH_INDIRECT packet at a 16-byte offset. Prefix words
contain a plausible but wrong 1x1x1; only the selected dimensions may change.
Producers grow Y, shrink Z, enable X from zero and disable Y to zero. All counters
and guards must match, with six 1x1x1 executions, one 1x2x1 and one 2x1x1.
Packet layouts are defined in AMD's
[ME header](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/hw/gfxip/gfx9/chip/gfx9_plus_merged_f32_me_pm4_packets.h)
and [MEC header](https://github.com/GPUOpen-Drivers/pal/blob/dev/src/core/hw/gfxip/gfx9/chip/gfx9_plus_merged_f32_mec_pm4_packets.h).
Both fixture variants use the existing graphics submission harness; accepting
the absolute MEC-format packet there is not proof of asynchronous queue support.
