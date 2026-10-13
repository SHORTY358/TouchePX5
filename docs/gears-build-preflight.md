# Gears build dependency checkpoint — 2026-10-13

The shared-page producer scene now passes independently changing left/right
regions, reverse order and missing-right controls. Further variations should
serve a concrete failure. Resume the larger independent Gears scene instead.
Gears has not been built or launched in TouchePX5.

## Verified from the retained source

Source: `artifacts/external-homebrew/ps5-agc-gears`, previously pinned at
`1ae1f9182abd2770c131b97419034fb85173c2dc`.
Its README and `tools/build_native.sh` require:

- LLPC fork `mpereiraesaa/llpc` at
  `23a0757d922da7eb06c6a626d48553e0fb99fde0`, supporting `-gfxip=10.1.3`.
- GPUOpen LLVM at `8fd93e26cf9b1235fc9573b68b96233818be0ed4`.
- Native foundation `mpereiraesaa/ps5-native-app-boilerplate` at
  `37dd53602bdead63936f718004555ba10154be48`, its payload SDK, zlib host
  dependency, native builder and `runtime/libc.prx`.
- Host contract checks and publication audit, separate from the native build.

The native script uses Bash/make, builds shaders first and then foundation
dependencies. It recreates its own build/dist folders. Inspect those resolved
paths before invoking it; do not run its cleanup against unrelated checkouts.

## Current local preflight

The expected foundation `.deps` directory does not exist. CMake and Ninja are
available under the installed BuildTools directory, although absent from PATH.
`wsl --list --quiet` returns installation/help text rather than a usable distro
inventory; the executable alone does not establish a working Linux environment.
No Windows feature or distro was installed during this preflight. C: had about
81.4 GiB free at the check, which must be refreshed before a large LLVM build.
The existing gfx1030 assembler is not the required GFX1013 LLPC compiler.

## Next concrete steps

1. Inspect the exact LLPC/LLVM pins and their dependency/build contracts; record
   licenses, source pins and host compatibility before downloading a large tree.
2. Select an isolated supported build path using available host tools. Record
   disk needs and limit build parallelism for the 16-GiB machine. Do not assume
   Windows clang can build the Linux-oriented native packaging unchanged.
3. Obtain and verify the pinned compiler and foundation. Compile a minimal
   public GFX1013 shader and verify PAL metadata and absent relocations before
   declaring the compiler available.
4. Run Gears host contracts and audit in its verified isolated checkout.
   Build native only after required compiler/foundation outputs exist.
5. Retain exact source/tool/input hashes, then run an owned bounded emulator
   launch. Record its first actual load/ABI/shader/render blocker and create
   a focused reproduction. A built executable alone is not a rendered scene.

Until these steps succeed, retain the compiler/foundation blocker explicitly;
do not describe the cube or producer probes as Gears or GTA VI compatibility.
