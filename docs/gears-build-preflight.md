# Gears build dependency checkpoint — 2026-10-13

The shared-page producer scene now passes independently changing left/right
regions, reverse order and missing-right controls. Further variations should
serve a concrete failure. Resume the larger independent Gears scene instead.
Gears is now built/packaged on Windows using actual GFX1013 PAL shaders;
it now executes its startup code in TouchePX5 but has not rendered a frame.
The build scripts and checkpoint below retain
the failed intermediate steps as provenance, not current blockers.

## Latest completed build

The final LLPC/llvm-readelf/llvm-objcopy build completes with exit 0 in 224.5 s.
The remaining Windows configuration issues were LLPC's flat `vulkan.h` include,
PAL's C++ standard macro check and POSIX `strcasecmp`. The wrapper adds the
Vulkan header subdirectory and scoped MSVC `/Zc:__cplusplus`/`_stricmp` mapping;
LLVM objects unrelated to those consumers and all upstream source pins are kept.
`vkgcUtil.cpp`, previously failing, compiles in the successful full target build.

`Build-GearsShadersWindows.ps1` uses that compiler and its own readelf/objcopy
to compile the actual upstream pipe and inspect machine flags 0x42, PAL metadata,
absence of relocations and symbol extents/hashes. The actual GS is 384 bytes,
PS 160 bytes. The real metadata generator then produces the AGC header.
`Build-GearsNativeWindows.ps1` compiles the previously pending main/assembly,
links the real native source units/import stubs, converts and packages the title.
Only assembly input paths are overlaid; no shader payload or metadata is invented.
The converter overlay described below is used, and SELF integrity passes.

```powershell
& tools/external-homebrew/Build-GearsShadersWindows.ps1
& tools/external-homebrew/Build-GearsNativeWindows.ps1
```

Artifacts: `gears-toolchain/gears-native-windows/shader-report.json`,
`native-report.json`, logs and `app/PPSA99997/eboot.bin`. Native ELF SHA256:
`FCFC7A3554524F571AEF32D069073B48DD70A199AA99C4F32750F14946FD78D3`;
SELF SHA256: `1953898EAF1D49CC5CA369EA18D831ACAECD8F4C7496F92BEADC82E360013862`.
The subsequent full host gate and bounded bootstrap are recorded below.
Do not promote build/metadata checks to game or renderer compatibility.

## Complete host gate and first native boot

`Setup-GearsCygwin.ps1` isolates the official setup/tools under artifacts, checks
the published installer SHA512, keeps signed package checks, and avoids elevation,
global PATH/shortcuts/installation-root registry changes. `Test-GearsCygwin.ps1`
forces all 27 C tests to rebuild with the upstream flags and POSIX.1-2008 feature
selection; this exposes `mkstemp` under strict C11 without suppressing warnings.
Unchanged `make test` and `make audit` both exit 0: 27 C tests, four Python
contracts, 122 allowlisted files. Cygwin Python uses the isolated PyYAML 6.0.3
pure implementation. This validates Cygwin POSIX, not a Linux host.

```powershell
& tools/external-homebrew/Setup-GearsCygwin.ps1
& tools/external-homebrew/Test-GearsCygwin.ps1
& tools/external-homebrew/Invoke-GearsNative.ps1
```

The bounded launcher retains the app/runtime hashes, source state and logs, and
terminates only its owned process tree at the deadline. The original title and
companion runtime execute; first boot stops at missing internal sysmodule-load
NID `39iV5E1HoCk`. The two internal no-argument load/unload exports now share the
existing HLE registry lifecycle; the no-argument ABI ignores scratch R8 and
does not imply loading Sony firmware. One dispatch/lifecycle/memory regression
and full 1276-test Release library suite pass, with a clean CLI build.

The next boot (`gears-native-windows/runs/20261013-111806-723`) passes that call
but rejects the title's two-argument call to public `sceAgcInit` NID
`kW3GLb7QfPg`. OpenAGC reference commit
`3c7e8072f0d7684e930454794fd5c02056946d73`, `src/game_compat.c` lines 588-621,
documents public `(revision)` and internal `(state, revision)` variants; current
HLE tests preserve that distinction. Next build a separate, provenance-recorded
title initializer ABI variant and preserve this original failure. No frame
or hardware-rendering result is claimed; do not loosen public HLE validation.

Official environment documentation: https://cygwin.com/install.html and
https://cygwin.com/sha512.sum. Exact installed package database and logs are in
`gears-toolchain/cygwin-host`; host evidence is `tests/20261013-111437-196`.

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
The GLSL/SPIRV dependencies now come from the pinned spvgen's own `CHANGES`:
glslang `1b65bd602b23d401d1c4c86dfa90a36a52c66294`, SPIRV-tools
`ce37fd67f83cd1e8793b988d2e4126bbf72b19dd`, SPIRV-headers
`e7294a8ebed84f8c5bd3686c68dbe12a4e65b644`, and SPIRV-cross
`6173e24b31f09a0c3217103a130e74c4ddec14a6`. Short upstream pins were resolved
through each official commit API. The fetch helper checks the lock against
`CHANGES`; llvm-dialects still uses LLPC's exact gitlink. The earlier AMDVLK
tools manifest supplied 2022 dependencies incompatible with the 2025 spvgen:
the real build failed on missing EXT mesh resource fields and stage enums.
Those clean old checkouts are preserved under `dependency-history/amdvlk-2022`;
the failed batch/report remains available. Compiler sources are not patched
to remove supported stages.
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

The following 900-second batch reached 830/2078 steps, and the next reached
493/1248, both with no compilation failure before their owned timeouts.
These counts are not completion percentages. The runner now preserves the
preceding completed log/report under `build-history` before starting a new batch.
Always inspect `active-build.json`, `build-report.json` and the retained log for
the latest state. If an owned build is active, do not start a duplicate launcher.

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

## Complete native foundation example on Windows

`Build-GearsFoundationDemoWindows.ps1` compiles the four original example/CRT
units, links the SDK imports, converts the native executable and packs an app
folder with the verified runtime. It also exposed a real converter assumption:
LLD omits unused `.data.rel.ro`, but the converter required that section even
when `.got` is the actual start of RELRO. An isolated GPL-3.0-or-later converter
overlay now selects the stored section at the already-computed RELRO start.
The existing start-equality and mapped LOAD congruence checks remain intact;
upstream checkout/linker script are unchanged, and no artificial section is added.

The original converter still reproduces the missing-section failure on the
same real PIE input. The repaired converter produces identical native ELF and
SELF outputs in two conversions; both container integrity inspections pass.
`foundation-demo-windows/report.json` records inputs/overlay/tool/output hashes
and the app path. The example has been built and packaged, **not executed**.
This repairs a native build prerequisite; it is not a Gears or GPU result.

```powershell
& tools/external-homebrew/Build-GearsFoundationDemoWindows.ps1
```
