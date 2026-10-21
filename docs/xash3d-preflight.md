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
