# Xash3D next-stage preflight — 2026-10-21

The standalone Gears source links to the independent
[PS5 Xash3D/Half-Life port](https://github.com/mpereiraesaa/ps5-xash3d-halflife).
The small top-level source checkout is retained under
`artifacts/external-homebrew/ps5-xash3d-halflife`, pinned to
`be0731b4fcc4233349bba5e0d3ff33c79428f695`. Its tracked sources remain clean.
Submodules are recorded but not initialized: Xash3D FWGS
`9aa39ad49b2ba07ffd945f93a19d31e11400b6cd` and hlsdk-portable
`e277ffaae85422cb674c8c19a2209a7fa85157db`. No game assets are supplied here.

The upstream README describes a native textured world, models, input, audio and
saves. Those are upstream claims about its PS5 port, not verified TouchePX5
compatibility. The current Gears-only native NGG gate deliberately rejects this
different shader pair and its sampled resources.

## Host build limitation

`tools/external-homebrew/Test-Xash3DHost.ps1` reuses the existing isolated Cygwin
toolchain; it installs nothing. The original `make test` fails compiling
`tests/test_ps5_memory_arena.c`: PE/COFF GCC supports static object alignment only
up to 8192 bytes while that test requires `_Alignas(16384)`. No alignment or
assertion was weakened. Full host checks and the subsequent audit have NOT
passed. Evidence: `artifacts/xash3d-host/20261019-121859-891`.
Use a suitable ELF/POSIX host for the unchanged full checks, or independently
review a portable aligned-allocation adaptation before claiming a host pass.

## Original textured shader available now

`tools/external-homebrew/Build-Xash3DTexturedShader.ps1` compiles unchanged
`shaders/bsp_textured.pipe` using the existing pinned LLPC/LLVM tools. It does
not invoke the native title build, SDK downloads or game-data packaging.
Evidence: `artifacts/xash3d-shaders/20261019-122045-187`.
The GFX1013 PAL stages have no relocations: GS 288 bytes, PS 316 bytes.
The build report hashes the source pipeline, compiler tools and manifest.

`SharpEmu.Tools.NativeShaderAudit` accepts that manifest, verifies the original
stage hashes and records decoded instructions/coverage without GPU execution.
It also retains concrete controls, register operands and opcode inventories for
the next input/binding investigation. A deliberately altered pixel-stage copy
is rejected by its manifest hash; original files remain unchanged.

This exposed a real decoder defect: the PS branch at byte 0x84 targets 0x12C,
after the first `s_endpgm` at 0x128. Earlier decode returned success after only
300/316 bytes, omitting the alpha-discard exit. After direct-branch traversal it
retains 68 instructions and all 316 bytes; GS retains all 56 instructions/288
bytes. `decode-before`, `decode-after` and `hash-negative` retain both outcomes.
This is decode coverage, not original shader execution or verified discard.

## Next concrete gates

1. Completed: retain shader fix and regress native Gears and branched pixels.
2. Completed: source-derived BSP inputs with original GS, identity/nonidentity
   matrices, position/UV streams and independent GLSL export expectations.
3. Completed for the bounded subset below: actual GPU NGG outputs and indices.
4. Completed for the source-derived fixture below: original PS resource table,
   base texture/lightmap, actual pixels and independent alpha-discard reference.
5. Only then consider a bounded native renderer package and full Xash3D startup.
   Native game boot, engine PRXs, real content and full host checks remain separate.

Keep the Gears shader gate in place; adding an unrelated hash without its input,
output and lifetime proof is not an implementation of this new pipeline.

## Source-derived GS execution proof

`tools/external-homebrew/Test-Xash3DCompute.ps1 -ManifestPath
artifacts/xash3d-shaders/20261019-122045-187/bsp_textured.manifest.json` passes at
`artifacts/xash3d-compute/20261021-034744-942/execution-report.json`.

The original 288-byte GS is compiled unchanged through the existing explicit
NGG compute transport and executed on the actual Vulkan GPU. Independent GLSL
reference inputs use identity and projective matrices with three vertices, then
a non-repeating 132-vertex stream across two explicit groups. Expected positions
come from column-major `mat4 * vec4`; base/lightmap UVs and control parameters
come directly from the separately declared source streams/constants.

All 13376 output/primitive/input/guard words across the three cases match;
positions permit at most 2e-5 relative-or-absolute error, UVs/control/connectivity
and idle guards remain bit-exact. Other input storage buffers must remain
unchanged. The checked remapper consumes 3/3/132 actual indices from completed
GPU primitive records, not regenerated geometry.

Changing the first vertex's base-U input fails its original expected output.
Resetting second-group source IDs to 0..5 also fails, exclusively within that
group's vertex-export range; only the expected source-table words are updated
for this negative control. Hashes cover original shader, matrix/vertex fixture,
SPIR-V, expected/actual data, remapped indices and driver logs; positive inputs
must remain unchanged while execution runs.

The three cases use explicit 128-thread/32-lane transport groups, not a claim
about console scheduling. They are source-derived inputs, not native title
captures. This proof produces GPU geometry/parameters/connectivity; the original
textured pixel stage and final scene pixels are the next separate gate.

## Original GS → original textured PS proof

The compute fixture additionally retains a source-derived background case with
the original GLSL control branch and matching triangle geometry. The latest
four-case compute run is `artifacts/xash3d-compute/20261021-035355-147`.
Its matrices, source streams, descriptor table, results and primitive indices are
hashed. Third-vertex Z is positive so the identity triangle stays in Vulkan's
clip-depth range for the controlled graphics check.

`Test-Xash3DGraphics.ps1 -ManifestPath <original manifest> -ComputeRoot
<completed four-case compute directory>` passes at
`artifacts/xash3d-graphics/20261021-035905-198/execution-report.json`.
The original 316-byte PS consumes two independent 2x2 textures through its
original resource-table loads and sampling sites. A vertex bridge only transports
completed GS positions, UVs and control; it does not recalculate geometry.
The first draw supplies a colored background on that geometry, and the second
samples base color/lightmap with one alpha-cutout texel. Ordered target readbacks
retain the actual 640x360 RGBA image before presentation scaling.

An independent GLSL reference recomputes clip coordinates and interpolated UVs
from the source fixture, with the explicitly selected positive-height viewport.
46756 interior pixels pass, including 43401 opaque and 3355 discarded pixels;
165329 exterior pixels remain clear. Five base/lightmap texel pairs are seen.
There are zero mismatches at tolerance 2/255; worst channel error is 1. Raster
edges and nearest-texel boundaries are excluded and whole-image conformance is
not claimed.

Wrong models without the lightmap or with the textures exchanged reject 43401
pixels; replacing discard with a black write rejects 3355. A copy with one
reference-selected interior pixel corrupted fails exactly one numerical check,
while original captures retain their hashes. The colored background distinguishes
real discard from writing a zero-initialized output. A diagnostic PNG was visually
inspected at `textured-bsp.png` in that directory.

This is original shader execution on source-derived test data through the
backend. It is not a native Xash3D process, engine PRX integration, real map,
full texture-format/LOD compatibility or commercial-title compatibility.

## Standalone native renderer bootstrap, 2026-10-21

`Prepare-Xash3DNativeInputs.ps1` uses eight unchanged upstream shader pairs and
the upstream `tests/test_bake_bsp.py:tiny_bsp()` factory through its original
baker. The 36008-byte synthetic bundle hashes to
`02bc5133a6fdcf74cff3d33878664f247304e22e40ed0194b54a85d7faf5ea5e`.
Input report: `artifacts/xash3d-native-inputs/20261021-040759-513/report.json`.
No Valve assets, submodules or full engine are required for this diagnostic.

`Build-Xash3DRendererWindows.ps1` compiles the 60-source upstream inventory,
two log sources and genuine `ref_agc_gpu_texture_cache.c` (missing from upstream's
native list although required by its skybox object). All runtime checks remain.
Clang 18 retains -Wall/-Wextra/-Werror with only the SDK v0.42 qualified `_Generic`
math macro diagnostic suppressed. Two unused optional-feature locals are
acknowledged with `(void)`. Inputs/objects/app files/overlays are hashed.
SDK verification compares 2923 files against the already pinned archive.

This is an explicit emulator consumer ABI variant: public init revision 8,
legacy HLE 127/78 defaults with a 12-byte type/index record. It does not establish
the original firmware 12.02 ABI. Optional `-ShaderAperture` requests BSP at
0x200000000 and shader data at 0x210000000 through fixed public memory mappings;
original high-address checks remain. Baseline low tables fail at `bsp_scene`
in run `artifacts/xash3d-native-runs/20261021-041725-908`.

With those mappings, the original six-word indexed writer contract exposed HLE
`sceAgcDcbDrawIndex` rejection of initiator 5 and unwanted five-word INDEX_BASE/SIZE
prefixes. Pinned OpenAGC `3c7e8072f0d7684e930454794fd5c02056946d73`
`src/game_compat.c` q88lQ+GP5Yk emits only a six-word DRAW_INDEX_2 with the low
caller initiator word. Touche now does the same; it does not infer arbitrary
64-bit modifier/hardware execution semantics from that reference.
Ten focused checks cover exact cursor, address/count/initiator, tail preservation
and insufficient capacity without partial writes; full library suite 1422/1422
passes. CLI Release build succeeds; test rebuild retains existing SHEM006.
Native index16 GPU probe still shows red/blue output.

The before/after runs use the same SELF; `NativeBuild.SelfSha256` in each
execution report records its identity. Before writer fix run `20261021-042031-398` fails compose
frame 0, callback -16. After fix `20261021-042343-396` submits real 109-dword
DCBs and indexed clear/world counts 3/6. It then fails translation on the
original GS SBarrier at PC 0x24 and parks with callback -22; no native scene
pixels are verified and no shader gate is broadened.

`Invoke-Xash3DRenderer.ps1` verifies source/build/app/runtime identities, sets only
scoped diagnostic variables, restores them, and bounds its own process tree.
It stops early after the instrumented retained-resource park instead of flooding
the log with unresolved `pause` calls. `-CaptureInputs` uses the existing bounded
replay facility for two native draws. Captures under
`artifacts/xash3d-native-runs/20261021-042616-243/replay` retain original GS/PS
state, resources and actual u16 indices; they are inputs, not GPU completion.
Next is independent captured-input computation, then pixel/depth/resource checks.

## Captured native indexed geometry proof

`Test-Xash3DNativeCompute.ps1` verifies the run evidence hashes and unchanged BSP
GS, then reads the first indexed clear and world draws. World vertex/index bytes
must exactly match VERT/INDX chunks of the hash-checked built synthetic bundle;
clear geometry and identity matrix must match upstream source. No translated
instruction is used to calculate the independent GLSL matrix/UV/control oracle.

Proof: `artifacts/xash3d-native-compute/20261021-043327-663/execution-report.json`,
from native run `artifacts/xash3d-native-runs/20261021-043116-952`.
Both 3348-word export/primitive/guard arrays match on the actual GPU. The audit
explicitly retains 3 vertices/1 primitive for clear and 4 shared vertices/2
primitives for world, using captured u16 topology 0,1,2,0,2,3. Completed primitive
remapping preserves it. Positions use tolerance 2e-5; all other words are exact.
Changing only the second primitive's shared-vertex source rejects exactly two
words in that primitive's export record. A changed captured-input copy rejects
at hash validation before fixture creation. Original evidence is preserved.

This is offline replay of original native inputs, not native draw scheduling,
camera visibility, full engine execution or native scene pixels. The existing
production gate continues to reject indexed BSP NGG. Next is actual native
descriptor/sampler/resource qualification and pixel inputs before scheduling.

## Original native pixel resources on controlled tiles

`Test-Xash3DNativeTextures.ps1 -ComputeRoot <completed native compute proof>`
passes at `artifacts/xash3d-native-textures/20261021-043823-794`.
It verifies original native run/capture/map hashes and original 316-byte PS,
retains actual table/descriptor/sampler words and native pixel inputs (2/2),
then uploads source-bundle resource bytes with the existing production mip
reader. Native descriptor addresses must match TEXP/TEXM and LMPX chunks.
All 4096 base texels also match the independent upstream tiny_bsp palette
equation, rather than using the production transfer as its own oracle.

The source resources are a seven-level 64x64 RGBA8 base chain with repeat and
trilinear sampler, plus a 64x7 bilinear/clamped lightmap. Sixteen controlled,
constant-UV tiles sample base texel centers, use original completed GS control
colors and retain alpha discard over original clear color. One tile tests repeat.
All 199424 interior pixels match at tolerance 2/255; worst channel error is 1.
Missing-lightmap, black-discard and swapped-R/B models reject 15/1/12 cells.
A copied image with one selected pixel changed rejects exactly one check;
positive captures retain hashes. The lossless PNG was visually inspected.

Tile clip positions are controlled diagnostic geometry, not native world
positions. Pixel bytes come from the original source bundle, not a GPU texture
readback. This proves the original native PS/table/resource route at base LOD;
it does not establish perspective/minifying LOD behavior or native scene pixels.
The tiny regression map's original camera points horizontally above its small
floor; its captured clip coordinates alone cannot promise a visible world scene.
