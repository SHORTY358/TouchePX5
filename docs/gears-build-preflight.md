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


## Windows compiler preparation — 2026-10-13

The documented LLPC/LLVM pins and native foundation are now fetched in
`artifacts/external-homebrew/gears-toolchain`. Thirteen exact source commits are
recorded in `tools/external-homebrew/GearsToolchainDependencies.json` and verified
by `Get-GearsToolchainSources.ps1`; original checkouts remain clean. Supplemental
PAL/SPVGEN/MetroHash/CWPack/Vulkan-header commits are candidate versions selected
before the LLPC parent date, not a claimed upstream validated dependency set.
The GLSL/SPIRV dependencies come from AMDVLK manifest commit
`12d06a9802d75fd043fb4002a1854cc5d5d121f8`; llvm-dialects uses LLPC's exact gitlink.
Available root license hashes are retained in `source-license-report.json`.
LLPC is MIT, LLVM carries its own license, and the native foundation is GPL-3.0;
retain all notices and subordinate dependency licenses.

A separate CMake wrapper configures native Windows MSVC/Ninja with the existing
BuildTools, Release, AMDGPU/X86 only, two compile tasks and one linker task.
The pinned compiler contains Windows build paths; a working CMake configuration
is now verified, but the compiler executable is not yet available. The wrapper
adds only host configuration, without editing upstream compiler sources.
Microsoft DXC v1.8.2505 is required for LLPC's advanced-blend runtime generation.
Its official archive is cached with exact size and observed SHA256
`81380F3ECA156D902D6404FD6DF9F4B0886F576FF3E18B2CC10D3075FFC9D119`.
This is a recorded download digest, not a published upstream signature/digest.

The first bounded build compiled hundreds of objects. A later batch exposed
optional Windows DIA/PDB support requiring unavailable `atlbase.h`; the wrapper
now sets the upstream `LLVM_ENABLE_DIA_SDK=OFF`. This disables Windows debug-symbol
support, not the GFX1013 shader target. The installed MSVC has Russian resources;
CMake's detected include prefix was mojibake. The wrapper uses the observed UTF-8
Russian prefix, so Ninja parses header dependencies and suppresses include spam.
The earlier invalid dependency cache was removed only inside the isolated build
directory, and those objects are rebuilding with correct dependency tracking.
The prior failure and batch logs/reports are preserved.

`Setup-GearsPython.ps1` supplies an isolated Python environment with PyYAML 6.0.3.
Four upstream Python source contracts and the publication audit passed there;
the audit checked 122 allowlisted files. The native C host suite has not run,
so this is not `make test` completion. Gears' native dependency/bootstrap scripts
still require Linux/WSL; their Windows wrappers call WSL as well. No Linux
installation, Windows feature change, system PATH or driver change was performed.

Resume:

```powershell
& tools/external-homebrew/Setup-GearsPython.ps1
& tools/external-homebrew/Build-GearsCompiler.ps1 -BuildSeconds 900
```

Each build is bounded, uses a mutex to prevent a duplicate launcher, and records
`build-report.json`. `BoundedIncomplete=true` means an intentional time boundary;
Ninja's completed outputs are retained for the next batch. A compilation error
is recorded separately and fails the command. The runner terminates only its own
live build process tree at the boundary. Do not treat completed object counts as
compiler, emulator or game readiness.

Next: finish the compiler and verify the actual Gears GLSL pipeline produces a
GFX1013 PAL ELF with expected notes and no relocations; then address the native
foundation/host-test Linux path before packaging or emulator launch.


The corrected build was rechecked in a fresh 180-second batch: 367/2446 build
steps completed before the owned timeout, with no compilation failure. These
are build steps, not a completion percentage. The original failing `PDB.cpp`
was then compiled directly and passed with DIA disabled
(`build-dia-regression-report.json`). Ninja's dependency query for `APInt.cpp`
now records 122 actual headers, including `llvm/ADT/APInt.h`
(`ninja-header-dependency-proof.txt`), confirming the locale correction on the
real compiler path. `build-report.json` still says `BuildComplete=false` and
`BoundedIncomplete=true`. No owned build process is left running at this
checkpoint. Continue with the bounded command above.
