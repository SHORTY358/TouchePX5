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
the audit checked 122 allowlisted files. A later native Windows C run passed
26/27 tests, using the upstream Makefile's source lists and LLVM-Mingw with
`-std=c11 -O2 -Wall -Wextra -Werror -static`. `test_ps5log_host` requires POSIX
sockets/poll/fcntl and remains unexecuted; `gears-host-windows/report.json` records
that omission. This is not complete `make test` validation. The original native
dependency/bootstrap scripts require Linux/WSL, but the separate Windows path
below now builds the real foundation runtime without those scripts. No Linux
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
GFX1013 PAL ELF with expected notes and no relocations; then generate actual
metadata, compile main/shader assets and link/package the native title. Complete
the remaining POSIX host test before claiming the upstream test gate or launch.


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

The following 900-second batch reached 830/2078 steps with no compilation
failure before its owned timeout. Another bounded batch is in progress; its
live count is not a completion percentage. Always inspect `build-report.json`
and the retained log for the latest result.

## Windows native foundation and cross-compiler evidence

`Build-GearsFoundationWindows.ps1` builds upstream host generators directly,
without editing their pinned GPL-3.0-or-later sources. LLVM-Mingw Clang 23.1.0
and static zlib 1.3.2 produce two raw runtime ELFs and two signed PRX containers:

- Raw ELF SHA256: `8EE6E124993E1AF26420CB455890FD002F5D6C7E78883C860CE45734E7D002BB`.
- Signed runtime SHA256: `E6FF45D16ADF687855CC3B33B0C8A4132B6504360B221E0A34C7E99FB3BA0036`, 1,284,674 bytes.

Both digests match the pinned foundation's published contract exactly.
`runtime-windows/report.json` records generator/compiler/tool/zlib provenance.
This proves runtime construction, not execution or Gears compatibility.
Zlib's official fossil archive is checked against the foundation's expected
SHA256 `BB329A0A2CD0274D05519D61C667C062E06990D72E125EE2DFA8DE64F0119D16`.

The foundation's exact payload SDK v0.42 is separately cached, SHA256
`8CFBC7CD5811E719EB4F0C47EEA668D3DC7B40BC8AB11C4A5031D40C23EC02DA`.
`Test-GearsWindowsCrossCompile.ps1` checks all extracted files against this zip,
then compiles 24 source units from the real native build plus two network units,
two import stub units and the foundation startup unit. All 29 are inspected as
x86-64 ELF relocatable objects. The two AGC import stubs also link as ELF shared
objects with the expected SONAMEs. Both Clang 23.1.0 and the upstream baseline
Clang 18.1.8 passed. Separate reports/logs are retained in `cross-compiler-probe`
and `cross-compiler-clang18`; the default runner now uses Clang 18. No fake
metadata or shader bytes are introduced;
`native/main.c` and `native/shader_assets.S` wait for actual LLPC output.

```powershell
& tools/external-homebrew/Build-GearsFoundationWindows.ps1
& tools/external-homebrew/Get-GearsClang18.ps1
& tools/external-homebrew/Test-GearsWindowsCrossCompile.ps1
```

Clang 18 comes from the official LLVM-Mingw 20240619 Windows UCRT x86-64 release,
cached only under artifacts. Its 142,797,010-byte archive has observed SHA256
`810703594A7E3EEA03385B5329C7EA3BD65F5E496B44CF1B68C17FF436D265E7`.
The helper verifies the archive and extracted compiler/version and records
`clang18-release-report.json`. This is a recorded release download digest,
not a claimed upstream signature. No global install or PATH modification occurs.

## Emulator imports identified during native preparation

The upstream native adapter calls two driver wait exports absent from our HLE:
`sceAgcDriverGetWaitRenderingPacketSizeInDwords` and
`sceAgcDriverWaitUntilSafeForRendering`. They are now implemented with NIDs
`0MtUJ3BpGhE` and `u8BkdHb1+Po`. The size is seven dwords for our existing
ordered flip-wait encoding, not a asserted Sony hardware packet size. The emitter
accepts the upstream cursor-pointer ABI, reserved=0, non-negative handle/index,
and sufficient capacity, and advances the full 64-bit cursor only after writing
the complete packet. Size queries/emission enqueue no GPU work themselves;
the existing command decoder queues the wait when submitted.

The new eleven regression cases and full 1275-test library suite pass in Release.
The Windows CLI also builds. This does not demonstrate driver waits in Gears,
which still lacks compiled shaders/main and has not launched.
