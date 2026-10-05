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
inline argument frame to `main(argc, argv, NULL)`; the runtime still owns ELF
preinit/init/fini array execution. DATADIR is `/app0/`, with guest arguments
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
