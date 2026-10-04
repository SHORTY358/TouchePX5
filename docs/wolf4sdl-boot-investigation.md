# Wolf4SDL PS5 boot investigation, 2026-10-04

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

## Observed boundary

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
