# Wolf4SDL PS5 boot investigation, updated 2026-10-05

The new integration target is the unmodified third-party Wolf4SDL PS5 v1.0
shareware ELF. **No game window or gameplay has been verified.** This replaces
further speculative GPU-format work as the immediate compatibility target.

## Provenance

- Packaging project: https://github.com/alex-free/wolf4sdl-ps5
- Release: https://github.com/alex-free/wolf4sdl-ps5/releases/download/v1.0/wolf4sdl-ps5-v1.0-5e63997.zip
- Archive SHA256: C8760CBC85106E9BB1C01991DF1E30020014439ACA4F527921F1686417B9A32B
- Packaging source checkout: 009d98cd219709cff5a8d2c50f9b35c26412059e
- SDK reference: https://github.com/ps5-payload-dev/sdk at
  f7fd02e6e195902a449b5e664917be8c01888b34. This is a current reference, not
  evidence that the release used that exact SDK revision. Confirmed startup
  behavior below also follows disassembly of the actual release ELF.

Inputs are under `artifacts/external-homebrew/wolf4sdl-v1.0`. No external build
scripts were executed; the archive contains official shareware game data.

## Original boundary (2026-10-04, superseded below)

The loader accepts the ELF, maps it at 0x400000, and reaches entry 0x400040.
It installs 241 import bridges and discovers 9,938 runtime symbols. Its FreeBSD
ABI version is 0, so the current classifier reports Gen4 despite upstream PS5
provenance. Do not relabel it by changing the artifact's bytes.

Startup calls the injected resolver with handle 1, then 0x2001, asking for
`sceKernelDlsym`. Both fail. A runtime alias experiment did not fix the launch
and was removed: ordinary module lookup correctly restricts concrete modules
to their own exports. Handle 1 is the registered executable, not libkernel.

There are additional boot-contract problems to address together:

1. The SDK distinguishes a resolver callback from its syscall entry at +0xA.
   Our injected payload assigns both entries the same resolver dispatcher.
2. `kernel_dynlib_dlsym(pid, handle, name)` returns an address. The loader currently
   intercepts that symbol using the public `sceKernelDlsym(handle, name, out)`
   dispatcher, which has a different calling contract.
3. Payload arguments include read/write descriptor pairs and kernel addresses.
   Current injected placeholders do not implement those facilities. The actual
   `__kernel_init` checks these fields and requests firmware/module information
   through syscall 0x289; resolving the first symbol alone is insufficient.

Evidence: `first-launch.log`, `second-launch.log`, `third-launch.log` in the
input directory. Each launch was bounded at 20 seconds and stopped only its
owned emulator process. Timeout is not successful execution.

## Reproduce and next decision

After a normal Release build, run `tools/external-homebrew/Invoke-Wolf4SDL.ps1`.
It records ELF/executable hashes, source state, output and timeout under the
input directory's `runs`. It does not download, patch, or rebuild the ELF.
Guest data/config arguments are still missing; this run diagnoses pre-main
startup, before those arguments matter.

Design a distinct payload startup adapter without weakening ordinary module
isolation or confusing resolver and syscall ABIs. Determine whether this
unmodified payload can be supported without emulating the SDK's privileged
kernel introspection, or whether a transparent upstream-source build using a
supported startup contract is the practical first playable target. Keep the
original release as independent evidence either way.

Validation must pair real third-party startup progress with dynamic-module
isolation/stop-unload regression checks. Do not infer gameplay from either.

Baseline verification after removing the failed alias experiment: Release build
passed with zero warnings/errors, the bounded runner reproduced both resolver
failures (`runs/20261004-103943-897/report.json`), and the existing focused
dynamic-module lifecycle/public-symbol-isolation check passed
(`artifacts/wolf-baseline-dynamic-module.log`). No runtime changes remain from
the alias experiment.

## Implemented startup correction (2026-10-05)

The injected resolver and syscall addresses were 10 bytes apart, but native
import patching writes 16 bytes. They now jump to disjoint landing pads.
The syscall pad uses a RIP-relative indirect jump so RAX retains the syscall
number. The public module resolver remains unchanged; the payload callback
uses a separate, deliberately limited namespace (standalone libkernel handle
1 and libc handle 2; only already-installed HLE functions). Alternative
hijacked-process handle 0x2001 and remote process IDs are not aliases.

The SDK helper now reads `(pid, handle, name)` and returns an address directly.
The resolver callback reads `(handle, name, out)` and returns a status instead.
Payload names/output pointers use mapped guest memory; invalid pointers cannot
fall through to raw host reads/writes. An adversarial native fixture exposed a
hang in the old raw-read fallback, which this path now avoids. Ordinary module
lookup and its visibility/stopped-module rules were not relaxed.

Confirmed on the **unchanged release ELF**: the first resolver succeeds, and
`__kernel_init` reaches `SYS_dynlib_get_obj_member` (0x289) with handle 2,
member 8 and a guest output pointer. Unsupported payload syscalls stop at a
diagnostic boundary and unwind to the host; no firmware data is invented and
the launch is reported as an error, not success. Before: deadline termination
after 20 seconds. After: natural emulator exit with code 4 / CPU_TRAP and the
exact unsupported syscall. This is boot progress, not game initialization.

Evidence:

- Real release: `artifacts/external-homebrew/wolf4sdl-v1.0/runs/20261005-003038-642/report.json`
  (248 runtime hashes; resolver failure false; unsupported syscall 0x289;
  deadline termination false; gameplay false).
- Native ABI checks: `artifacts/payload-bootstrap/20261005-003033-743/report.json`
  (2/2; callable resolver, direct-return SDK helper, bad PID/handle/name/output
  cases, RAX/R10 syscall preservation, explicit failure unwind).
- Focused loader/module units: 111/111 in `artifacts/wolf-adapter-unit-tests.log`.
- Existing native module lifecycle and public symbol isolation: pass in
  `artifacts/wolf-adapter-dynamic-module.log`.
- Release build: zero warnings/errors in `artifacts/wolf-bootstrap-adapter-build.log`.

## Next work: userland startup route

The reference SDK proceeds from the firmware query into kernel memory reads,
process credential lookup and direct-map/IOMMU discovery. Adding a made-up
version field would merely hide this dependency. The current payload argument
placeholders still do not supply a real kernel environment. Raw payload
syscalls, dynamically loaded payload handles and general SDK compatibility
remain unsupported.

Next, inspect and pin the actual engine branch
`https://github.com/alex-free/wolf4sdl/tree/sdl2remap` and its PS5 build recipe.
Prepare a clearly labelled source build with a userland startup contract that
preserves the engine, PS5 SDL backend, constructors, arguments and data paths.
If implementing an explicit emulator startup adapter is preferable, first
specify those same obligations; do not silently jump to `main` or silently
return success from privileged SDK initialization. Keep the original release
launch available as a separate compatibility test.

The next visible acceptance target is a real Wolf4SDL menu using the official
shareware data, followed by input and a playable level. No estimate is justified
until the source build and its next actual runtime blocker are known. Source
inspection/build preparation can proceed on Sol; request Astra again only for
a specific unresolved startup architecture decision.

## 2026-10-05: explicit PS5 source build and first presentation

The source route is now built on Windows with LLVM 19 targeting
`x86_64-sie-ps5`. This is an explicitly adapted startup profile, **not** proof
that the unchanged upstream payload boots. Its engine and static SDL backend
remain PS5 code. The separate unchanged-release test remains available.

Pinned inputs:

- `alex-free/wolf4sdl`, branch `sdl2remap`, engine
  `5e639973a665443053c58a0498b68f487537ec80` (clean checkout).
- Controller database `01275a5bbefbab17f274e60393cd75405f74feb3`.
- Shareware recipe inspected from `alex-free/pacbrew-repo`, branch `wolf4sdl`,
  `b8addd833fbb6c8c7f1996b4292c475505975115`.
- Open dependency bundle:
  `https://github.com/ps5-payload-dev/pacbrew-repo/releases/download/v0.40.2/ps5-payload-dev.tar.gz`,
  SHA256 `A85F65DE418A8E6A898C6C3E3C870D50FFF7618A200E4DD59EA9692AF6ECEC4D`.
- Linker script from open payload SDK v0.43:
  `https://github.com/ps5-payload-dev/sdk/releases/download/v0.43/ps5-payload-sdk.zip`,
  SHA256 `A9CC9929F21B2B2C5D5B309F3BAB4997067C45281C0622CF4838B1AECBA66FCB`.

`tools/external-homebrew/Extract-WolfSdk.py` verifies the dependency archive,
extracts selected build inputs inside the checked output root, and materializes
32 safe file aliases as regular files (6,447 regular archive inputs). It does
not execute package scripts. `Build-Wolf4SDL.ps1` copies the pinned engine
inputs, applies the shareware defines, generates controller mappings and links
the PS5 ELF using `wolf4sdl-start.S`. The latter translates the emulator's
inline argument frame to `main(argc, argv, NULL)`. Correction from the subsequent
startup investigation: the generic runtime skips main-module initializers; the
explicit entry now calls its init array before main (see the 2026-10-08 section
below). DATADIR is `/app0/`, with guest arguments
`--configdir /app0`; the runner copies the eight official .WL1 shareware files
next to the ELF, which is the actual `/app0` mount.

Re-extraction and rebuild completed. Source ELF SHA256 is unchanged:
`D7A1C8A46BC2DFC958ED9237480C9E0523D53D9F1C1739036F243CB2C0A4577A`.
Existing upstream pragma-pack warnings remain; the emulator Release build has
zero warnings/errors. Build and launch reports record hashes and provenance.

Runtime fixes exercised by this independent engine:

1. Native import setup resolves ordinary exported names as well as NIDs, then
   uses the canonical NID for existing intrinsic/dispatch classification.
2. `getenv` returns NULL for the currently empty guest environment. This is
   not support for setenv, environment inheritance or environment mutation.
3. snprintf/vsnprintf output, main-direct allocation output, event-queue
   handles and VideoOut buffer descriptor reads accept live libc malloc
   storage as well as mapped guest memory. The new fallback checks allocation
   ownership and bounds; it does not accept arbitrary writable host pointers.
4. Return-address classification formerly rejected everything below 12 GiB.
   The PS5 source ELF is mapped at 0x400000. The invalid heuristic selected a
   host trampoline from a later stack slot at import 192 and poisoned the
   captured continuation. The classifier now permits low canonical addresses
   while still requiring executable mapping. The real launch passes this
   point and reaches import 661, where controller mapping parsing requests
   the missing `strtok` export.

Evidence: before the address fix, run
`artifacts/external-homebrew/wolf4sdl-userland/runs/20261005-010523-202`
shows valid low returns being rejected, the substituted return, successful
mutex acquisition, and deadline termination. After the fix, run
`20261005-010652-425` proceeds through SDL initialization into controller
mapping parsing; `strtok` is the next actual blocker. VideoOut logs its first
1920x1080 presentation, but **no menu screenshot or gameplay was verified**.
The bounded launch still reaches its deadline. Remaining unresolved calls
include sigaction, sceKeyboardInit and sceUserServiceGetForegroundUser;
four imported data relocations also remain unresolved. These are not silently
reported as supported.

Focused validation includes malloc output/truncation/ownership/freed-pointer
checks, event-queue output and empty guest environment; native return-address
validation checks executable low memory, nonexecutable memory and invalid
addresses. Existing payload resolver/syscall ABI checks passed 2/2 again in
`artifacts/payload-bootstrap/20261005-010124-079/report.json`.

Focused checks passed 288/288 in `artifacts/wolf-userland-focused-tests.log`,
including the real low executable mapping regression. Next: checkpoint this
stage, then return to Sol for tokenization and the next real startup blocker. The
acceptance target remains an independently captured actual game menu.

## 2026-10-08: controller parsing, data reads and audio startup

The source profile retains ELF SHA256
`D7A1C8A46BC2DFC958ED9237480C9E0523D53D9F1C1739036F243CB2C0A4577A`.
It now implements the requested strtok, ASCII C-locale case conversion, strlcpy,
strlcat, LP64 strtol, atoi, puts, and XMM0 double sin/log/exp2 value results.
Math errno/fenv and broader locale support are not implemented by these additions.
Implicit strtok cursor is scoped to guest memory/process. Reads/writes to native
malloc buffers require ownership and allocation bounds: fread, kernel read,
semaphores, pthread handle output and single-port AudioOut now use this storage.

The open ELF autodetects Gen4 despite using the PS5 SDL backend. CLI
`--generation=ps5` and the runtime option explicitly select PS5 without patching
the file. The runner records this selection. The audio output export then resolves
and the worker submits buffers instead of repeatedly reporting missing imports.

Evidence progression (all bounded, no gameplay claim):
- `20261008-041220-339`: sem_wait completes after worker sem_post; sem_destroy follows.
- `20261008-041446-077`: libc trace advances through game headers, then last malloc
  returns to `0x5AD2EE` in C++ operator new, caller `0x41FAF2` in US_SetScanNames.
- `20261008-041856-947`: return probe preserves 0x5AD2EE, yield=False.
- `20261008-042420-247`: periodic native host snapshots repeatedly place the main
  thread at managed exception-handler trampoline +0x285. The earlier snapshot
  also sampled +0x26B. Audio worker remains active. Cached CpuContext.Rip still
  names malloc and must not be treated as the actual native instruction pointer.

Next investigation must recover the original exception and establish why the
handler cannot finish, including its recursive entry lock. Do not bypass errors,
force success, or replace the engine map code to hide this fault.

Validation: 43/43 focused startup regressions, 1193/1193 full library tests;
CLI build zero warnings/errors. Added regressions cover live malloc audio bytes
and an allocation overrun, malloc-backed semaphore lifecycle, and XMM0 math values
including NaN/infinity. The earlier first-frame BMP is red horizontal stripes,
not an actual game menu; it was produced by bmp-linear-read with tiling_mode=0.

## 2026-10-08: recovered native fault, constructors and real menu

Disassembling the emitted exception trampoline found `lock cmpxchg [rcx],r10`
at +0x280, although the lock address is in r9. Both branches used REX 0x4C;
0x4D adds the missing B bit. The new regression decodes both actual generated
instructions and executes host/recursive entry against independent lock and
exception-record storage, verifying depth 1/2 and final unlocked state.
This exposed the underlying native access violation at US_SetScanNames+0x8D:
the C++ scan-name map had never been initialized. Contrary to the earlier note,
the generic runtime skips main-module initializers. The explicit source-build
entry now executes __init_array_start..__init_array_end before main, preserving
the SysV callee-saved registers, argc/argv and call alignment. Generic payload
startup is unchanged. New ELF SHA256:
`703FA9B0B305FDEFD6799511D4296653F9D9512DC3517245CE18AB0FE55BDE6D`.

The engine then requested atan, tan, float sinf and owned strdup. These exports
now supply XMM0 values and independently freeable bounded string copies. No new
errno/fenv math guarantees are claimed. Display submissions formerly failed for
the CPU-written tiled framebuffer. A restricted Gen5 RGBA32 tiled fallback now
decodes the 512x128 tile layout from the open SDL reference:
https://github.com/ps5-payload-dev/SDL/blob/ee4c47dc0d617b3bc8f35108f9956baf228a1322/src/video/ps5/SDL_ps5tilemap.c
The address equation is expressed as independent X/Y terms; source attribution
and its original notice are retained in the altered C# implementation. Literal
offset/color oracles include tile boundaries, padded pitch and unreadable tail.
This is a CPU display-buffer path, not broad GPU tiling/shader support.
BMP fingerprints now cover the decoded visible image, including pixels outside
the old linear raw-memory prefix.

Keyboard state follows the same pinned SDL backend's 96-byte structure, scoped
to the guest process. Held keys keep their slots; released slots remain empty
for a read so SDL receives release before a replacement press. Focus loss
releases input. Supported keys include letters, digits, arrows, Enter/Esc,
Space, navigation and modifier keys. Foreground-user lookup returns the existing
single logged-in user. IME, mouse and physical keyboard operation remain separate.

Bounded real-engine evidence:
- `20261008-044902-949`: visually inspected readable sign-on with "Press a key";
  VideoOut submits successfully and buffers alternate. The shared dump files
  from that run were later overwritten; this is a historical observation.
- `20261008-050035-072/frames`: independently viewed help page, readable command
  reference and page 1 of 4 (preserved BMPs and flip metadata).
- `20261008-050206-862/frames`: Options main menu, with the quit confirmation
  overlay produced by the test sequence. No level/gameplay claim yet.

Test input is opt-in `SHARPEMU_TEST_KEYBOARD_REPLAY`, also exposed by the runner's
`-KeyboardReplay`: `start_ms:Windows_virtual_key_decimal:duration_ms;...`.
The clock starts at keyboard initialization, steps end within 60 seconds,
and no OS input is sent. Normal launches use focused physical host input.
The runner records the script and copies only this launch's BMPs and metadata
into its run directory with hashes. Diagnostic frame bounds default to eight;
`SHARPEMU_DUMP_VIDEOOUT_AFTER_FLIP`, `SHARPEMU_DUMP_VIDEOOUT_EVERY_FLIP` and
`SHARPEMU_DUMP_VIDEOOUT_MAX_FRAMES` allow bounded later/strided capture (maximum 64).

Validation: full library suite 1225/1225 (`artifacts/wolf-menu-regressions.log`),
zero-warning CLI builds, and native linear CPU flip lifecycle with three
independent displayed-color oracles (`artifacts/wolf-menu-linear-flip.log`).
Next acceptance: New Game -> episode/difficulty -> independently captured level,
then movement and shooting. The unchanged original payload is still unsupported
at privileged bootstrap syscall 0x289; commercial PS5 games/GTA VI are unverified.

### First level and automated movement observation

Run `artifacts/external-homebrew/wolf4sdl-userland/runs/20261008-050603-353`
reaches the first shareware level. Its report preserves 28 frame hashes and
the exact replay: initial acknowledgements, Esc to leave help, N to reject a
quit prompt if present/select New Game, Enter for episode/difficulty, then
Up/Right/Up. Independently inspected frame 16 (flip 600) shows the spawn door,
weapon and HUD; frame 28 (flip 920) shows the changed view facing a blue wall.
The game remains active until the bounded deadline, with successful alternating
framebuffer flips up to 937. Health 100, ammo 8. This establishes a loaded level
and response to automated movement/turn input; physical controls, shooting,
audible audio, performance and finishing a level still require separate evidence.

Run `20261008-050746-265` adds left-Ctrl test presses. Its preserved frame 1
shows ammo 8 at the spawn door. Frame 22 shows ammo 7 and the firing pose after
the movement/turn sequence and second Ctrl press. This visually confirms at
least one shot consumed ammunition; it does not claim a kill or held autofire.
All 22 captured-frame hashes and diagnostic settings are in its report.

### Repeatable level scenario and actual presented image

`tools/external-homebrew/Invoke-Wolf4SDLLevelSmoke.ps1` runs the recorded
60-second acknowledgement/menu/level/movement/shooting sequence. It restores
its process environment and saves bounded CPU snapshots plus periodic Vulkan
swapchain readbacks. CPU and GPU presentation now share the same diagnostic
readback interval/trigger selection; default first-frame/continuous behavior
is retained. This is opt-in diagnostic plumbing, not a rendering shortcut.

Run `20261008-051524-780` preserved nine actual swapchain samples under
`level-smoke/20261008-051524-692`. The ninth sample was converted losslessly to
`presented-level.png` and visually inspected: blue-wall view, weapon, HUD,
100 health and 7 ammunition. This confirms the game image survived actual
Vulkan presentation, independently of the CPU-buffer BMP observations.
The native tiled three-frame pixel oracle passed again after the shared
diagnostic change (`artifacts/wolf-readback-tiled-check.log`).

### Writable configuration and save/load

The original `/app0` configuration argument was read-only. The engine's save
code does not check every stdio failure, and a NULL `FILE*` reached `fwrite`.
The old fallback mistakenly printed save bytes to stdout. This is now a
zero-element failure; overflowing requests and writes outside an owned libc
allocation are rejected. `fopen` also returns NULL for a denied resolved path.
`fread` rejects overflowing sizes and write-only streams before host reads.
Regression checks cover real file bytes, heap bounds, read/write access,
size arithmetic, zero counts, and ASCII `isprint` used by save-name entry.

`Invoke-Wolf4SDLUserland.ps1 -ConfigSubdirectory <safe-basename>` maps only a
new/selected artifact subdirectory to writable `/temp0` for the child process.
The runner restores `SHARPEMU_TEMP0_DIR` afterward and records the actual host
configuration directory. `/app0` remains read-only.

Run `20261008-052937-455` created `save-probe-20261008/savegam0.wl1`, 23,468
bytes, slot `sol1`, map 0, health 100, ammo 8. In fresh run
`20261008-053753-026`, F3/Enter opened that file for reading, consumed it through
position 23,468, and returned to the level. Preserved frame 25 shows the
spawn-door scene, weapon firing pose and HUD. This establishes actual engine
save/load. The stronger scenario in run `20261008-054252-396` saved slot
`sol2` with ammo 7 after firing and turning. Fresh run `20261008-054353-483`
consumed all 23,468 bytes; frame 15 shows the changed view and ammo 7.
Its save is retained in `save-roundtrip-20261008-054252-323`.

`Invoke-Wolf4SDLSaveLoadSmoke.ps1` provides a two-process bounded scenario
with a unique configuration folder. Its phase-one header assertions require
slot `sol2` and ammo 7; phase-two captures and read trace must still be inspected.
The first scenario revision missed its final confirmation near the wall-clock
deadline and correctly failed because no save existed. Fixed-time test input
is not a proof of physical keyboard usability or a reliable unattended game bot.

### Native audio signal and a repeated presented HUD

Run `20261008-054532-611` captured actual swapchain frames every 101 presents
(odd stride samples alternating display buffers) and a raw AudioOut diagnostic
tap. The latest readback under `av-check/20261008-054532-491` contains the
complete level/HUD/weapon, health 100 and ammo 7. Both decoded CPU buffers also
show the HUD. This successful repeat does not establish the root cause of the
earlier intermittent missing HUD portions in run `20261008-052037-377`.

The runtime opened the Windows `winmm` stream at 48 kHz, stereo s16, and emitted
nonzero audio peaks. The raw tap is 10,936,320 bytes / 2,734,080 stereo frames
(56.96 seconds), normalized peak 0.736572, RMS 0.075055, 4,964,978 nonzero
samples out of 5,468,160, and no full-scale clipped samples. SHA256:
`a3f6226f4534026f57e3276eca8f3abc69e84e88c14e21d5949fee3a4c605ced`.
`Analyze-AudioPcm.py <tap.pcm> --rate 48000` writes an analysis report and
lossless WAV without playing it. A known-sample fixture separately checks
stereo bytes, peaks, clipping, duration and malformed-frame rejection.
No audible speaker or correctness-of-music claim is made from signal amplitude.

The output trace had hard-coded `coreaudio` for every non-NULL host backend;
it now carries the backend name selected at port creation, including tests.
