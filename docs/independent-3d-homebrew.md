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
