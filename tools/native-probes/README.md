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

`Test-TouchePX5-Compute.ps1 -ComputeQueue` adds case 29. It runs the same absolute
dimension transitions through `sceAgcDriverSubmitAcb`, owner 7, instead of the
graphics submit entry. The runner requires five ACB submissions and no DCB
submissions in addition to all counters, guards, completion and dispatch-size
checks. This validates one compute queue with repeated submissions, not actual
hardware concurrency or dependencies between separate guest queues. The
base-relative graphics packet mode cannot be combined with this switch.

`Test-TouchePX5-Triangle.ps1 -CrossQueue` and `-CrossQueueReuse` add cases 30/31.
The depth oracle now consumes arguments produced on ACB owner 7. Graphics is
submitted first and suspends on a standard WAIT_REG_MEM equality check; only
after a deliberate delay are the compute producers submitted. Four producer
dispatches signal RELEASE_MEM after writing their arguments. The guest checks
that graphics completion cannot advance during the producer-free interval.
The regular mode uses distinct labels and 20 ms delay. Reuse resets two previous
labels to zero and waits 750 ms, exceeding the configured 500 ms fallback age.
The old historical-signal fallback prematurely resumed a new waiter in reuse
mode. Requiring satisfaction latched for the current waiter fixes that failure
while preserving signals written during an active wait even if memory resets.
Both modes require red/red/blue pixels, exact compute/draw counts, at least three
suspensions and no deadlock-break, forced-resume or CPU semantic replacement.
The packet layout follows AMD's ME header linked above. Tests exercise one
compute-to-graphics dependency, not general hardware concurrency or game readiness.

`Test-TouchePX5-Triangle.ps1 -CrossQueueWriteData` adds case 32. Three producers
write `1` and then `0` to the same label with a nonincrementing WRITE_DATA packet.
Their consumers are already suspended; satisfaction must survive the reset.
The second producer in the first submission retains a RELEASE_MEM signal because
its consumer can be reached only after the first draw: replaying a transient
signal from before that wait is registered would test the wrong contract.
The guest checks final label values, completion and producer-free suspension.
The runner requires three executed WRITE_DATA actions, four computes/four draws,
red/red/blue pixels and no forced/deadlock/semantic fallback. This mode cannot
be combined with `-CrossQueueReuse`. Six submitted-packet unit cases additionally
cover standard/Gen5 encoding, overlapping 64-bit labels, and nonsatisfying writes.

`-CrossQueueDma` adds case 33 using the same three already-suspended consumers.
Each signal/reset pair uses two standard immediate DMA fills into ordinary label
memory. The runner requires all six fills, exact compute/draw counts, completion,
final reset labels and red/red/blue pixels without fallback. The before runtime
lost a signal and returned 78; the corrected runtime passes. Unit regressions
also cover copying a signal from memory and nonsatisfying copy/fill controls.
Only destinations without a known backend image latch immediately. DMA writes
that enqueue an image mirror still require separate completion-aware treatment;
this test does not establish transient-label behavior for those destinations.

`-DmaImage` adds case 34: draw a red triangle to establish a backend image,
then replace the same 320x180 target with a full blue DMA fill and a full red
DMA copy. After each submission's completion label the guest checks all 57,600
CPU words. After the copy it overwrites the source with green before presenting,
so the final red capture also verifies independence from subsequent source reuse.
The runner requires one draw, two successful 230,400-byte DMA operations against
an existing backend image, and four captures including startup. The first checked
frame uses the triangle oracle; both full images check every BGRA pixel exactly,
with blank and wrong-color negative controls. No runtime change was needed for
this baseline. It verifies sequential image mirroring through completion and
presentation, not transient waits on image memory or general cross-queue races.

`-DmaImageCrossQueue` adds case 35 using the same image and pixel oracle. Each
image update runs on ACB owner 7, after a graphics consumer has suspended on a
separate ready label. The ACB submission updates the image and releases that
label; only the resumed graphics consumer can publish the CPU completion label.
Both waits, resumes, DMA operations and deferred completion callbacks are required,
with no forced resume or deadlock fallback. CPU readback and source overwrite
checks remain enabled. This covers full-image DMA fill/copy through cross-queue
completion and presentation, not compute shaders or transient waits on image data.
No runtime change was needed. The fixture materializes the consumer packet address
at runtime because its minimal linker does not relocate pointer-valued constant
aggregates; an initial unrelocated packet failed before the fixture correction.

`-MipBc1 -WindowRecovery` is an interactive diagnostic, not an unattended suite
case. It keeps each checked frame for 20 seconds, raises only this child's stall
watchdog to 120 seconds, and bounds the child lifetime at 120 seconds. Restore
the window after its second draw, before the third draw reuses the same target.
Success requires both `vk.present_deferred` and `vk.present_resumed`, normal
guest completion, and the existing red/blue/red pixel oracle. A run without an
observed minimize/restore cycle must fail rather than claiming recovery coverage.
The original settings are restored on exit. Current end-to-end recovery status
and interrupted/failed attempts are recorded in `docs/local-progress.md`.

## Partial DMA image preservation

`Test-TouchePX5-Triangle.ps1 -DmaImagePrefix` covers suite case 36.
After drawing a red triangle, it fills the first 90 of 180 rows blue, then copies
red into those same rows. All CPU words are checked, including the untouched gray
backing-memory suffix. That suffix intentionally differs from the GPU-only triangle.
The pixel oracle requires the updated top half and compares the bottom half with
the first, independently verified GPU capture. Samples whose bilinear footprint
straddles the update boundary receive channel/alpha checks rather than an exact
color comparison. Positive and blank/full-overwrite negative
controls are exercised before checking the actual output.

The before runtime destroyed the untouched GPU pixels with a full gray-backed CPU
upload. Managed write tracking now records exact spans, and Vulkan uploads only
changed whole pixels for initialized, single-mip linear RGBA8 UNORM images. The
prefix case and `-DmaImagePrefix -DmaImageCrossQueue` (case 37) pass. Three tracker
unit tests cover adjacent chunk merging without merging gaps, later writes after
consume, and bounded storage with fallback after 64 disjoint spans.

This repair uses the CPU-image drain rather than adding immediate DMA image
mirrors. It establishes preservation through presentation, not dependent sampling
before that drain. Native page-fault tracking, unsupported formats/layouts,
byte-unaligned writes, and span overflow retain the older full-image behavior.
Metal is unchanged.

`-DmaImageSpan` adds case 38: update 115,840 bytes starting at pixel (80,45),
ending before (240,135). This exercises partial first/last rows and complete
middle rows while preserving GPU-only pixels around them. Both fill and copy
are checked, including all CPU words and source reuse after completion.
`-DmaImageSpan -DmaImageCrossQueue` adds case 39 with suspended/resumed consumers.
The interior destination does not match an exact registered image base, so its
DMA trace reports `image=False`; preservation is supplied by managed dirty-span
synchronization. Both cases pass after failing on the preceding runtime.

`-DmaImageDraw` adds case 40. It performs a prefix DMA fill followed immediately
by a half-alpha red triangle in the same graphics submission, before completion
and flip. The third checked frame uses a red DMA copy followed by a blue blend.
The oracle requires purple overlap in the updated prefix, appropriate red/purple
geometry below it, and preserved pixels outside the triangle. It rejects blank,
DMA-only and wrong-frame controls. All three translated draws must execute.
This passes without a runtime change and establishes dependent destination-image
blending for the tested sequence; it does not establish texture/compute reads or
every queue scheduling interleaving. This mode cannot be combined with the span
variant.

`-DmaImageDraw -DmaImageCrossQueue` adds case 41. The fixture splits each update
submission after its DMA packet: ACB owner 7 performs DMA and publishes readiness,
while the graphics queue waits, executes the following state/blend commands and
publishes completion. The runner requires the exact DMA-only producer and resumed
consumer command lengths, two genuine waits/resumes, three translated draws and
the dependent-blend pixel oracle. This passes without a runtime change. The
fixture's existing full-image/prefix/span cross-queue modes retain consumers that
only publish completion and are checked separately for regressions.

`-DmaImageSample` adds case 42. A red triangle is first drawn into a source image
whose CPU backing remains gray. A different pixel shader samples (0.25,0.25)
from that existing 320x180 image into a separate render target. Subsequent prefix
DMA fill/copy and dependent sample draws must produce blue and red triangles.
One untextured source draw and three textured draws are required; all checked
frames use the triangle oracle and blank/wrong-color negative controls.

The initial run sampled gray: an alias heuristic promoted nonzero CPU bytes even
though managed tracking supplied a generation for a GPU-only image. Restricting
that heuristic to genuinely untracked snapshots fixes this case. CPU-backed
textures and observed newer generations retain their refresh paths.
`-DmaImageSample -DmaImageCrossQueue` adds case 43: DMA-only ACB producers signal
waiting graphics consumers containing the texture draws. Exact producer/consumer
lengths, two real waits/resumes and all pixel checks are required. These tests do
not establish unobserved native CPU-write coherence or compute-image reads.

`-DmaImageSamplePreserved` adds case 44 and implies `-DmaImageSample`. The first
two samples still require the GPU-only red triangle and the blue DMA prefix.
The final copy changes only the upper half to green, then samples (0.25,0.625)
from the preserved lower red triangle. Its CPU backing is still gray; a stale
CPU snapshot, accidental whole-image upload or sampling the upper half cannot
pass the red triangle oracle. The source is overwritten blue after completion
to check that the copy does not depend on later source reuse.

This exposed a separate snapshot-selection bug: a partial CPU upload recorded a
generation without establishing complete CPU contents, but the historical
snapshot path treated the missing fingerprint as a mismatch and selected gray
CPU texels. The Vulkan image now explicitly records whether it has a complete
CPU snapshot. Partial uploads and GPU writes invalidate that fact; full CPU
uploads establish it. Historical snapshots remain available for complete CPU
images, and observed newer CPU writes retain the existing refresh path.
`-DmaImageSamplePreserved -DmaImageCrossQueue` adds case 45 with the same
DMA-only producer, waiting graphics consumer and strict wait/resume checks as
case 43. These cases cover the stated coordinates and linear RGBA8 layout,
not arbitrary subresources, native CPU writes without tracking or compute reads.

Dependent draw/sample cases now also require a successful partial image upload
at each DMA action's exact work sequence, before that action's completion trace.
The first 45-case aggregate exposed an intermittent frame-boundary upload that
erased the second frame's blend; a later rerun passed with identical source.
Vulkan drains dirty image stores immediately after ordered PM4 side effects, so
the next queued consumer sees the update and later frames cannot replay it over
that consumer. Unchanged dirty epochs retain the cheap early return. This is
Vulkan ordering coverage; Metal and unobserved native stores are not covered.

`-DmaImageComputeRead` adds case 46 and implies `-DmaImageSamplePreserved`.
Before each dependent texture draw, a compute program performs integer image
loads at (80,45) and (80,112), storing both RGBA vectors into a 64-byte output
buffer. After ordered completion the guest checks the exact float bit patterns:
red/red initially, blue/red after prefix fill, green/red after prefix copy.
The lower CPU backing remains gray, so stale CPU selection cannot satisfy the
readback. Eight guard words must stay unchanged for every pass. The runner
requires three real Vulkan dispatches, two image bindings per dispatch, global
GPU writes, no CPU semantic replacement, the ordered DMA-upload checks and the
existing three sampled-frame pixel oracles.

`-DmaImageComputeRead -DmaImageCrossQueue` adds case 47. The waiting graphics
consumer executes compute reads and sample draws after the DMA-only ACB producer.
Both resumed consumers must retain 133 command words. This validates read-only
integer image fetches at the stated points from this linear RGBA8 image; storage
image writes/atomics, arbitrary layouts/subresources and native unobserved stores
remain unverified. No runtime change was needed for these two cases.

The separate negative experiment in `artifacts/compute-image-read-wrong-row`
changes only the lower fetch row from 112 to 45. The first red/red read still
passes; the second fetch reads blue instead of preserved red and the guest
returns 94 after two real Vulkan dispatches. Its saved evidence is
`artifacts/dma-compute-image-read-negative.log`. This checks that the readback
assertions reject the wrong region independently of the sampled-frame oracle.

`-DmaImageComputeStore` adds case 48 and implies `-DmaImageComputeRead`.
Each compute invocation first writes yellow at (96,48), then reads the two
existing control texels plus that written pixel. Matching image store/load
descriptors use storage-image bindings, exercising `ImageWrite`/`ImageRead`
instead of the read-only variant's integer fetches. The guest checks the three
RGBA vectors and four remaining output guard words on every pass, while the
ordinary sampled frames must retain their exact red/blue/red triangle results.
Three actual Vulkan dispatches with four image bindings each are required.

`-DmaImageComputeStore -DmaImageCrossQueue` adds case 49 with DMA-only ACB
producers and waiting graphics consumers containing the compute and draw work.
Both resumed consumers must retain 139 command words. The separate negative
experiment in `artifacts/compute-image-store-wrong-pixel` changes only the store
row to 49 while checking row 48; it is rejected with guest 96 after one actual
GPU dispatch. Evidence is `artifacts/dma-compute-image-store-negative.log`.
These tests establish the stated one-pixel writes/read-after-write, not storage
atomics, every untouched source pixel, arbitrary layouts or native unobserved
stores. No runtime change was required.

All DMA sample modes now register four distinct output buffers: one warmup
buffer and one per verified result. A native-window presentation can be delayed
while GPU work continues; reusing one output for red then blue meant the queued
red flip could later display blue. The first 49-case aggregate caught exactly
that (blue/blue/red captures after `window-unavailable`). The runner now requires
three distinct nonzero presented image addresses, retaining every original
color/coverage, queue and DMA-order assertion. This is fixture buffer lifetime
discipline, not evidence of complete minimize/restore support or accurate
guest flip-status timing.

## R32_UINT storage-image atomics

`Test-TouchePX5-Compute.ps1 -ImageAtomics` adds case 50. A GPU image store
seeds texel (0,0) to 7 while its unchanged CPU backing contains 99/100/101/102.
Two one-invocation workgroups perform `ImageAtomicAdd` on that shared texel;
GLC returns old values into separate buffer entries. Both invocation orders
are accepted, but the returned pair must be exactly {7,10}, then {13,18}.
Separate GPU image-load dispatches must return final sums 13 and 23. Guard
words in the output buffer and the original CPU image words must stay intact.
Five real Vulkan dispatches are required; the seed also writes a buffer guard
slot so it cannot take a CPU constant-fill replacement path.

`-ImageAtomics -ComputeQueue` adds case 51, using five ACB owner-7 submissions
instead of graphics submissions. This is a sequential compute-queue variant,
not a producer/consumer cross-queue wait test. No emulator change was required.
Clearing only the atomic instruction's GLC bit is rejected at the old-value
check (guest 66 after two GPU dispatches):
`artifacts/compute-image-atomic-negative.log`. This establishes uint atomic add
at one texel with two workgroups, not other atomic operations, formats,
layouts or broad workgroup-ordering guarantees.

## Bounded atomic increment/decrement

`Test-TouchePX5-Compute.ps1 -BoundedAtomics` and `-BoundedAtomics -ComputeQueue`
add cases 52/53 for R32_UINT images. `-BufferAtomics` and its `-ComputeQueue`
variant add cases 54/55 for ordinary buffers. `-SignedImage` and its
`-ComputeQueue` variant add cases 56/57 for R32_SINT images; comparison still
uses unsigned bits as required by the instruction.

Eleven boundary cases independently check returned old values and a separate
GPU read of the final value: below/equal/above the limit, zero limit, zero
decrement, UINT_MAX and high-bit values. Two further dispatches each run 64
workgroups against the same texel/word with limit 7. The eight returned-value
bins must each occur eight times, and a separate GPU read must return 6.
The output has 80 words so every atomic writer has its own destination and
the trailing sixteen guard words must stay intact. Every scenario requires
39 GPU dispatches, including seed/read operations, and forbids CPU replacements.
Buffer mode also checks guest writeback and three neighboring input words;
image mode deliberately keeps stale CPU backing throughout.

Before repair, the first boundary read returned 8 instead of 0 in both image
and buffer modes (guest 100): `artifacts/compute-image-clamp-before.log` and
`artifacts/compute-buffer-clamp-before.log`. Vulkan now lowers bounded INC/DEC
to an atomic load and compare/exchange retry, keeping the DATA limit and the
old value from the successful attempt. The common emitter covers buffer,
image and LDS instructions; eight pipeline unit cases cover those routes and
both image component types. Native LDS execution remains separate work.

The limit semantics follow AMD's [RDNA 2 ISA reference](https://www.amd.com/content/dam/amd/en/documents/radeon-tech-docs/instruction-set-architectures/rdna2-shader-instruction-set-architecture.pdf),
sections 12.13, 12.14 and 12.16: INC resets at/above the limit; DEC resets to
the limit at zero/above it. These checks do not establish all memory-ordering
rules, arbitrary dispatch sizes, cross-queue waits or shader performance.

## Native LDS atomics and masked writes

`Test-TouchePX5-Compute.ps1 -LdsAtomics` adds case 58. The first eleven
single-invocation kernels initialize LDS, perform bounded INC/DEC twice,
read the shared word and verify exact old/final values plus output guards.
Two more kernels use 64 local invocations in one workgroup. Only lane 0
initializes the word through atomic exchange; barriers separate initialization,
the shared operation and final readback. The returned-value histogram must
contain eight occurrences of every value 0..7 and finish at 6.

Two additional cycles atomically initialize 48, then overwrite it through a
lane-0-only `DS_WRITE_B32` to 6/3 before the 64 invocations operate. Before
repair, inactive lanes read 48 and stored it back over the active writer:
the first atomic returned 48 and guest 132 rejected the first real dispatch.
Saved evidence: `artifacts/compute-lds-masked-write-before-evidence.json` and
`artifacts/compute-lds-masked-write-before-guest.log`. Vulkan's common LDS store
now emits an EXEC-conditional write, with no read/store from inactive lanes.

Four final kernels test DS writes independently of atomics: zero active lanes,
lane 0, lane 32 and lane 63. After a barrier all 64 invocations must read
48/6/3/9 respectively, while sixteen trailing output guards remain unchanged.
All nineteen dispatches run on Vulkan; CPU semantic replacements are rejected.
`-LdsAtomics -ComputeQueue` adds case 59 using nineteen sequential ACB owner-7
submissions. These establish the stated word operations and active-lane masks
in one 64-invocation group, not cross-queue waits, GDS or native conformance of
every wider LDS store. Wider stores use the repaired common emitter but do
not have native width-specific oracles here.

## Wider native LDS stores

`Test-TouchePX5-Compute.ps1 -LdsWide` and `-LdsWide -ComputeQueue` add cases
60/61. Each runs twelve 64-invocation dispatches: `DS_WRITE_B64`,
`DS_WRITE_B96` and `DS_WRITE_B128` under inactive/lane-0/32/63 masks.
Five LDS words are independently initialized to 48 via lane-0 atomic exchanges.
The selected writer stores 6/3/9/12 across exactly its declared width.
Independent `DS_READ_B32` operations read all five words, avoiding a matching
wide-read error hiding a wide-write error. Every invocation checks the written
components and untouched neighbors, including the fifth LDS guard beyond the
largest store. Each output record also has three padding guards and the buffer
has sixteen trailing guard words. No CPU replacement is allowed.

Changing only the 96-bit instruction to a 64-bit store is rejected at its
first active mask (guest 155 after six GPU dispatches):
`artifacts/compute-lds-wide-negative.log`. Direct and ACB variants require
exactly twelve submissions on their requested queue, with no other queue
submissions. The runner now enforces this for other atomic modes too.
No further runtime change was needed after the masked LDS-store fix. This
covers contiguous writes at byte offset zero and the stated masks, not paired
stores with scaled offsets, GDS or every possible LDS address.

## Paired native LDS stores

`Test-TouchePX5-Compute.ps1 -LdsPaired` and `-LdsPaired -ComputeQueue` add
cases 62/63. Eight 64-invocation dispatches test `DS_WRITE2_B32` and
`DS_WRITE2_ST64_B32` under inactive/lane-0/32/63 masks. Offset indices 1/3
address bytes 4/12 for ordinary writes and 256/768 for ST64. Five locations
are initialized independently to 48 through lane-0 atomic exchanges; an
active writer stores 6/12 only at the two selected locations. Independent
single-dword reads verify every invocation, both intervening and neighboring
LDS guards, per-record padding and sixteen trailing output guards.

Changing only the ST64 instruction to an ordinary paired store is rejected
at its first active mask (guest 155 after six GPU dispatches):
`artifacts/compute-lds-paired-negative-evidence.json` and
`artifacts/compute-lds-paired-negative.log`. Both modes require exactly eight
submissions on their requested queue and reject CPU semantic replacements.
No further runtime change was needed. These checks cover the stated scaled
offsets and masks; GDS and arbitrary addresses remain separate work.

## Cross-queue storage-image atomics

`Test-TouchePX5-Compute.ps1 -ImageAtomicsCrossQueue` adds case 64. An ACB
owner-7 kernel seeds a GPU R32_UINT texel to 7. Two subsequent ACB kernels
each run two contending workgroups adding 3/5. Their graphics-queue consumers
are submitted first and must suspend on separate completion labels, then
resume and independently read image sums 13/23. The two returned old values
are checked without assuming invocation order. CPU image backing deliberately
stays 99/100/101/102; output guards remain unchanged. The guest also checks
that neither consumer completion nor output appears before its producer.

Five actual GPU dispatches, three 70-dword ACB submissions, two 77-dword DCB
submissions, two suspensions and two resumptions are required. CPU semantic
replacement, forced resume and deadlock-break paths are rejected. Changing
the consumer to read texel 1 is rejected with guest 68 after three GPU
dispatches: `artifacts/compute-image-atomic-cross-negative-evidence.json`.

## Partial CPU DMA into integer images

`Test-TouchePX5-Compute.ps1 -PartialImage`, with optional `-SignedImage` and
`-ComputeQueue`, adds cases 65-68 for R32_UINT/R32_SINT on graphics/ACB queues.
A GPU kernel seeds texel 0 to 7 while CPU backing stays 99. A four-byte DMA
changes only texel 1 to 123; separate GPU reads must see preserved 7 and new
123. The guest checks all neighboring CPU words and output guards. Three
GPU dispatches, one successful DMA and one successful partial upload are
required, exclusively on the selected queue; CPU replacements are rejected.

Before repair the RGBA8-only partial-upload gate fell back to uploading all
16 stale CPU bytes, replacing GPU texel 0 with 99 and failing guest 80 after
two dispatches: `artifacts/compute-r32-partial-before-evidence.json` and
`artifacts/compute-r32-partial-before-guest.log`. Both the dirty-span gate and
upload validation now admit linear single-mip/depth-1 R32 integer images,
retaining four-byte alignment, exact extents and incomplete CPU snapshot
state. Unsupported formats/layouts and unaligned writes retain their existing
fallback behavior; these tests do not establish their correctness.
