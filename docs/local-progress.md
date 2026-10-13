# Local engineering checkpoint — 2026-10-13

This remains an experimental PS5 emulator. Commercial-game compatibility and GTA VI
playability have not been demonstrated. There is no supported whole-emulator completion
percentage or release-date estimate.

## Latest verified state

- **The real Gears native application is now built on Windows.**
  The pinned LLPC compiler and llvm-readelf/objcopy completed with exit 0
  (`build-report.json`, 224.5-second final batch). Our host wrapper adds Vulkan
  flat/header-root include paths and scoped MSVC `__cplusplus`/strcasecmp
  compatibility for PAL consumers; upstream sources remain unchanged. The
  previously failing `vkgcUtil.cpp` now compiles through the real build.
  `Build-GearsShadersWindows.ps1` compiled the actual upstream GLSL pipeline to
  GFX1013/0x42, relocation-free PAL NGG: 384-byte GS and 160-byte PS stages,
  inspected metadata and derived the real AGC header. `Build-GearsNativeWindows.ps1`
  compiled main/real shader assets, linked all native units and converted/packed
  `gears-native-windows/app/PPSA99997/eboot.bin`; container integrity passes.
  ELF SHA256 `FCFC7A3554524F571AEF32D069073B48DD70A199AA99C4F32750F14946FD78D3`;
  SELF SHA256 `1953898EAF1D49CC5CA369EA18D831ACAECD8F4C7496F92BEADC82E360013862`.
  All outputs/logs/reports remain under the isolated toolchain directory.
  **Gears has not executed.** The POSIX network host test is still unexecuted,
  so full upstream `make test` completion is not claimed. Next: resolve that
  host-test environment, then a bounded native boot with retained telemetry;
  passing builds/metadata do not certify guest execution or correct rendering.

- **A complete upstream foundation example now builds/packages on Windows.**
  Four original source/CRT units link against the pinned SDK. The actual native
  converter failed because it required unused `.data.rel.ro`; a separate local
  GPL-3.0-or-later overlay selects the real stored RELRO start, retaining the
  existing layout checks. The original failure is reproduced on the same PIE;
  two repaired conversions give identical ELF/SELF hashes and valid container
  integrity. Original foundation sources/linker script remain clean. The app
  folder is built, not executed. See `Build-GearsFoundationDemoWindows.ps1`.
  Meanwhile the compiler encountered a real spvgen/glslang mismatch: the earlier
  AMDVLK tools manifest pinned 2022 dependencies for 2025 code. These have been
  replaced by spvgen's own four `CHANGES` pins, with old sources preserved;
  the source helper now rejects a lock inconsistent with that upstream file.
  Reconfiguration succeeds; the previously failing spvgen object now compiles
  in the real MSVC/Ninja path (`build-spvgen-regression.log`, 19/19 steps).
  This repairs that concrete failure, not the complete compiler build.

- **Gears' missing driver cursor-wait imports now resolve to implemented HLE.**
  Added `sceAgcDriverGetWaitRenderingPacketSizeInDwords` and
  `sceAgcDriverWaitUntilSafeForRendering` with their derived/validated NIDs.
  The cursor ABI emits the existing ordered seven-dword flip-wait encoding;
  it does not wait on the CPU or claim Sony's packet representation. Checked
  arguments, pointer overflow/alignment, overlap and writable packet bounds
  prevent malformed calls from advancing the cursor. Eleven new cases cover
  the successful high-address cursor, packet/tail bounds and rejected calls.
  The full Release library suite now passes 1275/1275 and the Windows CLI builds
  without warnings/errors. This is not an in-game or new GPU validation result;
  older captured GPU evidence retains its historical runtime hashes. Next is
  the actual LLPC shader build, followed by native main/link and execution.

- **The native foundation runtime now builds reproducibly on Windows.**
  `Build-GearsFoundationWindows.ps1` builds the pinned upstream generator and
  packaging tool with static zlib. Two independent raw ELF outputs and two
  signed runtime containers match the exact upstream SHA256 contracts;
  `runtime-windows/report.json` retains the inputs and tool hashes. Original
  foundation sources are unchanged. This builds the library, not a title or
  evidence that the runtime executes. The exact SDK v0.42 archive is also cached.
  `Test-GearsWindowsCrossCompile.ps1` verifies every extracted SDK file against
  that archive, cross-compiles 29 genuine upstream source units to x86-64 ELF,
  and links/inspects both AGC import stubs. Both alternative Clang 23.1.0 and
  upstream-baseline Clang 18.1.8 passed independently, with separate reports.
  Clang 18 is the default and is isolated under artifacts. Main and shader
  assembly deliberately remain absent until real LLPC shader outputs exist.
  Native Windows host contracts passed 26/27; the POSIX network test remains
  unexecuted. See `docs/gears-build-preflight.md` for exact scope and next gates.

- **Earlier pinned Gears compiler build history (superseded by completion above).**
  Thirteen source commits are fetched/verified; a separate MSVC/Ninja wrapper
  configures LLPC/LLVM successfully with bounded memory use. Official pinned
  DXC and isolated PyYAML supply missing host dependencies. Four Gears Python
  contracts and its 122-file publication audit pass. A real build exposed optional DIA/PDB support missing ATL; that
  upstream option is disabled. The localized include-prefix problem is also
  corrected: Ninja now records 122 headers for the checked object. The original
  failing PDB.cpp compiles with DIA disabled. A corrected bounded batch made
  367/2446 build steps before its owned timeout; a subsequent 900-second batch
  reached 830/2078 steps; the next bounded batch reached 493/1248, both without
  a compilation failure. Completed objects are retained. Compiler
  binaries, Gears shaders/native title and a Gears emulator launch remain
  unverified. Resume bounded compiler batches via `docs/gears-build-preflight.md`;
  next gate is an actual GFX1013 PAL shader, followed by native title linking
  and the still-unexecuted POSIX network host test.

- **Shared-page producers now evolve separate left/right palettes.**
  Left changes every 150 frames and right every 100. Both 600-frame native
  runs pass all scheduling/retirement checks, 64 immutable cell samples and
  32 samples per producer region in each of seven captures. Palette pairs
  are [1,1], [1,1], [2,2], [2,0], [0,1], [1,2], [1,0]; every forward/reverse
  whole-frame pair is byte-identical. The missing-right control completes
  jobs and frames but fails image validation: right stays phase 1 while
  left follows its own schedule. Source/ELF/linker/runtime hashes are retained
  in `guest-producer-independent-columns-comparison.json`. GPU readers remain
  drained during fills. No runtime change or new full-suite result is claimed.
  Next: resume the Gears toolchain blocker from `docs/gears-build-preflight.md`,
  rather than adding another same-scene variant without a concrete defect.

- **Guest producer builds now zero alignment gaps instead of leaking heap bytes.**
  Rebuilding unchanged source exposed three varying ELF padding bytes in the
  SDK linker. A local GPL-3.0 overlay fills code/rodata/data gaps with zeros;
  the pinned SDK and historical linker are unchanged. Two fresh builds of the
  same column fixture now have identical source/ELF hashes. Final positive,
  reverse and missing-right GPU runs after the repair retain their inputs and
  linker provenance; image checks and all fourteen baseline/order comparisons
  pass, with the deliberate missing writes correctly rejected. Runtime code
  is unchanged. Next remains independent region palettes below.

- **Disjoint native column writers now share all four pages of one texture.**
  Two workers each fill 64 row spans of 128 bytes, owning left/right columns
  without overlapping bytes. Ready/half-fill barriers, 300 jobs/checks each,
  full-width joins and 600 retired frames pass. Seven captures per order pass
  every 64-cell sample; seven reverse and seven prior row-halves full-frame
  pairs are byte-identical. The skipped-right-half control completes execution
  but correctly fails update checking with 32/64 phase-2 matches. Input/source
  provenance and matching runtime hashes are retained. GPU readers are drained
  before every refill, so active-writer dirty polling and overlapping-byte
  stores are not certified. Next: independently changing left/right palettes,
  with separate image expectations for each producer's region.

- **Two native producers now fill disjoint halves of one shared texture.**
  Each worker owns 8192 bytes; both GPU frame slots sample the same mutable
  allocation. Ready/half-fill barriers, 300 jobs/checks per worker, full-width
  joins and 600 retired frames pass with both consumers retired before refill.
  Seven captures per order pass every 64-cell sample per object. All seven
  reverse and seven separate-allocation baseline pairs are byte-identical.
  The missing-half control completes jobs/barriers/frames but is correctly
  rejected: phase-2 images update exactly 32/64 cells of the second object.
  This covers disjoint page-aligned regions and explicitly drained consumers;
  shared-page/overlapping-byte writes and GPU reading during refill remain open.
  Next: disjoint columns sharing OS pages in the same texture, preserving the
  existing two-consumer retirement and independent image checks.

- **Two outstanding guest producer jobs now have a bounded GPU checkpoint.**
  Main issues both slot requests before waiting. Both workers must start before
  either fills, and each must observe the peer's half-fill before completing.
  All 300 batch generations, 300 half checks per worker, full-width joins and
  600 retired frames pass. Seven captures per positive order pass all 64-cell
  samples per object; seven forward/reverse and seven previous sequential
  whole-frame pairs have zero pixel differences. The same-runtime frozen-palette
  control passes all job/barrier accounting but correctly fails update checking.
  This establishes the paired disjoint-allocation handoff, not throughput or
  simultaneous GPU submission. Both GPU slots are drained before each batch.
  Next: two workers writing disjoint halves of one shared texture, with both
  GPU consumers retired before any refill.

- **Guest-created workers now feed a real sampled-texture GPU scene.**
  Two native workers each fill their own mutable texture slot 300 times through
  request/completion semaphores. Main waits for CPU completion before drawing,
  and retires each slot's GPU fence before reuse. Both full-width joins, 600
  submitted/retired frames, 598 reuses and max_pending=2 pass. Forward/reverse
  runs each pass seven independent 64-cell checks per object; all seven matched
  whole-frame pairs are byte-identical. A frozen-palette control completes the
  same jobs/frames but correctly fails the independent update checker. Source,
  ELF and build provenance are retained with final captures. This is a scoped
  two-owner handoff with one outstanding CPU job, not simultaneous guest writers
  or arbitrary game compatibility. Fixture builds with `-mno-red-zone`.
  See `tools/external-homebrew/GuestTextureProducers.md`.
  Next: test two outstanding guest producer jobs on disjoint image ranges before
  attempting concurrent GPU submission or shared-resource mutation.

- **Guest semaphore waits expose and now cover the resumed-return path.**
  Two native guest workers each block twice on a zero-count semaphore; main
  releases two tokens per phase and checks the halfway counter is 1000 with no
  completed workers. Four actual block/wake pairs and final counter=2000 pass
  in ordinary-return and explicit-exit modes, both with write-watch and disabled
  tracking. The initial probe found ordinary resumed returns incorrectly left
  the last import's zero in context RAX. A local continuation return thunk now
  captures the full result before the common host sentinel. This covers this
  bounded semaphore continuation sequence, not arbitrary synchronization or
  guest GPU producers. Next: connect guest producers to tracked image data.

- **Native guest-created threads now preserve full-width return values.**
  An original unsigned SCE ELF starts two workers, verifies 2000 shared atomic
  increments and 1000 local increments each, then joins both. The first run
  exposed sign extension/truncation at the managed int native-call boundary.
  The native thread entry thunk now captures full RAX before that boundary,
  distinguishing ordinary returns from explicit `scePthreadExit` results already
  stored in the CPU context. Both full-width returns (`12345678CAFE0001/2`) pass
  with ordinary and explicit-exit variants, in fresh write-watch and disabled
  processes. CLI builds and 1264 library checks pass. This is guest create/join
  validation, not GPU producer concurrency or complete thread synchronization.
  Reproduction: `tools/native-probes/fixtures/ps5-touche-guest-threads/README.md`.
  Next: cover guest producer synchronization and then tracked image writes from
  guest workers before combining them with a GPU scene.

- **Concurrent native writes and dirty-range resizing now have scoped checks.**
  Four Windows workers issue 25,600 native leaf writes while two workers poll
  the real HLE tracker. Each final run preserves 409,600 red-zone markers and
  passes 600 owner checks over 200 rounds; measured polling windows overlap
  actual write completions. A separate poll/free check forgets all released
  owners after writers join. A new reproduction exposed a lost dirty flag when
  Track replaced a resized range; the runtime now carries the unconsumed flag
  and forces full-range synchronization. Both growing/shrinking regressions
  pass; final library suite is 1264/1264. This covers the bounded Windows worker
  pattern, not the PS5 guest thread scheduler or all concurrent memory races.
  The final write-watch GPU scene independently samples two textures: all 14
  captures pass 64 cell checks per object, with one immutable and one cycling
  palette. All seven matched forward/reverse whole frames are byte-identical.

- **An opt-in Windows write-watch runtime avoids this fixture's red-zone
  corruption without changing its executable.** Host reservations enable OS
  write watch before commit; polling resets page state while distributing
  changes to all overlapping image owners. Released allocations are forgotten
  under the polling lock. Native pages stay writable. The exact prior unguarded
  red-zone ELF passes all five CPU probes and 210 background points in each of
  seven captures, cycling 0,0,1,1,2,0,0 over 600 retired submissions. Same-input,
  same-runtime controls confirm that native fault mode still corrupts values
  and disabled tracking leaves the GPU background at phase 0. Fresh-process HLE
  checks cover 100 write generations, aliases, registration and release; all
  1262 library tests pass. Select only with `SHARPEMU_GUEST_IMAGE_CPU_SYNC=write-watch`
  or smoke `-UseWriteWatch`. This is a scoped runtime fix for tracked writable
  image memory; other faults, allocation types, concurrency and performance
  remain unverified. See `tools/native-probes/write-watch-tracker/README.md`.

- **Windows red-zone corruption is now isolated outside the emulator.** A
  six-case native probe uses a handler containing no managed calls and snapshots
  guest-style stack markers before its first native call or push. Two fresh
  processes reproduce two damaged qwords already at handler entry; offsets vary.
  No-fault, reserved-frame and guarded-store controls preserve all 16 markers.
  This confirms a Windows exception-path / guest stack-contract incompatibility,
  not a graphics shader or detiler error. A source-adapted cube with one guarded
  register-addressed store completes 600 frames and passes all 210 background
  samples in each of seven captures, cycling 0,0,1,1,2,0,0 with correct CPU probes.
  That inline assembly also changes compiler vectorization and removes the
  original fill function's red-zone spills, so cube success alone is not a
  controlled proof of general red-zone protection. Runtime code is unchanged;
  arbitrary guest executable instrumentation remains unimplemented. Details in
  `tools/native-probes/red-zone-fault/README.md` and `independent-3d-homebrew.md`.

- **Patterned tiled CPU backgrounds now pass initial seeding and subsequent
  updates in the owned cube fixture.** A native direct bit-matrix writer fills
  tile27 BGRA8 with a logical color grid; the capture checker calculates expected
  colors without the emulator's detiler. Frozen forward/reverse captures pass
  **210 background + 247 object samples each** and all nine saved images are
  byte-identical. Animated output passes **210/210 background samples on all seven
  captures**, with phase sequence **0,0,1,1,2,0,0** and native CPU source probes
  correct at frames 0/149/299/449/599. Each run retires 600 submissions with 598
  reuses and max_pending=2. A discovered Windows native-fault/red-zone interaction
  corrupts the default red-zone writer after its initial frame; this source
  adapter now builds with `-mno-red-zone`. The explicit red-zone control still
  fails, whereas disabling fault tracking preserves its CPU values but prevents
  background updates. This is a fixture compilation adaptation, not a general
  red-zone fix for arbitrary games. Runtime source and default seed setting did
  not change in this stage. See `independent-3d-homebrew.md` for controls/scope.

- **Initial CPU background seeding now passes a controlled Vulkan opt-in.**
  Registration-only display buffers are distinguished from published GPU work;
  a bounded first-draw snapshot detiles the supported tile27 RGBA8/BGRA8 surface.
  `-SeedRegisteredDisplayBuffers` enables the experiment only in the smoke process.
  On the final runtime, five frozen forward and five reversed captures are all
  byte-identical and pass **247 object samples + six exterior background samples**
  each, including the first. Same-input/same-runtime seeding-off control fails
  six first-background points, while its last frame is identical to seeded output.
  A separate native no-CPU-fill control preserves all 247 object samples but fails
  every background check, demonstrating real guest data dependence. Animated
  600-frame output was inspected. All runs retire 600 frames with 598 reuses and
  max_pending=2. **1262/1262 library tests pass** and Release CLI builds. This fixes
  this registered uniform-background fixture under the opt-in; default behavior,
  spatially varying tiled seed accuracy and commercial titles are not established.

- **Two rotating textured cubes now use two owned frame slots with real GPU
  retirement.** All five new profiles finish 600 submissions / 600 retirements /
  598 reuses with measured max_pending=2 and three real texture addresses.
  Frozen depth-enabled forward/reverse captures differ by **0 pixels**; disabling
  depth produces **163077** order-dependent pixels and **351638** differences
  from the enabled forward result. A separate CPU triangle/projection reference
  checks **176 first-object + 71 second-object interior samples**, with **zero
  mismatches in all five forward and two reversed captures**, including the first.
  Disabled-depth controls fail 199/172 samples; deliberately affine UVs fail 129,
  and a shared-texture reference fails all 71 second-object samples. Animated
  first/later images were inspected and show both cubes. The first background
  remains black instead of the later dark fill; this does not claim complete
  first-frame correctness. Runtime source is unchanged. Detailed commands,
  evidence and the limited reference scope are in `independent-3d-homebrew.md`.

- **Two frame slots now own their mutable resources until real GPU retirement.**
  `Build-PS5LinkFrameSlots.ps1` generates an adapted native two-plane scene with
  separate terminal labels and mutable texture allocations per slot, alongside
  the existing per-slot command/constants state. It waits only when reusing the
  selected slot, then drains both slots before exit. Both submission orders
  complete **600 submissions / 600 retirements / 598 reuses**, with measured
  **max_pending=2**. Real traces bind three distinct texture addresses (one
  immutable, two mutable). All five saved captures in each run pass **64/64 cell
  centers per object**, including the first; all five forward/reverse whole-frame
  comparisons have **0 differing pixels**. First/last decoded images were
  inspected. This is validated ownership in this adapted scene, not a general
  throughput gain, a console-equivalent image or commercial-game compatibility.

- **Serialized GPU retirement gives complete first frames in both updated-plane
  submission orders.** New fenced diagnostic profiles append terminal RELEASE_MEM
  after the ordered flip and wait for the real completion label before another
  CPU rewrite. Forward/reverse final-runtime replays finish 600 frames and pass
  64/64 cell centers per object in every capture, including the first. The legacy
  probes still reuse memory after only SuspendPoint/Vblank, which is not proof
  of GPU retirement. This is a sample ownership correction, not a global change
  to presentation scheduling or proof for all games. The prior seed-fingerprint
  causal claim is withdrawn: actual targets use BGRA/tile27, outside that helper's
  guard; that unvalidated runtime change was removed. Initial-frame symptoms in
  unfenced samples remain unresolved. Release build passes with existing SHEM006.

- **Different textures remain attached to their own draws.** Two separated native
  planes share geometry/shaders but use distinct constant buffers and independent
  64x64 texture allocations. Both normal and reversed submission match **64/64
  cell centers per object**, with **0 differing pixels** between orders. A
  shared-texture control matches the left object but fails all 64 expected right
  cells, changing **652032 pixels**. Real draw traces contain both texture
  addresses without fallback. The same bindings also run on the overlapping,
  rotating D32 cubes: both frozen submission orders are identical; the animated
  scene finishes 600 frames with four distinct captures. Decoded images were
  inspected. The first animated readback is partial; later images show both
  textured cubes. This is source-adapted probe coverage, not full game support.

- **Two overlapping textured objects preserve depth across separate draws.**
  The adapted native scene owns separate constant buffers for both objects and
  both frame slots, shares geometry/texture, and resets depth only once per frame.
  Forward/reversed object submission produces exactly **0 differing pixels**
  with depth enabled, **145274** with depth disabled, and **355811** between
  enabled/disabled forward controls. Decoded four-way captures were visually
  inspected. All four frozen probes and the animated probe finish 600 frames;
  the animated run has four distinct presented captures. This verifies this
  adapted scene, not general game compatibility.

- **CPU updates to a reused linear texture reach sampled images.** The adapted
  plane changes its 64x64 texture contents every 150 frames without replacing
  the allocation or descriptor. Four captured palettes follow **0,1,2,0**;
  all **64/64 cell centers in each capture** match exact expected colors.
  Strict descriptors confirm real texture binding without fallback. The static
  texture negative control is rejected. CPU image sync is enabled only for
  these probes; this does not establish console cache/fence correctness,
  streaming, tiled textures, mipmaps or concurrent texture writes.

- **Texturing and depth now work together in an animated adapted cube.**
  `TextureDepth` submits the same public texture program and a real fullscreen
  depth-reset draw, then renders all six textured cube faces without back-face
  culling. Native execution finishes 600 frames with host exit 0, three
  distinct captures and real texture bindings without fallback. The decoded
  contact sheet shows complete rotating textured faces in the later captures;
  the first readback is blank. This is a single-object, single-mip, linear
  texture scene, not a full 3D game. No general performance claim is inferred.

- **Native adapted texture plane reaches verified image sampling.** Local
  LLVM 18.1.8 AMDGPU tools assemble the pinned public one-texture pixel
  program; the generated SCE ELF has 30 real imports and no unpatched
  relocations. Strict descriptor validation and the draw trace confirm a real
  64x64 RGBA8 2D texture without fallback. The 600-frame scene completes with
  host exit 0. Decoded readbacks show the intended four colored quadrants and
  checker cells; all 64 sampled cell centers exactly match the intended BGRA
  colors. This verifies static linear single-texture sampling in this adapted
  scene, not texture streaming, multiple textures or whole-image correctness.
  The compiler is separate from the pinned LLPC/GFX1013 toolchain Gears needs.

- **Adapted independent cube now exercises persistent D32 depth.** A masked
  color pass with a pixel shader formerly lost its attachment and fell back
  to default raster/depth state. Translation now retains the bound attachment
  when valid depth/stencil work is active, preserves the pixel shader, and
  keeps color writes masked. A real fullscreen triangle resets depth to 1
  before each cube draw; no unconditional emulator clear was added.
  The animated scene finishes its 600-frame source loop with host exit 0;
  decoded readbacks show complete rotating geometry. In a frozen-view control,
  reversing all six face submissions changes **0 pixels with depth enabled**
  and **365718 pixels with depth disabled**. Both cases disable back-face
  culling. The input is explicitly source-adapted, not unchanged upstream or
  a built Gears game. CPU depth-memory clearing remains unresolved. All 1261
  library tests pass, including four masked-target regressions. The final
  incremental CLI build succeeds without warnings; a full dependency rebuild
  still reports the existing `sceAgcInit_0090` catalog warning. Base cube replay
  also completes. See `independent-3d-homebrew.md` for reproduction and limits.

- **Independent cube now reaches real indexed GPU rendering.** Experimental
  non-tessellated triangle linkage combines existing primitive-state and
  semantic-mapping helpers. Ordinary zero-initiator indexed draws now emit
  DRAW_INDEX_2 instead of silently returning null. Text notifications reach
  the emulator log. Four Vulkan swapchain readbacks show changing cube faces;
  the default Windows configuration leaves trails. A second captured run with
  sample-scoped `SHARPEMU_GUEST_IMAGE_CPU_SYNC=1` correctly clears the background;
  its four-frame contact sheet was visually inspected. The corrected-input
  cube completes its 600-frame source loop and exits successfully. This is
  independent basic-3D evidence, not commercial-game compatibility.
  All 1257 library checks pass. Native exit probes preserve both 0 and 7;
  exit 0 succeeds and exit 7 remains a host failure. Guest RAX is preserved
  across the host-stack TLS lookup and initialized from the CPU context.
  Reproduce with `tools/external-homebrew/Invoke-PS5LinkCubeSmoke.ps1`; the
  CPU-write tracking setting is not enabled globally for other games.

- **The independent cube passes AGC initialization and shader creation.**
  Real one-argument init and initialization-dependent defaults accessors now
  use the correct argument registers and guest-isolated state. CLI build has
  zero warnings/errors; all 1248 library checks pass. Corrected-input native
  cube execution reaches import 69, missing `sceAgcLinkShaders`, instead of
  failing at init import 5. Notifications also remain unresolved. No pixels
  have been drawn; the next gap is genuine shader linkage.

- **Wolf basic play is user-confirmed.** The user confirms the title-bar mouse
  fix works, the first level was completed, level transitions work, the saved
  game loaded after a process restart, and audio matches the game. These are
  physical user observations, not automated coverage of every action. Fast
  movement is explicitly accepted for now; no timing correctness is inferred.
  Wolf remains a regression reference. Long-run stability and the historical
  intermittent HUD omission are not declared solved.

- **An independently written GPU cube now builds and reaches main.** The
  unchanged ps5link-sdk cube and native startup link as an SCE ELF with 30
  imports. Native execution stops before rendering on the real `sceAgcInit`
  NID, different from the emulator's existing similarly named export. See
  `independent-3d-homebrew.md` for pins, reproduction and the first two missing
  imports. No successful independent 3D rendering is claimed.

- **Mouse capture now requires a click inside the game.** The user reports the
  GLFW Raw revision works, with one remaining issue: dragging the title bar
  captured immediately on window activation. Activation now leaves Normal mode;
  only a focused client-area left click captures. F8 or focus loss releases and
  activation alone never recaptures. A regression covers title-bar coordinates,
  release and reactivation. All 1246 library tests pass; CLI build has zero
  warnings/errors. The user subsequently confirms the final drag/release revision works.
  The earlier screenshot's centered 640x400 image was caused by
  `--windowed-mouse`; that argument is removed. A decoded 1920x1080 frame fills
  its framebuffer, and the user broadly reports the latest version works.

- **Physical mouse turning and left-button firing are user-confirmed.** The
  explicit `Build-Wolf4SDL.ps1 -EnableMouseBridge` produces a separate mouse
  source ELF; `Invoke-Wolf4SDLUserland.ps1 -Mouse` selects it. A source-only SDL
  adapter calls the actual mouse HLE imports; the Windows backend delivers
  focused motion/buttons. The initial implementation allowed fast motion to
  escape and left the cursor visible; its later ClipCursor/hiding attempt also
  failed the user's physical check. GLFW Raw events now replace those attempts.
  The original turning/firing confirmation applies to run
  `20261008-121910-159` opened the game window and the user confirmed both
  turning and firing. `Start-Wolf4SDLMouse.cmd` provides an interactive launch
  without the diagnostic deadline; revised interactive session uses `mouse-play`.
  Wheel input and other host platforms remain
  unsupported. Mouse state is isolated per guest memory; two new motion/ABI and
  isolation checks pass. Full library suite: 1244/1244, CLI zero warnings/errors.

- **Synchronous HUD stage capture is available, but no scaling fix is proven.**
  The optional source-profile surface slots now capture on the submitting guest
  thread alongside each selected raw display source. This removes the earlier
  asynchronous cross-frame ambiguity for that thread; other threads are not
  frozen. Run `20261008-121104-739` records texture scale mode 0, blend 0,
  blit flags 0x200 and no colorkey/native texture. Its inspected synchronous
  samples show complete numbers, including death/restart. Earlier concurrent
  source/window differences still require reproduction at submission before
  attributing a root cause to scaling. The HUD omission is not fixed.

- **The HUD omission now has captured source-byte evidence.** In run
  `20261008-115543-460`, all twelve comparable raw-source/BMP HUD pairs match,
  including the late frame where most numeric fields are absent. Independent
  decoding therefore reproduces the omission from guest bytes, rather than
  creating it in the emulator's tiled decoder. Sample 4 also has a complete
  numeric HUD in the indexed and converted 640x400 surfaces, but omissions in
  the 1920x1080 SDL window surface; both native display buffers match that window
  sample. These are concurrent observations, not an atomic cross-stage snapshot.
  Follow-up run `20261008-120025-225` records seven exact-metadata matching
  source/BMP pairs; sample 4 also retains the numbers in the software texture
  while the window sample omits them. The next target is SDL software scaling
  and its blit state before display tiling. A bounded opt-in capture saves at most sixteen sources of at
  most 16 MiB each; new captures record exact flip/buffer/geometry metadata.
  Earlier empty keyboard replays still permitted physical keyboard input;
  they must not be described as isolated no-input runs. Library checks remain
  1242/1242 and the CLI builds with zero warnings/errors. The HUD bug is not fixed.

- **Direct Wolf4SDL level startup now receives its complete arguments.** The
  explicit runner's `-StartLevel 0` passes the public `--tedlevel 0 --nowait
  --normal` options along with an isolated configuration directory. Previously
  the dispatcher silently kept only two configured arguments. An opt-in bounded
  source-profile argv extension now passes all arguments and a NULL terminator;
  ordinary title startup keeps its existing layout. The rebuilt homebrew entry
  reads the inline array. Six argument-frame checks and the full 1242/1242 suite
  pass; CLI build has zero warnings/errors (the upstream C++ source build still
  emits existing pragma-pack warnings). Run `20261008-110227-638` shows direct
  level startup with a complete HUD. The actual default forward key is **W**,
  not Up; `20261008-110558-134` shows approach and a partially opened door in
  frame 20. Leaving the room is still not verified. Most importantly, no-input
  run `20261008-110924-930` reproduces the HUD discrepancy: frame 13 / buffer 0 /
  flip 1376 lacks numbers, while frame 14 / buffer 1 / flip 1427 has the full HUD.
  An empty replay leaves physical input enabled. Both are decoded guest buffers; this does not establish the root cause or a
  Vulkan presentation fix. Odd capture spacing now samples both buffers.
  The newer source-byte checkpoint above narrows the next investigation.

- **Native main-thread flip waits now block until an event really exists.**
  `sceKernelWaitEqueue(NULL timeout)` previously returned empty success when
  no schedulable guest-thread handle existed. Public PS5 SDL then submitted
  more frames without awaiting presentation; an earlier trace reached repeated
  flip `RESOURCE_BUSY` at argument 916. The host fallback now waits on the
  queue monitor, reserves events atomically, and wakes for display events or
  deletion. Three host-wait regressions pass; full suite 1236/1236 (including
  the longer bounded replay check). Native `-TiledCpu -HostWait` and
  `-Retry present` both pass actual three-frame pixels/status/event assertions.
  Run `20261008-055847-737` reaches the level without observed flip errors;
  its HUD still has intermittent omissions, so that issue is not claimed fixed.
  Gameplay replay/launch can now be explicitly bounded up to 120 seconds;
  the existing 60-second smoke scripts keep their original deadlines.

- **Wolf4SDL produces real PCM audio alongside a correct presented HUD.**
  Run `20261008-054532-611` opened the Windows `winmm` backend at 48 kHz,
  stereo s16. Its opt-in diagnostic tap contains 56.96 seconds / 2,734,080
  frames, peak 0.7366, RMS 0.0751, 4,964,978 nonzero samples and no clipped
  samples. `av-check/20261008-054532-491/audio-stereo-s16le.analysis.json`
  records the hash/measurements, with a WAV for later listening. This proves
  non-silent game submissions, not audible speaker output or audio fidelity.
  Its tenth actual Vulkan readback (`presented-level.png`) shows the complete
  HUD, health 100 and ammo 7. An earlier intermittent HUD omission is still
  not fully explained. The audio output trace now uses the selected backend
  name instead of incorrectly labeling every non-NULL stream `coreaudio`.

- **Wolf4SDL can write a save and load it in a fresh process.** The explicit
  source profile now supports an isolated writable `/temp0` configuration
  directory via `Invoke-Wolf4SDLUserland.ps1 -ConfigSubdirectory <name>`.
  Run `20261008-052937-455` wrote slot `sol1`, 23,468 bytes, map 0, health 100,
  ammo 8. Run `20261008-053753-026` read the entire save through position 23,468
  and returned to the level; frame 25 shows the loaded scene and firing pose.
  The stronger roundtrip also passes: run `20261008-054252-396` saved `sol2`
  with ammo 7 after firing/turning; fresh run `20261008-054353-483` read the
  full file and frame 15 shows the changed blue-wall view and ammo 7.
  `Invoke-Wolf4SDLSaveLoadSmoke.ps1` repeats both processes with a unique folder.
  `fwrite` now rejects NULL streams and overflowing requests, supports bounded
  owned-libc-heap input, and no longer prints a failed NULL save to stdout.
  Denied `fopen` paths return NULL instead of throwing. ASCII `isprint` supports
  save-name entry. Invalid `fread` sizes and write-only streams return failure
  before attempting host I/O. Full library checks pass 1232/1232; CLI build has no warnings
  or errors (`artifacts/wolf-save-full-tests.log`, `wolf-save-build.log`).

- **CPU tiled decoding is faster with identical benchmark pixels.** On this
  i5-12400F, a fixed seeded 1920x1080 frame measured 14.553 -> 8.278 ms median
  across 12 warm samples, and 17,207,432 -> 8,302,299 allocated bytes/frame.
  Both output hashes are `2E095BE17D7AA1CA`. Axis tables avoid repeated bit
  calculations; scratch pooling is limited to <=16 MiB requests and only the
  required read span is used. Returned frames retain independent pixel storage.
  Full library checks pass 1226/1226; native tiled presentation still passes.
  Evidence: `artifacts/wolf-tiled-performance-before.log`,
  `artifacts/wolf-tiled-performance-after.log`, `artifacts/wolf-tiled-speed-native.log`.
  These decoder timings are not a measured game-FPS improvement.

- **Wolf4SDL's level is confirmed in an actual Vulkan swapchain readback.**
  `Invoke-Wolf4SDLLevelSmoke.ps1` provides the bounded one-command scenario.
  Run `20261008-051524-780` preserves CPU snapshots and nine actual presented
  samples in `level-smoke/20261008-051524-692`; `presented-level.png` shows
  the level, weapon and ammo 7. CPU presentation now respects the existing
  periodic diagnostic readback settings. Tiled native pixel/lifecycle checks
  still pass after this change. No physical-control or commercial-game claim.

- **First Wolf4SDL shareware level, movement/turn and one shot are visually verified
  with opt-in test input.** Run `20261008-050746-265/frames` shows ammo 8 in
  frame 1, and ammo 7 plus the firing pose in frame 22 after left Ctrl. Earlier
  run `20261008-050603-353` demonstrates the changed view after arrows.
  These are actual captured game frames; physical keyboard and audible audio
  still need verification. The next engineering check targets patterned tiled
  display output and flip completion, followed by a reproducible level launcher.

- **Tiled CPU display has an independent native presentation oracle.**
  `Test-TouchePX5-FlipLifecycle.ps1 -Mode cpu -TiledCpu` and `-Retry present`
  both pass. Three shifted RGB stripe frames cross x=512/y=128 boundaries;
  48 actual swapchain pixels per frame include corners, RGB and opaque alpha.
  Guest pending/status/event assertions retain delayed completion and retry.
  Evidence: `artifacts/wolf-tiled-native-flip.log`,
  `artifacts/wolf-tiled-native-retry.log` and their fixture `verification.json`.

- **Wolf4SDL reaches a readable sign-on, help page and main menu.** The native
  exception trampoline encoded `cmpxchg [rcx],r10` instead of its lock pointer
  `[r9]`; both paths now set REX.B. An actual emitted-handler regression checks
  both encodings and executes recursive entry with independent exception storage.
  The recovered fault then identified skipped C++ constructors. The explicit
  source-build entry now runs its init array before main; the unchanged payload
  remains a separate unsupported target. New source ELF SHA256:
  `703FA9B0B305FDEFD6799511D4296653F9D9512DC3517245CE18AB0FE55BDE6D`.
  PS5 CPU display tiling is decoded using the pinned open SDL backend; the old
  red stripes were a linear interpretation of tiled memory. Added bounded PS5
  keyboard state, foreground-user lookup, atan/tan/sinf and owned strdup.
  Help evidence: `artifacts/external-homebrew/wolf4sdl-userland/runs/20261008-050035-072/frames/`.
  Menu evidence: `artifacts/external-homebrew/wolf4sdl-userland/runs/20261008-050206-862/frames/`.
  The latter shows the Options menu with its quit confirmation over it; it is
  not a loaded level. Replay is opt-in HLE test input and sends no Windows keys.
  Physical keyboard, audible sound and gameplay remain unverified. Full library
  checks passed 1225/1225 in `artifacts/wolf-sign-on-full-regressions.log`.
  Next: select New Game, capture a level, then verify movement and shooting.

- **The source profile now loads the first shareware level and responds to
  movement/turn replay.** Run `20261008-050603-353` preserves 28 dated frames,
  runtime/data/ELF hashes and the full input sequence. Frame 16 (flip 600) shows
  the starting door and HUD; frame 28 (flip 920) shows a changed position/view
  facing the blue wall after the arrow-key sequence. Health is 100 and ammo 8.
  This is a limited automated level observation, not physical-control, audible
  audio, completed-level or broad compatibility verification. Shooting is next.

- **Wolf4SDL source profile now passes controller parsing, reads game data and starts the audio worker.**
  Added the actually requested C string/number and elementary double math exports;
  file reads, semaphore storage, pthread output and audio buffers now accept bounded
  live libc allocations. The runner explicitly selects PS5 for ambiguous open-SDK
  ELF metadata without changing the guest file. Latest diagnostic run:
  `artifacts/external-homebrew/wolf4sdl-userland/runs/20261008-042420-247/report.json`.
  Main execution repeatedly stops inside the managed exception-handler trampoline
  at offsets 0x285/0x26B after malloc returns into C++ operator new. Audio continues.
  The last observed game caller is `US_SetScanNames`; this is not a verified game-data
  parsing fault or a semaphore deadlock. Next: inspect the original exception and
  trampoline lock/return path before changing its behavior. No menu/gameplay verified.
  Focused startup checks passed 43/43; full library suite passed 1193/1193 in
  `artifacts/wolf-libs-regressions.log`. CLI builds with zero warnings/errors.
  The first captured frame from the prior 20261005-013304-765 run contains red stripes,
  not a menu; its linear-read dump of tiling_mode=0 does not prove rendering layout.

- **Explicit Wolf4SDL PS5 source build reaches VideoOut and controller mapping parsing.**
  The adapted userland entry preserves the pinned engine and PS5 SDL backend;
  it does not make the unchanged payload compatible. Native named imports,
  empty guest getenv, bounded libc-heap output/descriptor access and low-address
  executable returns are repaired. Before the last fix, a valid 0x570F3F return
  was rejected by a 12 GiB floor and a host trampoline was selected from the
  stack. Afterward the actual engine reaches controller parsing and missing
  `strtok`. Latest bounded run:
  `artifacts/external-homebrew/wolf4sdl-userland/runs/20261005-010933-782/report.json`.
  The log records first 1920x1080 presentation; no actual menu or gameplay was
  visually verified, and the run still terminates at its deadline. Focused
  checks passed **288/288**, payload ABI checks **2/2** in
  `artifacts/payload-bootstrap/20261005-010928-499/report.json`; final emulator
  Release build passed with zero warnings/errors. Open dependency extraction
  and source rebuild reproduced the same ELF SHA256. See
  `docs/wolf4sdl-boot-investigation.md` for provenance and exact limitations.
  Next routine Sol work is the actual `strtok` blocker and then a captured menu.

- **Wolf4SDL now passes its first resolver calls and reaches syscall 0x289**.
  The unmodified PS5 shareware ELF no longer times out at the old boundary:
  unsupported kernel introspection unwinds to the host with a concrete error.
  No game graphics have been reached. Evidence with all 248 runtime file hashes:
  `artifacts/external-homebrew/wolf4sdl-v1.0/runs/20261005-003038-642/report.json`.
  Release build passed (zero warnings/errors), two native payload ABI cases
  passed (`artifacts/payload-bootstrap/20261005-003033-743/report.json`), the
  dynamic-module lifecycle/public-lookup check passed
  (`artifacts/wolf-adapter-dynamic-module.log`), and 111 focused loader/module
  unit tests passed (`artifacts/wolf-adapter-unit-tests.log`). The full GPU
  aggregate was not repeated for this startup-only change; its result below
  remains historical. This unchanged-release boundary is retained separately
  from the explicit source profile above.

- **Byte-unaligned image DMA repair is verified by 100/100 native GPU cases**:
  `artifacts/native-gpu/20261004-100952-502/report.json`, clean probe commit
  `df68e3a`. General engineering passed **27/27 cases / 1,298 unit tests**:
  `artifacts/engineering/20261004-095709-889/report.json`, clean runtime code
  `a9f0b90`. The intervening commit changed only the sampled-frame oracle and
  documentation; no runtime rebuild preceded that checkpoint comparison. All
  **248 runtime entries** matched between the general post-run manifest, GPU
  aggregate and build at that checkpoint.
  Independent final comparison: `artifacts/byte-patch-final-verification.json`.
  This includes the original 76 native scenarios and all 24 new R32 byte-span
  variants, preserving the strict successful capture count and pixel oracles.
  The first aggregate's actual OutOfDate retry was a test-oracle issue; its
  retained failure and both positive/negative controls are recorded below.
  The Astra implementation and Sol validation stages are complete. Next focused
  coverage can exercise unaligned RGBA8 consumers and disjoint byte writes in
  one texel; performance and failure-path injection remain separate work.
- General validation of the byte repair passed **27/27 cases / 1,298 tests**
  on clean `a9f0b90`:
  `artifacts/engineering/20261004-095709-889/report.json`.
  The first 100-case GPU aggregate stopped after 53 passes because an existing
  sampled-frame oracle counted a real QueuePresent OutOfDate retry as another
  displayed image: `artifacts/native-gpu/20261004-095939-366/report.json`.
  The preserved log shows one adjacent retry with completed recreation, then
  three distinct image sources and four swapchain captures. The oracle now
  permits adjacent repeats only with intervening OutOfDate/recreation markers;
  successful capture counts, unique source count and pixel checks are preserved.
  The actual retry trace is accepted and a repeat without OutOfDate is rejected:
  `artifacts/byte-patch-retry-oracle-control.json`. Native graphics and cross-queue
  sampled controls pass on unchanged runtime (`artifacts/byte-patch-sampled-control.log`
  and `artifacts/byte-patch-sampled-acb-control.log`). A new full aggregate must
  finish before this byte repair is declared generally verified; the subsequent
  100-case passing aggregate above now supplies that validation.
- **Byte-unaligned image DMA repair is implemented; 24 targeted native variants
  pass.** Initialized linear single-mip/depth-1 RGBA8/R32 images now preserve
  untouched edge bytes by reading only affected edge texels from the GPU before
  merging exact managed writes. Whole texels retain the existing batched upload.
  Edge copies use image/host barriers and a bounded submission-fence wait; their
  buffers remain owned by submission retirement on failures/timeouts. Failed
  writes are retained and no partial mirror becomes a complete CPU snapshot.
  Six native R32 cases cover middle/head/tail bytes, crossing a texel boundary,
  a six-byte span, and crossing a row; UINT/SINT x graphics/ACB gives 24 cases.
  They seed both GPU words explicitly (the earlier probe assumed an unseeded
  neighbor inherited CPU 100), verify independent reads plus CPU/output guards,
  and count bounded edge readbacks. `artifacts/byte-targeted-passed.json` and
  `artifacts/byte-*-signed-*-acb-*.log` record the targeted runs; Release build
  passed without warnings/errors in `artifacts/byte-patch-build.log`.
  The full aggregate now contains 100 cases and passed on the frozen runtime
  recorded above. The synchronous
  edge path favors correctness; performance, fault injection, arbitrary tiled/
  mip layouts and native unaligned RGBA8 consumers remain separate validation.
  The Astra implementation stage and subsequent Sol aggregate validation are complete.
- **New confirmed compatibility gap: byte-unaligned image DMA destroys GPU-only
  bytes in an affected texel.** The opt-in compute probe `-UnalignedByte` seeds
  GPU texel 0 with `0x11223344` while CPU backing is 99, then fills exactly byte 1
  with `0x7b`. The required GPU result is `0x11227b44`; independent GPU readback
  instead returned the stale CPU word `0x00007b63` (explicit guest failure 85).
  CPU neighboring words and output guards were checked before that failure.
  All four UINT/SINT x graphics/ACB variants reproduced the same replacement;
  the aligned graphics and ACB controls still passed. Evidence and unchanged
  248-file runtime manifest: `artifacts/r32-byte-before/report.json` and its
  four preserved guest/runner log pairs.
  Before the repair above, the managed-write drain admitted only four-byte-aligned spans to its
  bounded upload path; this byte span therefore falls into the full stale-CPU
  upload. Merely rounding the span to a texel would still overwrite GPU-only
  neighboring bytes. This retained evidence describes the pre-repair runtime;
  the repaired implementation and strengthened probe are described above.
- **3/3 real-window recovery trials passed with human window control**:
  CPU frames, direct GPU image flips and ordered immutable GPU captures.
  Report and preserved guest/runner/pixel evidence:
  `artifacts/window-recovery/20261004-manual/report.json`.
  Each trial observed native window-unavailable deferral, pending/no-event
  guest assertions during minimization, swapchain recreation on restoration,
  successful completion and three independently verified output colors.
  Ordered capture also survived a later write to its live source image.
  Probe source was clean `bdf082d`; all 248 runtime files still exactly match
  the 76-case aggregate on runtime code `dfd37f2`. No rebuild was performed.
  Scope: one minimize/restore cycle per mode on this Windows/NVIDIA machine.
  Physical scanout timing, repeated cycles, overlays, Metal and commercial
  titles remain separate validation. Computer-use automation is still unavailable;
  the user performed window actions in these successful trials.
- An opt-in real-window recovery probe is prepared:
  `Test-TouchePX5-FlipLifecycle.ps1 -Mode cpu -WindowRecovery` (also direct/ordered).
  It pauses after frame 1, requires frame 2 to remain pending without an event,
  then checks resumed presentation, swapchain recreation and all three colors.
  It requires interactive window control and is not in the unattended aggregate.
  The current computer-use helper enumerated the native window, but its click
  failed with `coordinate input geometry is unavailable`; activation/capture
  failed with `FrameArrived timed out: timed out waiting on channel`. Recovery
  attempts did not minimize the window. Both trials correctly rejected early
  frame-2 completion (guest 105). These automation failures preceded the passing
  human-controlled trials above. First failed guest evidence is preserved in
  `artifacts/flip-window-cpu-before.log`; the shared smoke log is overwritten
  on subsequent runs and is not retained as failed-trial evidence.
  A final deliberately unminimized trial also rejected guest 105:
  `artifacts/flip-window-negative-control.log`. This is negative-control evidence,
  not a passing recovery test.
  Ordinary CPU/direct/ordered controls passed after adding the opt-in branch:
  `artifacts/flip-window-control-cpu.log`, `artifacts/flip-window-control-direct.log`,
  `artifacts/flip-window-control-ordered.log`. No emulator rebuild was required.
- Flip lifecycle implementation is verified on clean code commit `dfd37f2`:
  **76/76 native GPU cases passed** in
  `artifacts/native-gpu/20261004-090013-898/report.json`, and **27/27 general
  engineering cases**, including **1,298 unit tests**, passed in
  `artifacts/engineering/20261004-002458-772/report.json`.
  The general post-run manifest, GPU manifest and final runtime match exactly
  across all **248 entries**. Sources and binaries were frozen throughout the
  native aggregate. General fixtures are at `8f6e4b7`; their existing unrelated
  local changes were preserved.
  Eight new native cases independently verify pending/status/event ordering,
  direct CPU/GPU flips, a six-frame burst, immutable ordered captures and both
  acquire/present recreation branches with correct output colors. The retry
  oracle exposed stale readback state after recreation; resetting that state
  fixed the false black capture. See `docs/flip-lifecycle-plan.md` for boundaries.
  The user has switched back to Sol Medium; the requested Astra stage is complete.
- Complete **68/68 native GPU regression passed** on clean source `cfe6848`:
  `artifacts/native-gpu/20261003-030501-778/report.json`.
  All 248 runtime entries remained unchanged and exactly match the post-run
  manifest from the **27/27 general engineering cases** below, including all
  **1,291 unit tests**. Release build has zero warnings/errors.
  This validates the current R32 partial-DMA repair, cross-queue image-atomic
  consumer, masked and wider/paired LDS operations, and every prior native
  graphics/coherence scenario on the same binaries.
  This is the baseline before the flip-completion changes above.
- Fixed **partial DMA replacing GPU-only R32 texels**. The former RGBA8-only
  dirty-span gate uploaded the entire stale R32 CPU image after a one-word DMA,
  replacing GPU value 7 with CPU value 99. The native preserved-word oracle
  rejected guest 80 after two dispatches:
  `artifacts/compute-r32-partial-before-evidence.json` and
  `artifacts/compute-r32-partial-before-guest.log`.
  Both partial-upload gates now accept linear single-mip/depth-1 R32_UINT and
  R32_SINT with the same aligned span bounds as RGBA8. The Release rebuild and
  **27/27 general engineering cases**, including **1,291 unit tests**, pass:
  `artifacts/engineering/20261003-030021-325/report.json`.
  All four targeted native checks pass:
  `artifacts/compute-r32-partial-after.log`,
  `artifacts/compute-r32-partial-after-queue.log`,
  `artifacts/compute-r32-partial-signed-after.log`,
  `artifacts/compute-r32-partial-signed-after-queue.log`.
  They verify GPU-only 7 beside updated 123 through separate GPU reads and
  preserve CPU neighbors and output guards. The cross-queue image consumer
  also passes on this runtime: `artifacts/compute-image-atomic-cross-maintained.log`.
  These add native cases 64-68 and pass in the complete aggregate above.
  The 63-case report below predates this runtime fix.
- A targeted **cross-queue image atomic consumer** passes without another
  runtime change: `artifacts/compute-image-atomic-cross-targeted.log`.
  Graphics consumers suspend before their ACB producers exist, then resume
  through real labels and independently read sums 13/23. Returned atomic old
  values, intentionally stale CPU image backing and output guards are checked.
  Reading the wrong texel is rejected at guest 68 after three GPU dispatches:
  `artifacts/compute-image-atomic-cross-negative-evidence.json`.
- Complete **63/63 native GPU regression passed** on clean source `1b6fb26`:
  `artifacts/native-gpu/20261003-024646-656/report.json`.
  All 248 runtime entries stayed unchanged and exactly match the post-run
  manifest of the earlier 27-case general report below. The later R32 partial
  runtime repair supersedes these binaries and needs its own aggregate.
- Complete **61/61 native GPU regression passed** on clean source `f2cc0b5`,
  with all 248 runtime entries unchanged throughout:
  `artifacts/native-gpu/20261003-022638-535/report.json`.
  The subsequent Release rebuild and general engineering run also passed
  **27/27 cases**, including **1,291 unit tests**:
  `artifacts/engineering/20261003-024158-962/report.json`.
  Its post-run runtime manifest is saved alongside that report. This rebuild
  can change binary metadata; the preceding GPU report is not used to claim
  identical files without a manifest comparison.
- Added targeted **paired/scaled-offset LDS store checks** without a runtime
  change: `artifacts/compute-lds-paired-native.log` and
  `artifacts/compute-lds-paired-native-queue.log`. Each runs eight kernels with
  64 invocations, checks inactive/lane-0/32/63 masks, independently reads
  offsets 4/12 and 256/768, and verifies untouched LDS locations and output
  guards. Replacing the scaled instruction with the ordinary one is rejected
  at guest 155 after six GPU dispatches:
  `artifacts/compute-lds-paired-negative-evidence.json`.
  These add native cases 62/63 and pass in the aggregate above.
- Added **two targeted wider LDS store cases**, passing without another runtime
  change: `artifacts/compute-lds-wide-native.log` and
  `artifacts/compute-lds-wide-native-queue.log`.
  Each runs twelve 64-invocation kernels for 64/96/128-bit writes under
  inactive/lane-0/32/63 masks. Independent dword reads verify written values,
  untouched components and a neighboring fifth LDS word. Per-record padding
  and trailing output guards are also checked. A wrong-width instruction
  (96 -> 64 bits) is correctly rejected with guest 155 after six real GPU
  dispatches: `artifacts/compute-lds-wide-negative.log`.
  Atomic modes now require exact submissions exclusively on their requested
  graphics/ACB queue; prior image atomic add passes this stricter check:
  `artifacts/compute-lds-wide-submit-regression.log`.
  These add native cases 60/61 and pass in the 61-case aggregate above.
  Paired/scaled-offset LDS stores have independent checks above; GDS remains
  separate work.
- Complete **59/59 native GPU regression passed** on clean source `450b8f2`,
  with all 248 runtime entries unchanged throughout:
  `artifacts/native-gpu/20261003-021050-830/report.json`.
  This includes the native LDS cases and masked-store runtime repair below.
  All 1,291 unit tests pass on this Release runtime.
- Fixed **inactive-lane LDS overwrites**: inactive lanes now issue no shared
  memory access instead of reading the old value and writing it back over an
  active lane's store. Native evidence before repair returned old value 48
  instead of the expected 0..7 cycle values, rejecting guest 132 after one
  dispatch. Evidence is preserved in
  `artifacts/compute-lds-masked-write-before-evidence.json` and
  `artifacts/compute-lds-masked-write-before-guest.log`.
  The exact experiment passes after changing only the common Vulkan LDS store:
  `artifacts/compute-lds-masked-write-after.log`.
  New graphics and ACB cases pass:
  `artifacts/compute-lds-native.log`, `artifacts/compute-lds-native-queue.log`.
  Each requires nineteen GPU dispatches: eleven scalar INC/DEC boundaries,
  four 64-invocation shared cycles (including the reproduced masked overwrite)
  and four independent DS writes for inactive/lane-0/32/63 masks. Returned
  values, final shared contents and output guards are verified. No CPU
  replacements are allowed. Clean build has no warnings/errors and all
  **1,291 unit tests pass**:
  `artifacts/compute-lds-masked-build.log`, `artifacts/compute-lds-masked-tests.log`.
  These add native cases 58/59 and pass in the complete aggregate above.
  The earlier 57/57 report predates this runtime repair. GDS and broad
  scheduling/performance remain outside these word-level checks; the newer
  width-specific cases above cover their stated wider stores.
- Complete **57/57 native GPU regression passed** on clean source `67d2323`,
  with all 248 runtime entries unchanged throughout:
  `artifacts/native-gpu/20261003-015244-572/report.json`.
  This covers all six bounded-atomic scenarios below and every earlier graphics,
  queue and image-coherence check. The Release runtime passes 1,291 unit tests.
- Fixed **bounded Vulkan atomic INC/DEC**: the DATA limit is now honored via an
  atomic load and compare/exchange retry, returning the old value from the
  successful attempt. The shared buffer/image/LDS emitter uses unsigned limit
  comparisons even for signed image storage. Before repair, native buffer and
  image counters reached 8 rather than wrapping to 0 at limit 7; the saved
  failures below are preserved. Six targeted graphics/ACB scenarios now pass
  for buffers, R32_UINT and R32_SINT images:
  `artifacts/compute-clamp-{image,buffer,signed}-after.log`,
  `artifacts/compute-clamp-{image,buffer,signed}-queue.log`.
  Each checks eleven boundary cases and two 64-workgroup contended cycles,
  including returned-value histograms, separate GPU readback and output guards.
  Buffer writeback preserves three neighboring words; image CPU backing stays
  intentionally stale. Each requires 39 actual Vulkan dispatches.
  Clean Release build has no warnings/errors; all **1,291 unit tests pass**,
  including eight pipeline cases for buffer/LDS/uint/sint INC/DEC:
  `artifacts/compute-clamp-build.log`, `artifacts/compute-clamp-tests.log`.
  Earlier atomic add and GPU-produced indirect ACB dimensions still pass:
  `artifacts/compute-clamp-add-regression.log`,
  `artifacts/compute-clamp-indirect-queue-regression.log`.
  These add native cases 52–57 and pass in the complete aggregate above.
  The complete 51-case report below predates this runtime repair; arbitrary
  scheduling and performance are not established by these targeted checks.
  The newer native LDS cases above cover their stated word operations.
- Complete **51/51 native GPU regression passed** on clean source `cf67ade`,
  with all 248 runtime entries unchanged throughout:
  `artifacts/native-gpu/20261003-013510-019/report.json`.
  Both image-atomic add variants below are included. Before the current repair,
  bounded atomic INC/DEC were incorrect: native image and buffer fixtures each returned guest 100
  on their first limit boundary (6 -> 7 -> expected 0, actual 8):
  `artifacts/compute-image-clamp-before.log`,
  `artifacts/compute-buffer-clamp-before.log`.
- Added **two targeted storage-image atomic cases**, passing without a runtime
  change: `artifacts/compute-image-atomic-direct.log` and
  `artifacts/compute-image-atomic-queue.log`. A GPU R32_UINT image is seeded to 7,
  two workgroups add 3 then 5 to the same texel and return unique old values;
  separate GPU reads verify sums 13/23. Output guards and intentionally stale
  CPU image backing are unchanged. Graphics and sequential ACB owner-7 forms
  each require five actual GPU dispatches and reject CPU replacements.
  Clearing only GLC correctly fails the old-value check with guest 66:
  `artifacts/compute-image-atomic-negative.log`.
  These add native cases 50/51 and pass in the complete aggregate above.
  Other atomics, layouts and cross-queue image-atomic consumers are not
  established by these targeted checks. The old compute fixture also passes:
  `artifacts/compute-image-atomic-basic-regression.log`.
- Complete **49/49 native GPU regression passed** on clean source `93259af`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20261003-011856-103/report.json`.
  This covers both compute storage-write variants and the distinct-buffer
  fixture repair below, alongside all prior graphics and queue checks.
  The Release runtime still passes all 1,283 unit tests. Storage-image atomics
  and actual window minimize/restore remain separate, unverified work.
- The first 49-case aggregate on `19cbc90` failed in preserved-region sampling
  across queues: `artifacts/native-gpu/20261003-010425-117/report.json`.
  This was a fixture lifetime bug, not a new CPU/GPU color-source failure:
  `window-unavailable` delayed the red flip until after the fixture overwrote
  that same output image blue. Captures were blue/blue/red and the first color
  oracle correctly rejected them. Sample fixtures now register four distinct
  buffers (warmup plus three results) and never redraw a queued result image.
  The runner requires three distinct nonzero presented image addresses as well
  as all original pixel, queue and DMA-upload checks. Preserved sampling,
  both storage-write variants and the prior cross-queue sample pass:
  `artifacts/dma-sample-distinct-buffers.log`,
  `artifacts/dma-distinct-buffers-store.log`,
  `artifacts/dma-distinct-buffers-store-cross.log`,
  `artifacts/dma-distinct-buffers-sample-cross.log`.
  This fixes fixture buffer ownership; it does not establish end-to-end
  minimize/restore handling or change the emulator's flip-status semantics.
- Added single-pixel storage-image write/read coverage without a runtime change:
  `-DmaImageComputeStore` writes yellow at (96,48), then returns that pixel plus
  the existing updated/preserved samples into the guarded output buffer. Three
  GPU dispatches require four storage-image bindings each; readback checks 36
  RGBA values and 12 guard words total. The prior sampled frames remain exact.
  Direct and cross-queue forms pass:
  `artifacts/dma-compute-image-store.log`,
  `artifacts/dma-compute-image-store-cross-queue.log`.
  Writing row 49 while checking row 48 is rejected by the store readback check
  (guest 96 after one real GPU dispatch):
  `artifacts/dma-compute-image-store-negative.log`,
  `artifacts/compute-image-store-wrong-pixel/triangle.log`.
  The original read-only variant also passes after the shared-fixture changes:
  `artifacts/dma-compute-image-store-read-regression.log`.
  These add cases 48/49 and pass in the complete aggregate above,
  with unchanged runtime binaries. This establishes one pixel's store
  and read-after-write in these dispatches, not storage atomics, other layouts,
  all untouched source pixels or unobserved native CPU stores.
- Complete **47/47 native GPU regression passed** on clean source `24435a4`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20261003-004902-315/report.json`.
  This includes both new compute-image readback scenarios. The Release runtime
  remains the one from the ordered image-sync fix; its 1,283 unit tests pass.
- Added GPU compute-image readback coverage without a runtime change:
  `-DmaImageComputeRead` reads updated (80,45) and preserved (80,112) texels with
  integer image fetches, returning RGBA float values into a separate buffer.
  The guest checks red/red, blue/red and green/red after ordered completion,
  plus eight unchanged guard words on every pass. CPU backing in the preserved
  region remains gray. Three actual Vulkan dispatches, observed global writes,
  no CPU semantic replacement, ordered image uploads and all sampled-frame
  pixel checks are required. Both direct and cross-queue forms pass:
  `artifacts/dma-compute-image-read.log`,
  `artifacts/dma-compute-image-read-cross-queue.log`.
  A separate wrong-row negative fixture is rejected at the second compute
  readback (guest code 94 after two real GPU dispatches):
  `artifacts/dma-compute-image-read-negative.log`,
  `artifacts/compute-image-read-wrong-row/triangle.log`.
  These add native cases 46/47 and are covered by the complete aggregate above;
  runtime binaries are unchanged from the preceding 45-case result.
  Storage-image writes/atomics, other layouts and unobserved native stores are
  not established by these point reads; the newer store cases above cover only
  their stated pixel write.
- Complete **45/45 native GPU regression passed** on clean source `1092ac8`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20261003-000241-248/report.json`.
  This verifies both complete-snapshot selection and ordered partial-image
  publication fixes alongside the existing compute, texture/mip, depth,
  blending, indexed draw and queue scenarios. Commercial games, unobserved
  native CPU stores, arbitrary layouts and compute-image reads remain outside
  that aggregate; the two newer targeted cases above cover the stated reads.
- The first 45-case aggregate on `608ee75` exposed an intermittent dependent
  blend failure: `artifacts/native-gpu/20261002-235139-018/report.json`.
  Its second frame contained a blue prefix without the expected purple blend;
  an immediate rerun passed. Partial DMA had no queued image mirror, so its
  dirty CPU spans could upload only at the next frame, after the dependent draw
  and overwrite that draw. Vulkan now drains dirty image stores immediately
  after each ordered PM4 side effect, before later queued work. The dirty-epoch
  fast path still skips unchanged stores. Dependent draw/sample probes also
  require each partial upload to share the DMA action's work sequence, before
  the action completes; pixel results alone cannot hide delayed uploads.
  Direct blending, cross-queue blending and both preserved-region sampling
  variants pass on the repaired runtime:
  `artifacts/dma-ordered-image-sync-draw.log`,
  `artifacts/dma-ordered-image-sync-draw-cross.log`,
  `artifacts/dma-ordered-image-sync-sample.log`,
  `artifacts/dma-ordered-image-sync-sample-cross.log`.
  Clean build and all 1,283 unit tests pass:
  `artifacts/dma-ordered-image-sync-build.log`,
  `artifacts/dma-ordered-image-sync-tests.log`.
  The complete aggregate above passes. Metal and unobserved native writes
  have not been changed or validated by this repair.
- Fixed historical CPU snapshot selection after partial DMA: an uploaded
  generation alone does not establish full CPU contents. The Vulkan image now
  records complete CPU snapshots explicitly; partial uploads and GPU writes
  invalidate that state, full CPU uploads establish it. Existing complete CPU
  historical snapshots and newer observed write refreshes retain their paths.
  The new `-DmaImageSamplePreserved` samples the lower GPU-only red triangle
  after copying green into the upper half. CPU backing below remains gray,
  independently detecting stale CPU selection and wrong sampling coordinates.
  Before repair the final triangle was gray:
  `artifacts/dma-sample-preserved-before.log`,
  `artifacts/dma-sample-preserved-before-guest.log`.
  All three frames now pass directly and across queues:
  `artifacts/dma-sample-preserved-after.log`,
  `artifacts/dma-sample-preserved-cross-queue.log`.
  These add native cases 44/45. Clean Release build has no warnings/errors;
  all **1,283 unit tests pass**, including incomplete-mirror policy regressions:
  `artifacts/dma-sample-preserved-build.log`,
  `artifacts/dma-sample-preserved-tests.log`.
  The complete 45-case aggregate above covers these cases; the 43-case result
  below predates this fix. Scope remains linear RGBA8 at the stated coordinates, not arbitrary
  subresources, unobserved native CPU stores or compute-image reads.
- Complete **43/43 native GPU regression passed** on clean source `926c1e2`,
  with runtime files unchanged throughout:
  `artifacts/native-gpu/20260929-124502-959/report.json`.
  This includes all partial-row, dependent blending and texture-consumer cases.
- Fixed a sampled-render-target overwrite when native write-fault tracking is
  disabled but managed write generations remain available. The legacy nonzero
  CPU-texel heuristic now applies only to untracked snapshots (generation -1),
  so it cannot promote a tracked GPU-only render target into a stale CPU source.
  `-DmaImageSample` renders a red source triangle while its CPU backing remains
  gray, samples that source into a distinct output, then samples blue/red after
  prefix DMA fill/copy. Before the fix the first output failed color checks:
  `artifacts/dma-sample-before.log`, `artifacts/dma-sample-before-guest.log`.
  All three captures now pass: `artifacts/dma-sample-native.log`. The cross-queue
  form passes too: `artifacts/dma-sample-cross-queue.log`. It keeps DMA-only ACB
  producers and executes texture draws in the resumed graphics consumers.
  Cases 42/43 are added. Clean build and all 1,279 unit tests pass:
  `artifacts/dma-sample-build.log`, `artifacts/dma-sample-tests.log`.
  Ordinary texturing and cross-queue blending also pass. Native CPU writes with
  fault tracking disabled remain unobserved; this does not establish their
  coherence, arbitrary sampling coordinates/layouts or compute-image reads.
- Cross-queue dependent blending now passes as well, without a runtime change:
  `artifacts/dma-draw-cross-queue.log`. `-DmaImageDraw -DmaImageCrossQueue`
  submits the waiting graphics consumer first, then ACB owner 7 performs the
  prefix DMA and signals readiness. Only the resumed graphics queue executes
  the blend and publishes completion. The runner requires two real waits and
  unforced resumes, the DMA-only producer lengths (21/15 words), graphics
  continuation lengths (23/17 words), three translated draws and the same pixel
  oracle/negative controls. This adds case 41, not a full 41-case aggregate.
  Texture sampling, compute image reads and arbitrary scheduling remain outside
  this result. No emulator fix was required for either dependent-blend variant.
  Existing full-image/prefix/span cross-queue regressions pass after the fixture
  split: `artifacts/dma-draw-split-{full,prefix,span}-regression.log`.
- Added dependent-draw coverage without a runtime change: `-DmaImageDraw`
  fills the first 90 rows blue, immediately blends a half-alpha red triangle in
  the same graphics submission, then repeats with a red DMA copy and blue blend.
  No flip separates DMA from its dependent draw. Expected purple intersections,
  preserved lower geometry and unchanged outside pixels all pass on Vulkan:
  `artifacts/dma-draw-native.log`. Three translated draws and two DMA operations
  are required. Blank, DMA-only (missing draw) and wrong-frame controls fail.
  This adds case 40; it is not a full 40-case aggregate result. It establishes
  destination-image blending after DMA in this queue, not texture sampling,
  compute image reads, all scheduling interleavings or transient label waits.
  Shared-fixture prefix, partial-row span and ordinary blend checks also pass:
  `artifacts/dma-draw-{prefix,span,blend}-regression.log`. Runtime binaries are
  unchanged from the preceding pixel-span fix; no new unit run was necessary.
- Extended preservation to pixel-aligned spans starting/ending inside a row on
  initialized linear RGBA8 UNORM single-mip images. Each span becomes a leading
  row fragment, complete middle rows, and a trailing fragment; GPU pixels outside
  these regions remain untouched. Byte-unaligned writes and other layouts still
  use the historical fallback. Immediate visibility before the CPU-image drain
  remains unverified.
  `-DmaImageSpan` updates pixels [14480,43440), starting at (80,45) and ending
  before (240,135), using both fill and copy. It failed before at (0,0):
  `artifacts/dma-span-before.log`, `artifacts/dma-span-before-guest.log`.
  After repair it passes directly and across queues:
  `artifacts/dma-span-after.log`, `artifacts/dma-span-cross-queue.log`.
  The preservation oracle checks solid/untouched bilinear footprints against
  exact colors/the first verified frame, and validates color/alpha channels at
  mixed boundaries. Constructed positive, blank and full-overwrite controls run
  before actual output. Cases 38/39 are added; the previous complete 37-case
  report below is not a 39-case result. Clean build and 1,279 unit tests pass:
  `artifacts/dma-span-build.log`, `artifacts/dma-span-tests.log`.
  Six targeted GPU cases pass on this runtime: both span variants, both prior
  prefix variants, full-image DMA and ordinary texturing. Evidence is in
  `artifacts/dma-span-{after,cross-queue,prefix-regression,prefix-cross-regression,full-regression,texture-regression}.log`.
- Complete **37/37 native GPU regression passed** on clean source `c9f231c`,
  with unchanged runtime files throughout the run:
  `artifacts/native-gpu/20260929-021256-372/report.json`.
  This includes both formerly failing prefix-preservation variants and all
  existing compute, queue, texture, mip, depth, blend and indexed-draw scenarios.
- **Partial DMA preservation fixed for tested linear RGBA8 row updates.**
  Managed write tracking now retains up to 64 disjoint byte spans, merges adjacent
  chunks, and consumes spans plus their generation atomically. Vulkan uploads
  only those rows for initialized, single-mip/depth linear RGBA8 UNORM images
  with tightly packed, whole-row writes. It does not clear unrelated later writes
  or describe a partial mirror as a complete CPU snapshot. Failed reads re-dirty
  their spans. Native fault tracking, unsupported layouts, partial rows and span
  overflow retain the previous full-upload behavior; their preservation is not
  claimed. Metal has not been changed.
  The formerly failing `-DmaImagePrefix` now passes, as does its cross-queue
  variant: `artifacts/dma-region-prefix-after.log` and
  `artifacts/dma-region-prefix-cross-queue.log`. Both preserve the GPU-only lower
  triangle through blue fill/red copy of the first 90 rows, CPU completion,
  source reuse and presentation. These add suite cases 36/37. This is presentation
  preservation coverage, not proof of immediate visibility to a dependent draw
  before the CPU-image drain, or transient waits on image memory.
  Clean Release build and **1,279 unit tests pass**, including merged/disjoint
  spans, consume/reset, generation advancement and bounded-overflow controls:
  `artifacts/dma-region-build.log`, `artifacts/dma-region-tests.log`.
  Before evidence remains `artifacts/dma-prefix-preservation-before.log` and
  `artifacts/dma-prefix-preservation-before-guest.log`: CPU words were correct,
  but the second capture failed at (0,542) because a 230,400-byte CPU upload
  overwrote untouched GPU pixels. Earlier `dma-prefix-before*` artifacts had an
  ambiguous deliberate CPU write and are superseded by these preservation logs.
- Added and passed `-DmaImageCrossQueue`: `artifacts/dma-image-cross-queue.log`.
  The graphics queue genuinely suspends twice while ACB owner 7 fills/copies an
  existing image, then resumes through separate ready/completion labels. All
  three GPU captures pass the existing pixel oracle; both complete image updates
  and their deferred callbacks execute on ACB. No forced resume or deadlock
  fallback occurred. This adds suite case 35, without changing emulator runtime.
  It verifies full-image transfer completion between queues, not transient waits
  on image data. An initial fixture pointer lacked relocation; materializing it
  at runtime corrected the guest. The runner counts wait diagnostics because
  detailed packet traces are throttled. The full-suite result below remains the
  previous 34-case run; do not report it as a 35-case aggregate.
  Sequential DMA image and ordinary triangle regressions also pass:
  `artifacts/dma-image-sequential-regression.log` and
  `artifacts/dma-image-triangle-regression.log`. The last build/unit result is
  unchanged because this change only extends native fixtures and runners.
- Added and passed a native existing-image DMA baseline, without a runtime fix:
  `artifacts/dma-image-native.log`. A red triangle establishes the image, then
  DMA fills it blue and copies a red source into it. All 57,600 CPU words are
  checked after each completion. The source becomes green after copy completion;
  the final GPU image must remain red. Both full-image captures pass all 2,037,120
  BGRA pixels, with blank/wrong-color negative controls. Logs require two successful
  230,400-byte writes to an existing backend image and one translated draw.
  All **34/34 native GPU cases pass** on clean source `dced885` with unchanged
  runtime files: `artifacts/native-gpu/20260928-103751-009/report.json`.
  This includes the new DMA signal and image cases in one complete run. Sequential mirror
  ordering is established, not transient waits on image memory or a cross-queue
  mirror race. The runtime and last 1,276-test unit result are unchanged.
- Plain-memory DMA copy/fill now preserves a satisfied wait before a later DMA
  resets the label. Two submitted-packet regressions failed before and two
  nonsatisfying controls passed (`artifacts/dma-label-before.log`). The native
  cross-queue fixture also failed before with guest result 78 after losing a
  signal: `artifacts/dma-label-native-before-guest.log`. After the fix it passes
  red/red/blue pixels, six immediate DMA fills, four computes/four draws, final
  reset-label values and no fallback (`artifacts/dma-label-native-after.log`).
  WRITE_DATA and reused-label native regressions pass on this same build:
  `artifacts/dma-label-write-data-regression.log`,
  `artifacts/dma-label-reuse-regression.log`. Release build has zero warnings/errors;
  all **1,276 unit tests pass** (`artifacts/dma-label-build.log`,
  `artifacts/dma-label-tests.log`). Native case 33 is added and passed individually;
  the full 32-case report below predates this DMA fix, and no full 33-case pass is
  claimed. Immediate latching is restricted to destinations without a known
  backend image; image-mirror completion and transient labels remain separate work.
- Rewind valid-bit patches now latch full overlapping label values, using the
  same width-aware path as GPU releases. Previously a 32-bit patch incorrectly
  satisfied a 64-bit equality wait for `0x80000000`, and missed the correct
  `0x1234567880000000` value. Both regressions fail before the change; the existing
  narrow-wait control passes (`artifacts/rewind-label-before.log`). All **1,272
  unit tests pass** after the fix, with zero build warnings/errors:
  `artifacts/rewind-label-build.log`, `artifacts/rewind-label-tests.log`.
  All **32/32 native GPU cases pass** on clean source `3636660`, with unchanged
  runtime files: `artifacts/native-gpu/20260928-075326-613/report.json`.
  Image-backed DMA producers need a
  separate completion-aware design: their CPU copy can enqueue a later GPU image
  mirror, so immediately latching after the copy could wake dependent work early.
- Fixed lost WRITE_DATA satisfaction when a later payload word resets a label
  (same-address writes) or changes the upper half of a wider label. Each successful
  word write now latches matching overlapping waiters before the next word.
  Four submitted-packet cases failed before; two nonsatisfying controls passed.
  All six pass after: `artifacts/write-data-direct-{before,after}.log`.
  The direct assertion runner was used after two transient VSTest socket 10013
  errors; the normal full runner subsequently recovered without system changes.
  Release builds with zero warnings/errors and **1,269 unit tests pass**:
  `artifacts/write-data-wait-build.log`, `artifacts/write-data-wait-tests.log`.
  The new native WRITE_DATA cross-queue probe passes red/red/blue pixel checks,
  four computes/four draws and three signal/reset operations without fallback:
  `artifacts/write-data-cross-queue-native.log`. The padded ABGR/AGC-fence native
  regression passes too (`artifacts/write-data-fence-native.log`). Case 32 is
  added to the aggregate; it has been tested individually, not in a full 32-case run.
  An initial fixture incorrectly reset the second signal before its consumer
  registered; it correctly remained suspended. That signal is now retained,
  and transient resets test only already-registered waiters. Evidence retained
  in `artifacts/write-data-native-fixture-ordering.log`.
- Fixed partial RELEASE_MEM writes falsely satisfying a wider waiter. A real
  submitted 32-bit release wrote `0x1234567800000001`, but previously latched a
  64-bit equality wait for `1` using only the packet payload. The before test
  fails with the waiter incorrectly removed: `artifacts/partial-release-before.log`.
  Both standard and Gen5 releases now inspect complete overlapping labels at
  the ordered write point. Upper-word waiters and narrow writes completing a
  wide value retain satisfaction across a later reset; unreadable/unrelated
  labels do not latch. Six regressions cover these cases. Release build has
  zero warnings/errors and all **1,263 unit tests pass**:
  `artifacts/partial-release-build.log`, `artifacts/partial-release-tests.log`.
  All **31/31 native GPU cases pass** on clean source `87af501`:
  `artifacts/native-gpu/20260928-010710-924/report.json`. The runner verified
  unchanged runtime files throughout. A subsequent interactive recovery attempt
  could not operate the window (`coordinate input geometry is unavailable`, then
  `FrameArrived timed out`); its strict diagnostic correctly failed for absent
  deferral/restoration markers. Evidence: `artifacts/window-recovery-87af501.log`
  and `artifacts/window-recovery-87af501-guest.log`. Recovery remains unverified.
- On the preceding `c4e0d7a` runtime, the five cases left unfinished by the
  interrupted aggregate passed individually: BC1, BC1 2D, BC3, index16 and
  index32/scissor (`artifacts/remaining-*.log`). The legacy padded ABGR/fence
  scenario also passed all 2,037,120 Vulkan pixels and 57,600 guest pixels:
  `artifacts/window-guard-fence.log`. These individual results do not turn the
  earlier failed aggregate into a passed run, or verify minimize/restore.
- The full 31-case run on `db78dfa` **failed after 26 passed cases**, at BC1
  presentation: `artifacts/native-gpu/20260927-124329-243/report.json`.
  Its guest log explicitly reports `windowState=Minimized`, a 0x0 surface and
  `ErrorOutOfDateKhr`; two captures are missing. Both cross-queue cases passed
  in that run. Do not describe this as 31/31 passed.
- Work in progress: defer taking a queued presentation while the native window
  is minimized or has a zero-size framebuffer, continue guest GPU work, and
  recreate the swapchain before taking the next presentation after restoration.
  Build passes (`artifacts/window-recovery-build.log`), all 1,257 unit tests pass
  (`artifacts/window-recovery-tests.log`). The window diagnostic has reproduced
  the original missing capture (`artifacts/window-recovery-before-guest.log`).
  A later run observed deferral but could not complete restoration through the
  UI tool (repeated user-input conflict), so end-to-end recovery remains unverified.
  An earlier interrupted run observed deferral/restoration but its window closed
  before the fixture completed: `artifacts/window-recovery-interrupted-guest.log`.
  This guard does not yet address minimization racing with an already-taken frame,
  or retaining every historical image when guest buffers are reused while hidden.
- Fixed premature cross-queue wakeup after CPU reset/reuse of a completion label.
  `CollectDeadlockBroken` now requires satisfaction latched for the current waiter;
  an older producer value cannot release a later waiter just because 500 ms elapsed.
  The native before case returned 77 after `agc.deadlock_break` resumed graphics
  before its new compute producer was submitted:
  `artifacts/cross-queue-reuse-before-guest.log`. After the fix, both ordinary
  and reused-label tests pass red/red/blue pixels, four GPU computes/four draws,
  genuine suspension/resume and no fallback: `artifacts/cross-queue-after.log`
  and `artifacts/cross-queue-reuse-after.log`. The consumer is submitted first;
  the reused-label case withholds producers for 750 ms and checks that completion
  does not advance. Release builds passed and all **1,257 unit tests** passed
  (`artifacts/cross-queue-build.log`, `artifacts/cross-queue-tests.log`). The new
  unit regression also verifies a fresh signal survives a subsequent label reset.
  Native suite now contains 31 cases; the interrupted full run is recorded above.
  This verifies one compute queue feeding graphics, not general queue concurrency.
- All **28/28 native GPU cases** and **27/27 general engineering cases** passed
  on clean source `d0c0584`:
  `artifacts/native-gpu/20260926-095224-350/report.json` and
  `artifacts/engineering/20260926-094941-679/report.json`.
  The 248-entry GPU runtime manifest exactly matches the general run's
  `runtime-after.json`; that snapshot also matches one taken during the general
  run after its build completed. The build/unit stage again passed all 1,256 tests.
- After those full runs, the fixture gained a separate ACB compute-queue mode.
  Five submissions to owner 7 pass the direct atomic baseline and four indirect
  dimension transitions, with exact GPU execution counts, completion and guards:
  `artifacts/indirect-dimensions-acb-after.log`. The ordinary compute, absolute
  graphics-queue and base-relative variants were all rerun after the shared import
  extension (`artifacts/indirect-dimensions-final-{compute,absolute,base}.log`).
  No emulator/runtime change was needed. This is newly integrated case 29, tested
  individually; the full 28-case report above predates the fixture extension.
  Cross-queue producer/consumer dependencies and concurrency are not established.
- Latest Release build: no warnings/errors (`artifacts/indirect-dimensions-build.log`).
- All 1,256 unit tests passed (`artifacts/indirect-dimensions-tests.log`).
- Indirect compute now synchronizes its 12-byte dimension buffer before CPU
  interpretation. The native atomic counter baseline passed before the fix, but
  the following GPU producer changed X from 1 to 2 while the consumer still ran
  once (guest result 91): `artifacts/indirect-dimensions-before-guest.log`.
  The corrected fixture passes 1->2, 2->1, 0->2 and 2->0, including completion
  and adjacent-buffer guards: `artifacts/indirect-dimensions-zero-after.log`.
  The unused retry-address output and misleading zero-means-pending comments
  were removed; a synchronized zero emits no compute work. Base-relative address
  addition is overflow checked. All **27/27** native cases passed on clean source
  `501aeeb`: `artifacts/native-gpu/20260926-094145-880/report.json`.
  Readback performance is not optimized.
- A subsequent fixture extension tests the base-relative three-dword indirect
  packet with a nonzero 16-byte offset, poison prefix dimensions, and GPU changes
  to X, Y and Z. Both extended variants passed individually without runtime
  changes: `artifacts/indirect-dimensions-base-after.log` and
  `artifacts/indirect-dimensions-expanded-after.log`. This adds native case 28;
  the later full result is recorded above. Separate compute-queue submission is
  tested by the subsequent ACB fixture above, not by either graphics variant.
- GPU-produced argument records are now synchronized even when CPU counts are
  already nonzero. The before probe issued all four computes and four draws but
  sampled stale depth (last frame red): `artifacts/indirect-gpu-args-before-guest.log`.
  Fresh ordered argument-span readback fixes the same probe to red/red/blue:
  `artifacts/indirect-gpu-args-after.log`. Completion is checked, range overflow
  rejected, and the old empty-read throttling/1024-record partial synchronization
  removed. All **26/26** native cases passed on clean source `a5626f9`:
  `artifacts/native-gpu/20260926-041323-524/report.json`. Synchronous readback cost
  is not yet optimized; this establishes one controlled compute-to-draw path.
- GPU-produced multi-draw counts now use a fresh ordered four-byte readback on
  every counted packet, with checked completion. Reusing the argument helper's
  throttling previously consumed stale counts after later compute dispatches.
  Native before: four computes but only two raster draws
  (`artifacts/indirect-gpu-count-before-guest.log`). Native after: four computes,
  four raster draws and correct red/red/blue pixels
  (`artifacts/indirect-gpu-count-after.log`). All **25/25** native cases passed
  on clean source `ee2af67`: `artifacts/native-gpu/20260926-040303-359/report.json`.
  GPU-produced argument-record coverage is added separately above.
- Indexed indirect multi-draw now uses the hardware 10-dword packet, correct
  stride offset, direct/memory-count limits and shader register destinations.
  The native two-record probe failed before the fix and passes afterward:
  `artifacts/indirect-multi-before-guest.log`, `artifacts/indirect-multi-after.log`.
  Seven unit cases verify count/stride/register behavior and exact builder bounds,
  including literal zero stride reusing one record. All **24/24** native cases
  passed on clean source `84ae366`:
  `artifacts/native-gpu/20260926-035337-215/report.json`.
  All 27 general cases passed on `abd5b6a` before the final
  zero-stride refinement: `artifacts/engineering/20260925-225914-436/report.json`.
  VGPR destination mode remains unsupported; GPU-produced count synchronization
  is covered separately by the new fixture above.
- The preceding general run `artifacts/engineering/20260925-225645-871/report.json`
  failed on its two-frame test. This time diagnostics explicitly reported
  `windowState=Minimized`, framebuffer/surface 0x0 and ErrorOutOfDateKhr, losing
  the second capture. A targeted diagnostic passed afterward
  (`artifacts/indirect-multi-two-frames-diagnostic-guest.log`), then the fresh full
  general run above passed. The failed report is retained; minimization handling
  is not fixed by the indirect-draw changes.
- Single indirect draws now copy argument values into the packet-selected shader
  registers. A native depth probe failed before this fix (third frame remained red)
  and passed afterward (red/red/blue): `artifacts/indirect-register-before-guest.log`
  and `artifacts/indirect-register-after.log`. Unit checks cover both single packet
  variants, nonzero argument offsets and a signed indexed base-vertex bit pattern.
  All **23/23** native cases passed on clean source `38a14ab`:
  `artifacts/native-gpu/20260925-224622-652/report.json`. The 22-case and general
  runs below predate this fix. General base-instance semantics remain separate.
- The first post-fix probe hit the old runner's 20-second log deadline although
  its guest later completed successfully. Preserved log:
  `artifacts/indirect-register-after-timeout-guest.log`. The runner now waits for
  its exact child process with a 60-second bound, then checks exit/log/pixels.
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
  enhancement was subsequently exercised by the 18-case run below.
- A 5x3 base with odd-sized mip storage passed native red/blue/red checks
  (`artifacts/mip-odd-native.log`). Green unused storage texels ensure the shader
  cannot accidentally sample the ceiling-rounded physical extent. It is now the
  eighteenth aggregate case. All **18/18** passed together on clean source `ec8cc06`:
  `artifacts/native-gpu/20260925-011332-977/report.json`, including verification of
  all 248 runtime manifest entries.
- Native sRGB mip test reproduced a wrong first-level sample (blue=55 instead of
  red=55, alpha=128). Allowing RGBA8 number type 9 through the full-chain reader
  fixed the same probe: `artifacts/mip-srgb-before.log`, `mip-srgb-before-guest.log`,
  `mip-srgb-after.log`. Three frames verify RGB decoding (128 -> 55, 64 -> 13 on
  UNORM output), alpha remaining 128, cache reuse and lower-level-only changes.
  All **19/19** aggregate cases passed on clean source `551cc2f`:
  `artifacts/native-gpu/20260925-082252-052/report.json`.
- BC1 UNORM native mip sampling failed before the block-aware reader and passed
  afterwards (`artifacts/mip-bc1-before.log`, `mip-bc1-before-guest.log`,
  `mip-bc1-after.log`). The new case requires red/blue/red, correct opaque alpha,
  one unchanged reuse and two uploads. All **20/20** passed on source `844915f`:
  `artifacts/native-gpu/20260925-083835-186/report.json`.
- All **27/27** general engineering cases passed on source `844915f`:
  `artifacts/engineering/20260925-083517-069/report.json`. Its saved
  `runtime-after.json` exactly matches all 248 runtime entries in the subsequent
  20-case GPU report above. An earlier GPU run preceded this rebuild and is not
  used to claim identical binaries.
- The new BC1 multi-row probe passed independently:
  `artifacts/mip-bc1-2d-native.log`. A 16x16 base follows an 8x8 lower level;
  sampling its bottom-right block gives blue, then red after a rewrite.
  All **21/21** passed together on source `4398a1c`:
  `artifacts/native-gpu/20260925-134214-928/report.json`.
- BC3 UNORM reproduced blue instead of red at the base level before the fix:
  `artifacts/mip-bc3-before-evidence.json` and `mip-bc3-before-guest.log`.
  Extending the block-aware reader to 16-byte BC3 blocks fixed the native probe:
  `artifacts/mip-bc3-after.log`. Frames verify red/blue/red with alpha 128/128/64,
  one cache hit and two uploads. Unit cases cover odd dimensions, rows above
  256 bytes, truncation and unsupported BC3 sRGB. All **22/22** native cases passed
  on clean source `47ec2fc`: `artifacts/native-gpu/20260925-223516-947/report.json`.
  The general engineering run also passed all 27 cases:
  `artifacts/engineering/20260925-135039-796/report.json`. A runtime manifest saved
  at resumption matches all 248 entries of the new native GPU report.

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
5. CPU RGBA8 sRGB mip chains now use the full-chain path instead of interpreting
   the allocation's lowest mip as the base texture. Vulkan performs sRGB sampling.
6. CPU BC1 UNORM mip chains calculate storage in compressed blocks, retain texel
   dimensions for sampling and provide texel-unit row lengths to Vulkan copies.
7. CPU BC3 UNORM mip chains use the same layout with 16-byte blocks and preserve
   independently encoded alpha during sampling and lower-level rewrites.

Mip layout follows AMD AddrLib GFX10: smallest level first, ceiling-rounded storage
sizes and 256-byte rows. Primary-source links and the supported subset are in
`tools/native-probes/README.md`. The earlier experimental base-first mip result in
`artifacts/native-gpu/20260924-115830-882/report.json` is superseded; it must not be
used as evidence for correct GFX10 layout.

## Scope and next work

Full-chain Vulkan upload currently supports CPU-authored linear 2D RGBA8 UNORM/sRGB or BC1/BC3 UNORM with implicit
width-derived pitch and no existing backend image at the allocation address. It uses
per-draw CPU snapshots with exact-content Vulkan reuse. Tiled/array/storage/GPU-written
chains and broad commercial-game compatibility remain separate work.

Flip-completion lifecycle is implemented and passed the frozen-runtime aggregates
above. Real minimize/restore passed one cycle in each of three modes with human
window control; repeated cycles and movie-overlay interaction remain unverified.
Further tasks:
extend partial-DMA coverage to byte-unaligned and non-linear layouts, and go beyond
the passing sequential and cross-queue
full-image transfers before changing its completion semantics. A transient-image
wait regression must distinguish the CPU copy, queued GPU image update, and waiter
release, including a reset between them and failed/unreadable writes.
Plain-memory DMA and rewind patches now have separate regressions above.
The bounded row-upload path now preserves separate managed-write spans. Do not
clear a dirty flag to suppress DMA writes: that could discard independent CPU
writes. Dependent destination-image blending before presentation now passes.
Texture sampling, read-only compute fetches and single-pixel storage writes now
have separate direct/cross-queue regressions above. Single-texel R32_UINT atomic
add and bounded INC/DEC now have independent graphics/ACB checks. Further work
should cover other image-atomic consumers and operations, additional
atomics, other layouts and failed/unreadable writes without
inferring those capabilities from the passing point-read/store cases.
Minimized 0x0 surfaces are confirmed in the earlier failed
aggregate; recovery remains unverified. Do not weaken pixel/count checks. Next texture work
can cover additional formats, with an independent layout reference before broadening
the supported CPU upload subset. Native odd-sized mip sampling is now covered.

Use `tools/native-probes/Test-TouchePX5-NativeGpu.ps1` for GPU checks. Keep sources and
builds under `C:\Dev\TouchePX5`; the old OneDrive triangle runner is obsolete. General
engineering checks still use the OneDrive `Test-TouchePX5-Engineering.ps1` runner.
Physical controller operation and commercial games have not been verified by these
controlled fixtures.
