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
