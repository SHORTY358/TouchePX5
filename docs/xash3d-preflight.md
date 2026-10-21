# Xash3D next-stage preflight — 2026-10-21

The standalone Gears source links to the independent
[PS5 Xash3D/Half-Life port](https://github.com/mpereiraesaa/ps5-xash3d-halflife).
The small top-level source checkout is retained under
`artifacts/external-homebrew/ps5-xash3d-halflife`, pinned to
`be0731b4fcc4233349bba5e0d3ff33c79428f695`. Its tracked sources remain clean.
Submodules are recorded but not initialized: Xash3D FWGS
`9aa39ad49b2ba07ffd945f93a19d31e11400b6cd` and hlsdk-portable
`e277ffaae85422cb674c8c19a2209a7fa85157db`. No game assets are supplied here.

The upstream README describes a native textured world, models, input, audio and
saves. Those are upstream claims about its PS5 port, not verified TouchePX5
compatibility. The current Gears-only native NGG gate deliberately rejects this
different shader pair and its sampled resources.

## Host build limitation

`tools/external-homebrew/Test-Xash3DHost.ps1` reuses the existing isolated Cygwin
toolchain; it installs nothing. The original `make test` fails compiling
`tests/test_ps5_memory_arena.c`: PE/COFF GCC supports static object alignment only
up to 8192 bytes while that test requires `_Alignas(16384)`. No alignment or
assertion was weakened. Full host checks and the subsequent audit have NOT
passed. Evidence: `artifacts/xash3d-host/20261019-121859-891`.
Use a suitable ELF/POSIX host for the unchanged full checks, or independently
review a portable aligned-allocation adaptation before claiming a host pass.

## Original textured shader available now

`tools/external-homebrew/Build-Xash3DTexturedShader.ps1` compiles unchanged
`shaders/bsp_textured.pipe` using the existing pinned LLPC/LLVM tools. It does
not invoke the native title build, SDK downloads or game-data packaging.
Evidence: `artifacts/xash3d-shaders/20261019-122045-187`.
The GFX1013 PAL stages have no relocations: GS 288 bytes, PS 316 bytes.
The build report hashes the source pipeline, compiler tools and manifest.

`SharpEmu.Tools.NativeShaderAudit` accepts that manifest, verifies the original
stage hashes and records decoded instructions/coverage without GPU execution.
It also retains concrete controls, register operands and opcode inventories for
the next input/binding investigation. A deliberately altered pixel-stage copy
is rejected by its manifest hash; original files remain unchanged.

This exposed a real decoder defect: the PS branch at byte 0x84 targets 0x12C,
after the first `s_endpgm` at 0x128. Earlier decode returned success after only
300/316 bytes, omitting the alpha-discard exit. After direct-branch traversal it
retains 68 instructions and all 316 bytes; GS retains all 56 instructions/288
bytes. `decode-before`, `decode-after` and `hash-negative` retain both outcomes.
This is decode coverage, not original shader execution or verified discard.

## Next concrete gates

1. Retain the shader fix and regress existing native Gears and branched pixels.
2. Construct a clearly labelled source-derived BSP input fixture: original GS,
   identity/nonidentity matrices, position/UV streams and independent GLSL export
   expectations. Do not label these inputs as captured title state.
3. Verify actual GPU NGG output/indices before adding a graphics consumer.
4. Supply original PS resource-table inputs, base texture and lightmap; compare
   actual pixels and alpha-discard behavior with an independent reference.
5. Only then consider a bounded native renderer package and full Xash3D startup.
   Native game boot, engine PRXs, real content and full host checks remain separate.

Keep the Gears shader gate in place; adding an unrelated hash without its input,
output and lifetime proof is not an implementation of this new pipeline.
