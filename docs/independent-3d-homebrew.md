# Independent GPU homebrew checkpoint

The next target after Wolf is the unchanged `examples/gpu_cube/main.c` from
[ps5link-sdk](https://github.com/Rufidj/ps5link-sdk), pinned to
`ea771e535378740b6a058b8e5419eb8a0e0e0ec8`. Its vertex/index buffers, matrices,
lighting shaders and tiled render target exercise the real AGC path. Unlike
our synthetic triangle fixtures, this source was written independently of this
emulator. Its startup and cube source are not patched to skip failures.

`tools/external-homebrew/Build-PS5LinkCube.ps1` builds the host linker and
unchanged PS5 source on Windows. The cube only needs upstream mesh shader
containers, so it does not require assembling the SDK's additional texture
shaders. Upstream keeps the PSSL source, GPL-3.0 license and attribution with
those containers. No proprietary SDK or firmware is added.

The result is an unsigned SCE ELF for the emulator, not a signed console package.
The first native run of `artifacts/external-homebrew/ps5link-cube/app.elf` reached
main and exited with guest status 1 / host exit 4 before rendering. Logs remain
in that directory. The two missing NIDs independently match the public name
hash algorithm:

| NID | Name | Observed call |
| --- | --- | --- |
| `zl7hupSO0C0` | `sceKernelSendNotificationRequest` | notification size `0xC30` |
| `kW3GLb7QfPg` | `sceAgcInit` | state pointer, defaults revision 8 |

Further ABI inspection changes the first diagnosis. The cube and SharpProspero
call `sceAgcInit(state, revision)`, but
[OpenAGC game_compat.c](https://github.com/OpenAGC/OpenAGC/blob/main/src/game_compat.c)
at `3c7e8072f0d7684e930454794fd5c02056946d73` explicitly distinguishes:

- `kW3GLb7QfPg`: `sceAgcInit(uint32_t version)`;
- `23LRUSvYu1M`: `sceAgcInit_0090(void *state, uint32_t version)`.

The existing emulator export is the latter two-argument shape, despite its
short working name. An alias would therefore use the wrong register. The
upstream cube's two-argument declaration is incompatible with its linker's
chosen NID according to that reference. This is an upstream sample ABI issue
as well as a genuinely missing emulator export.

`Build-PS5LinkCube.ps1 -CorrectInitAbi` creates a distinct `app-init-abi.elf`,
changing only the declaration and call to `sceAgcInit(8)` in a generated copy.
The upstream checkout and original ELF remain unchanged. Its native rerun
confirms RDI=8 at the unresolved real NID; it still exits before rendering.
The next emulator change must select the per-guest defaults revision through
this one-argument entry point, with meaningful invalid-version and isolation
checks, then repeat this corrected-input run. Neither profile has rendered
cube pixels yet. The native initialization state and firmware behavior still
need careful scope boundaries; references are not console validation.

[AGC Gears](https://github.com/mpereiraesaa/ps5-agc-gears) at
`1ae1f9182abd2770c131b97419034fb85173c2dc` is retained as a later independent
depth-tested scene. Its native build requires public GFX1013 LLPC and a pinned
native foundation; those dependencies have not been built here. The simpler
cube already exposes the first actual compatibility gap without that setup.

## Initializer implemented — 9 October 2026

The real one-argument `kW3GLb7QfPg` entry now consumes RDI, creates the existing
managed register-default tables and stores initialization per guest memory.
The two-argument entry keeps RSI as revision and is named `sceAgcInit_0090`.
Unversioned public/internal defaults accessors return null before initialization
and the corresponding guest allocation afterward. Invalid reinitialization
preserves the prior state. This uses the existing supported revisions and
existing tables; it does not establish console-accurate defaults for every
firmware/revision, packet-mode workarounds or the complete driver setup.

API distinctions were researched in OpenAGC, but no Apache-licensed source body
was copied. New C# implementation uses local primitives. SharpEmu upstream
`b02c06181a` was inspected separately: its initialization is now split into
`AgcExports.Init.cs`, with compact versioned defaults. Whole-file replacement
would conflict with local changes and requires an independent migration.

CLI builds with zero warnings/errors; all 1248 library tests pass. New checks
cover both register conventions, initialization-dependent access, guest
isolation and rejection without losing earlier defaults. Native corrected cube
run `runtime-initializer-fixed.log` moves from missing init at import 5 to
missing `sceAgcLinkShaders` (`MqAdbRMdNz4`) at import 69. It passes both
CreateShader calls and the source's resource-descriptor checks before that.
Notifications remain unresolved; they are ignored by this sample but still
record a dispatcher error. Host exit is still 4; no cube pixels are claimed.

Next: establish shader-link output records using independent public references
and the actual mesh shader metadata. Do not return fake success or substitute
our synthetic shaders to declare this external renderer working.

## Partial independent rendering — 9 October 2026

`sceAgcLinkShaders` now exposes an explicitly experimental composition of the
existing local primitive-state and interpolant-mapping routines: two CX
primitive records followed by 32 interpolant records and three UCONFIG records.
The caller's 34/3 buffers are direct evidence for the extent, while the
composition is an engineering inference, not an independently verified complete
firmware implementation. Support is restricted to type-2 geometry plus type-1
pixel shaders, triangle primitive 4 and no hull shader. Other configurations
are rejected. Tests use reordered semantics and check the actual export-slot
mapping rather than always assuming identity.

OpenAGC `src/game_compat.c` at the previously pinned revision was inspected for
DRAW_INDEX_2 field order: maximum count, 64-bit base, count, initiator. No
Apache-licensed implementation body was copied. The local draw export formerly
accepted only initiator `0x40000000`, returning null for the cube's ordinary 0.
It now accepts both known variants and preserves the initiator. Zero-count
packets use maximum count 1 while keeping draw count 0. Packet-layout checks
cover both variants and zero-count behavior.

The sample's 3120-byte type-0 text notification now validates and delivers its
message to the emulator log. This is a log backend, not host desktop banners
or complete console notification-service emulation. Invalid, unmapped and
unterminated requests fail explicitly.

Native execution `runtime-indexed.log` reaches actual translated indexed draws.
`capture-rotation` contains four distinct Vulkan swapchain readbacks; a decoded
`rotation-contact-sheet.bmp` was visually inspected and shows changing cube
faces. **It also shows trails.** The source clears its framebuffer each frame
through direct CPU stores. Repeated CPU clearing of the GPU-owned image is not
correctly observed, so this is not a clean-animation pass. Flip resource-busy
messages also occur and require separate pacing/backpressure investigation.

The host return trampoline formerly overwrote guest RAX with the TlsGetValue
result; it now preserves the guest value in Win64-nonvolatile R12 until the
host stack is restored. The normal entry wrapper now loads initial RAX from
the guest CPU context instead of leaving the function pointer in it (which
made the no-op `_fini` look like a nonzero return). Reproduction:
`tools/native-probes/Test-TouchePX5-EntryExit.ps1` builds tiny native fixtures
with the same unchanged startup and verifies explicit exit 0 and exit 7.
Both pass; the CLI returns 0 and 4 respectively, so failure is not masked.
All 1257 library tests pass after these changes.

Final built-revision replay `runtime-final-linkage.log` completes the corrected
cube and unchanged startup/finalizer with `ORBIS_GEN2_OK`, no LastError, and
host exit 0. This confirms process completion, not visual correctness of the
remaining framebuffer-clear behavior.

Next: correct CPU/GPU ownership and visibility of framebuffer writes without
discarding GPU results, then verify clean rotation, capture stability and
presentation completion. Preserve the unchanged-input / init-ABI-corrected
input distinction; all rendering observations above use `app-init-abi.elf`.

### Sample-scoped CPU-write tracking replay

The existing guest-image tracker is off by default on Windows because native
page-fault synchronization regresses some other titles. A separate bounded
replay with `SHARPEMU_GUEST_IMAGE_CPU_SYNC=1` completes with host exit 0 and
produces four distinct presented readbacks. The decoded contact sheet
`capture-cpu-sync/rotation-contact-sheet.bmp` was visually inspected: the
background has the source's dark color and old cube positions no longer trail.
This resolves the observed clearing problem for this sample configuration;
it does not justify enabling the setting globally or declaring all image
ownership/coherency behavior solved.

`tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1` preserves/restores the
calling shell's environment, selects this setting only for the owned test
process, records the input hash and init-ABI correction, bounds execution,
and requires completion plus at least two distinct presented captures.
Its report explicitly does not equate different frame hashes with automatic
visual correctness. Main/exit/finalizer all succeed in this profile.

Next: repeated capture validation and GPU validation diagnostics, especially
busy flips and synchronization while reusing the two command/resource buffers;
then the next independent scene with depth and textures.

## Adapted D32 depth scene — 13 October 2026

`Build-PS5LinkCubeDepth.ps1` generates a separate source adaptation from the
same pinned cube, leaving the upstream checkout and native startup unchanged.
The D32/no-HTILE register contract is cross-checked against public Gears commit
`1ae1f9182abd2770c131b97419034fb85173c2dc`. The existing mesh shaders are reused;
this does not build Gears or validate its LLPC toolchain. Back-face culling is
disabled so depth testing, rather than culling, determines visibility.

An initial CPU-depth-clear version completed but showed disappearing geometry
when animated. Static controls retained geometry, isolating repeated clearing
as a problem. CPU writes to a reused Vulkan depth attachment are not currently
uploaded by the color-image write tracker. This remains an explicit gap.

The default adapted scene instead submits a real fullscreen triangle at depth
1 with identity transforms, ALWAYS comparison, depth writes enabled and color
writes masked. It then restores cube resources, LESS_OR_EQUAL comparison and
color writes. This exposed a separate emulator bug: with a pixel shader bound
and all color channels masked, target filtering discarded the attachment and
`CreateRenderState` returned defaults, losing viewport and depth state.
The translator now keeps valid bound masked targets for active depth/stencil
work. The existing pixel shader still runs and the color masks remain zero;
shader discards or depth exports are not bypassed. Ordinary targetless draws
without valid depth work keep their prior behavior.

Verification:

- `smoke-Depth-20261013-111508`: host exit 0, 600-frame source-loop completion,
  three distinct readbacks. The decoded contact sheet was visually inspected:
  complete rotating cube geometry, without the earlier missing fragments.
- Frozen forward/reverse face-order runs with depth enabled have identical
  final 1920x1061 BGRA captures: **0 differing pixels**. The corresponding
  disabled-depth controls differ in **365718 pixels**. Enabled versus disabled
  forward-order controls differ in **221216 pixels**. This verifies a specific
  order/occlusion relationship, not all depth/stencil correctness.
- All 1261 library tests pass; the four new regressions cover masked depth
  state, preserved viewport/color mask, invalid depth surfaces, inactive depth
  and ordinary color targets. The base cube smoke also completes with four
  distinct captures after the runtime change.

Reproduce each generated input and retain the capture folders printed by smoke:

```powershell
& ./tools/external-homebrew/Build-PS5LinkCubeDepth.ps1
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile Depth
& ./tools/external-homebrew/Build-PS5LinkCubeDepth.ps1 -FreezeView
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile DepthStatic
& ./tools/external-homebrew/Build-PS5LinkCubeDepth.ps1 -FreezeView -ReverseFaces
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile DepthReverseStatic
& ./tools/external-homebrew/Build-PS5LinkCubeDepth.ps1 -FreezeView -DisableDepth
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile DepthDisabledStatic
& ./tools/external-homebrew/Build-PS5LinkCubeDepth.ps1 -FreezeView -DisableDepth -ReverseFaces
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile DepthDisabledReverseStatic
```

Pass the four static capture folders, in the order shown above, to
`py -3 tools/external-homebrew/Compare-PS5LinkCubeDepth.py <enabled> <enabled_reverse> <disabled> <disabled_reverse> --output <report.json>`.
Keep the window dimensions fixed. The comparison validates complete BGRA file
sizes and matching dimensions before comparing pixels. Build reports mark
source adaptations and the clear method; smoke verifies each ELF hash against
its build provenance. CPU-clear diagnosis remains selectable separately with
`-CpuDepthClear` and smoke profile `DepthCpuClear` (animated); its process pass
must not be mistaken for a correct image.

Remaining: CPU depth uploads/ownership, nonuniform depth contents, stencil,
textures, independent multi-object scenes and Gears compiler setup. These
results do not establish commercial PS5-game or GTA VI compatibility.

## Adapted texture plane — 13 October 2026

The installed MSVC and llvm-mingw compilers did not register an AMDGPU target.
Official LLVM 18.1.8 sources were downloaded to the separate local tool folder
`C:/Dev/tools/llvm-amdgpu-18.1.8`; `Build-LLVMAMDGPUTools.ps1` builds only
`llvm-mc` and `llvm-objcopy` with AMDGPU support. The Windows tar lacked xz
support, so extraction uses Python's standard library with the data safety
filter and completion markers. The build detected DIA but lacked `atlbase.h`;
disabling optional DIA SDK support resolved that dependency without installing
ATL. A final script replay succeeds and both tool binaries are present.

Pinned official release archives:

- [llvm-18.1.8.src.tar.xz](https://github.com/llvm/llvm-project/releases/download/llvmorg-18.1.8/llvm-18.1.8.src.tar.xz),
  SHA256 `f68cf90f369bc7d0158ba70d860b0cb34dbc163d6ff0ebc6cfa5e515b9b2e28d`.
- [cmake-18.1.8.src.tar.xz](https://github.com/llvm/llvm-project/releases/download/llvmorg-18.1.8/cmake-18.1.8.src.tar.xz),
  SHA256 `59badef592dd34893cd319d42b323aaa990b452d05c7180ff20f23ab1b41e837`.

These are locally recorded download hashes, not a claimed signature audit.
The build script checks exact sizes and hashes before extraction. It does not
change system PATH or install drivers. This assembler is **not LLPC** and does
not satisfy the Gears GLSL/GFX1013 compiler dependency.

`Build-PS5LinkTextureShaders.ps1` assembles the unchanged pinned
`shaders/src/textured_p.s` for gfx1030, matching upstream `build.sh`. The public
packer generates its one-texture/one-sampler container from the retained
GPL-3.0 SharpProspero pixel container. Packing passes its container self-check;
the resulting program is 116 bytes and its container is 2112 bytes. Two-texture
sources remain excluded because their resource contract requires a different
container. Successful assembly is not console hardware validation.

`Build-PS5LinkTexturePlane.ps1` creates a separate source adaptation from the
init-corrected cube. The mesh vertex program actually exports `normal` through
TEXCOORD0, not the input UV field. The local plane therefore supplies UV through
`normal.xy`, with identity model/MVP constants; it does not claim unchanged
upstream mesh semantics. A 64x64 linear RGBA8 texture carries an 8x8 checker
pattern, red/green lower quadrants and blue/yellow upper quadrants, with bright
255 and dark 48 cell colors. Native startup remains unchanged. The SCE ELF
links 30 imports and has zero unpatched relocations.

`smoke-TextureStatic-20261013-114116` completes its 600-frame source loop with
host exit 0 and three captures. Its compact draw trace reports
`64x64:f10/n0/t9/dFAC`; strict descriptor validation is enabled and no fallback
texture is reported. The decoded contact sheet was visually inspected and
shows the expected colored checker plane. The cell validator samples all 64
cell centers away from edges: **64/64 exactly match expected BGRA values**.
The first captured frame is blank, so it is not counted as a rendered-image
success; the final capture is used for the palette check. A negative control
using the blank first capture is rejected. This is bounded evidence for one
static linear texture and normalized coordinate transport. Nonuniform depth
uploads, texture streaming/updates, mipmapping and multi-texture shader
resources remain separate work.

Reproduce with the downloaded LLVM archives in the tool folder:

```powershell
& ./tools/external-homebrew/Build-LLVMAMDGPUTools.ps1
& ./tools/external-homebrew/Build-PS5LinkTextureShaders.ps1
& ./tools/external-homebrew/Build-PS5LinkTexturePlane.ps1
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureStatic
py -3 tools/external-homebrew/Check-PS5LinkTexturePlane.py <printed-capture-folder>
```

The cell report explicitly does not certify all pixels or commercial-game
compatibility. CPU depth clear replay `smoke-DepthCpuClear-20261013-113709`
still visually loses most rotating geometry despite completing normally;
this diagnostic profile must not be presented as a visual pass.

### Combined texture/depth cube

`Build-PS5LinkTextureCube.ps1` builds a separate animated source adaptation
using the validated plane's checker texture initialization and the depth
cube's real fullscreen depth reset. Each face's UV is transported through
`normal.xy`, with identity model constants for those interpolants; the MVP
still rotates positions normally. All six faces are submitted without culling.
The texture resources are bound before both the masked depth-reset draw and
the cube draw. The final native ELF links 31 imports with zero unpatched
relocations; original startup and the source checkout remain unchanged.

`smoke-TextureDepth-20261013-114638` completes 600 source frames with host exit
0 and three distinct presented captures. Strict descriptors and the compact
trace confirm the real 64x64 linear RGBA8 texture without fallback. The decoded
contact sheet was visually inspected: the later captures show complete cube
faces with the checker quadrants rotating with the geometry, without trails
or the earlier disappearing fragments. The first readback is blank. The
plane's 64-center palette check is not incorrectly applied to a projected
cube; no automated whole-image or perspective-accuracy claim is made.

```powershell
& ./tools/external-homebrew/Build-PS5LinkTextureCube.ps1
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureDepth
```

### Two overlapping textured objects

`Build-PS5LinkOverlappingCubes.ps1` generates a separate two-object adaptation.
It shares mesh, index buffer and texture, but allocates independent constants
for each object in each of the two frame slots. It positions the cubes at
different lateral offsets/depths and uses different rotations. Both draws
share the frame's D32 attachment and occur after one GPU depth-reset draw.
The reverse-order profile changes only object submission order, without
rewriting a constant buffer already referenced by the first recorded draw.

Verified captures on 2026-10-13:

- Forward depth: `smoke-TextureOverlapStatic-20261013-123413`.
- Reversed depth: `smoke-TextureOverlapReverseStatic-20261013-123425`.
- Forward without depth: `smoke-TextureOverlapDisabledStatic-20261013-123438`.
- Reversed without depth: `smoke-TextureOverlapDisabledReverseStatic-20261013-123450`.
- Animated: `smoke-TextureOverlap-20261013-123511`.

`overlap-order-comparison.json` records **0** depth-enabled order differences,
**145274** depth-disabled order differences and **355811** enabled-versus-disabled
forward differences. Enabled captures share SHA256
`06f0a5227a6d3bd18b2f0683306689cc9419d3c9fae91e67b3a2d74aa4615f04`.
The decoded four-way contact sheet was visually inspected: enabled geometry
is consistent in both orders; disabled geometry visibly overwrites nearer
surfaces. All five inputs finish 600 source frames with host exit 0. The
animated run yields four distinct captures. These are adapted unsigned native
ELFs, with the original source checkout and startup preserved.

```powershell
& ./tools/external-homebrew/Build-PS5LinkOverlappingCubes.ps1 -FreezeView
& ./tools/external-homebrew/Build-PS5LinkOverlappingCubes.ps1 -FreezeView -ReverseObjects
& ./tools/external-homebrew/Build-PS5LinkOverlappingCubes.ps1 -FreezeView -DisableDepth
& ./tools/external-homebrew/Build-PS5LinkOverlappingCubes.ps1 -FreezeView -DisableDepth -ReverseObjects
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureOverlapStatic
# Run the matching ReverseStatic, DisabledStatic, DisabledReverseStatic profiles.
# Compare their actual capture folders using Compare-PS5LinkCubeDepth.py.
& ./tools/external-homebrew/Build-PS5LinkOverlappingCubes.ps1
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureOverlap
```

### Reused texture with CPU palette updates

`Build-PS5LinkTextureUpdates.ps1` adapts the flat plane to rotate RGB channels
every 150 source frames. It reuses one 64x64 linear allocation and descriptor;
the CPU changes its bytes before each submission. This probe retains the
source's suspend/vblank boundaries and sample-scoped CPU image synchronization.
It does not establish correctness for asynchronous guest writes, all console
cache/fence semantics, tiled storage, mipmaps or texture streaming.

`smoke-TextureUpdates-20261013-123620` finishes 600 frames with host exit 0.
The real texture binding passes strict descriptor checks without fallback.
`Check-PS5LinkTextureUpdates.py` checks exact expected colors at 64 cell centers
in each capture: all four pass, with ordered palettes **0,1,2,0**. It requires
the changed palettes and forward cyclic progression, rejecting stale images.
The original static plane capture folder is a negative control and is rejected.
One initial uniform-background readback may be skipped; subsequent invalid
captures fail. This is a sampled color check, not whole-image certification.

```powershell
& ./tools/external-homebrew/Build-PS5LinkTextureUpdates.ps1
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureUpdates
py -3 ./tools/external-homebrew/Check-PS5LinkTextureUpdates.py <capture-folder>
```

### Independent texture bindings per draw

`Build-PS5LinkTextureBindings.ps1` generates two separated planes with independent
constant buffers in each frame slot and two texture allocations. The geometry
and shader programs are shared. The first plane uses the original checker
palette; the second texture rotates RGB channels once. Each draw explicitly
binds its own constant-buffer and texture descriptor, while sharing the sampler.
`PS5LinkTexturePair.ps1` provides the same source-generation helper for these
planes and `Build-PS5LinkOverlappingCubes.ps1 -DistinctTextures`.

Verified on 2026-10-13:

- `smoke-TextureBindingsStatic-20261013-140754`: 64/64 cells on each object.
- `smoke-TextureBindingsReverseStatic-20261013-140807`: 64/64 on each object,
  exactly 0 whole-frame pixel differences from forward submission.
- `smoke-TextureBindingsSharedStatic-20261013-140819`: intentional shared binding
  matches 64/64 left cells and 0/64 right cells, with 652032 whole-frame pixel
  differences. This ensures the oracle detects the wrong texture.

`Check-PS5LinkTextureBindings.py` reads actual saved BGRA images, validates their
dimensions/byte counts, samples fixed cell centers on both transformed planes,
requires order-invariant images and checks the shared-binding negative control.
The forward/reverse traces use both real addresses `0x11418000` and `0x1141C000`.
The decoded contact sheet was inspected and shows distinct palettes on both
planes, identical under reversed order, and the wrong right palette in the
control. The report is `texture-bindings-comparison.json` under the cube output.

The cube pair uses real addresses `0x12824000` and `0x12828000`:
`smoke-TexturePairOverlapStatic-20261013-140919` and
`smoke-TexturePairOverlapReverseStatic-20261013-140931` have identical final
captures, recorded in `texture-pair-overlap-comparison.json`.
`smoke-TexturePairOverlap-20261013-140943` completes 600 frames with host exit 0
and four distinct captures. Its decoded contact sheet was inspected: the first
readback contains partial geometry, while later readbacks show both complete
textured rotating objects. Initial capture completeness is not established.
All six probes finish their native source loops with host exit 0, strict
descriptor validation and no fallback. Smoke validation now additionally
requires at least two real texture addresses for distinct-texture profiles.

```powershell
& ./tools/external-homebrew/Build-PS5LinkTextureBindings.ps1
& ./tools/external-homebrew/Build-PS5LinkTextureBindings.ps1 -ReverseObjects
& ./tools/external-homebrew/Build-PS5LinkTextureBindings.ps1 -SharedTextureControl
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureBindingsStatic
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureBindingsReverseStatic
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureBindingsSharedStatic
py -3 ./tools/external-homebrew/Check-PS5LinkTextureBindings.py <forward-folder> <reverse-folder> <shared-control-folder> --output <report-path>
& ./tools/external-homebrew/Build-PS5LinkOverlappingCubes.ps1 -DistinctTextures
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TexturePairOverlap
```

These remain locally adapted unsigned native inputs. Upstream source/startup are
preserved; texture/shader scope remains single-mip linear RGBA8. This does not
validate concurrent writes, tiling, general perspective precision or commercial
titles. No emulator runtime source changed for this stage.

### Independent texture updates expose initial-frame corruption

`Build-PS5LinkTextureBindings.ps1 -UpdateSecondTexture` preserves the first
texture and cycles only the second allocation's palette every 150 frames.
Both normal and `-ReverseObjects` variants retain separate constant buffers
and texture descriptors. `Check-PS5LinkIndependentTextureUpdates.py` requires
64 correct cell centers on each object for every nonblank saved frame and an
ordered changing second palette, rejecting corruption of the first object.

Early replays completed but failed image validation:
`smoke-TextureBindingsUpdates-20261013-141258` had only the second plane in its
first capture; `smoke-TextureBindingsReverseUpdates-20261013-141310` had only
the first. The later captures passed. These failed reports are retained.

An attempted seed-fingerprint change was initially credited with successful
replays. That causal claim was not established: detailed logs show actual targets
use B8G8R8A8Unorm and tile27, while the helper only accepted linear RGBA8/R32.
Its branch did not cover this scene. The helper and both calls were removed;
the following successful runs are historical observations, not proof of that
runtime fix. This correction supersedes the earlier interpretation.

Historical native replay, with unchanged input ELFs:

- `smoke-TextureBindingsUpdates-20261013-141541`: all four captures pass 64/64
  cells per object; second palette sequence 1,2,0,1. Contact sheet inspected.
- `smoke-TextureBindingsReverseUpdates-20261013-141555`: initial uniform
  background, then all three captures pass both objects, palettes 2,0,1.
- `smoke-TexturePairOverlap-20261013-141608`: 600 frames, four distinct captures;
  first is uniform background, later show complete textured objects. Contact
  sheet inspected. This does not resolve initial presentation timing.

Release CLI build succeeds with the existing SHEM006 catalog warning.
All 1261 library tests pass. No full commercial-game compatibility is inferred.
Base native replay `smoke-Base-20261013-141628` finishes 600 frames with four
distinct captures. The single-texture update replay
`smoke-TextureUpdates-20261013-141641` passes all 64 centers in each nonblank
capture (palettes 1,2,0 after an initial uniform background). The independent
update oracle rejects the original static two-plane capture as a negative
control because it never changes the second palette.

```powershell
& ./tools/external-homebrew/Build-PS5LinkTextureBindings.ps1 -UpdateSecondTexture
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureBindingsUpdates
py -3 ./tools/external-homebrew/Check-PS5LinkIndependentTextureUpdates.py <capture-folder>
& ./tools/external-homebrew/Build-PS5LinkTextureBindings.ps1 -UpdateSecondTexture -ReverseObjects
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureBindingsReverseUpdates
```

### Serialized frame ownership diagnostic

Detailed logs show the guest can queue many frames before Vulkan starts drawing.
The original samples call SuspendPoint and Vblank, neither of which proves GPU
completion. Both command slots and the updated texture can therefore be reused
while earlier queued work still needs their memory. The RGBA seed-helper guard
also excludes the scene's actual BGRA/tile27 render targets, disproving attribution
of the old success to that helper. The helper was removed as described above.

`Build-PS5LinkTextureBindings.ps1 -UpdateSecondTexture -FenceFrames` adds a
separate serialized diagnostic: initialize an aligned 64-bit label, append
standard RELEASE_MEM after SetFlip, then poll the real terminal zero before
another frame can rewrite memory. The poll yields with sceKernelUsleep(1000)
and uses sceKernelGetProcessTime for a five-second deadline. Counting calls to
SuspendPoint as milliseconds was rejected after an initial timeout, because
that call can return immediately under native execution. The terminal packet
contract follows the reviewed public `ps5-agc-gears/src/ps5_present.c`, pin
`1ae1f9182abd2770c131b97419034fb85173c2dc`; that project is not built or claimed
to run. The source generator preserves the upstream checkout/startup and emits
32 bound native imports with zero unpatched relocations.

Final-runtime runs, after removal of the unvalidated seed helper:

- `smoke-TextureBindingsUpdatesFenced-20261013-142325`: 600 frames; four captures,
  all 64/64 cells per object; complete first frame, palettes 1,1,2,0.
- `smoke-TextureBindingsReverseUpdatesFenced-20261013-142343`: 600 frames; five
  captures, all 64/64 cells per object; complete first frame, palettes 1,1,2,0,1.

Use `--require-complete-first-frame` to reject an initial uniform background
rather than silently skipping it. Repeated captured palettes are allowed because
the first capture and presentation 150 can both occur before the first change;
palette changes must still advance cyclically and include the changed colors.
Old partial-frame and static captures remain negative controls. This demonstrates
correct image cells in a serialized adapted scene, not a general presentation
fix or production throughput. Both objects, changing palettes and first-frame
content are checked from actual readbacks. All 1261 library checks pass on the
final runtime; Release build succeeds with existing SHEM006.
After adding the packet-capacity guard, forward replay
`smoke-TextureBindingsUpdatesFenced-20261013-142540` also passes all five captures
and the strict first-frame requirement (palettes 1,1,2,0,1). Its decoded first
image was inspected: both checker planes are present. Background bytes and
whole-image equivalence to a console are not certified by the 128-center check.
The old unfenced reverse replay `smoke-TextureBindingsReverseUpdates-20261013-141555`
is rejected when the complete-first-frame requirement is enabled.

```powershell
& ./tools/external-homebrew/Build-PS5LinkTextureBindings.ps1 -UpdateSecondTexture -FenceFrames
& ./tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1 -Profile TextureBindingsUpdatesFenced
py -3 ./tools/external-homebrew/Check-PS5LinkIndependentTextureUpdates.py <capture-folder> --require-complete-first-frame
# Repeat with -ReverseObjects / TextureBindingsReverseUpdatesFenced.
```

Next: retire two independent frame slots with their own fences before reuse,
including per-slot mutable texture storage. Keep immutable flip snapshots ahead
of their terminal fences; do not mutate shared texture bytes while another slot
can still sample them. Then compare perspective/UV reference images and investigate
guest CPU depth-image ownership. Legacy unfenced probes remain diagnostic only;
their initial-frame behavior has not been fixed globally. Gears still needs its
pinned LLPC/native foundation. GTA VI has not been run.
