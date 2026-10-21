# Reachable shader exits after s_endpgm — 2026-10-21

The unchanged Xash3D `bsp_textured` pixel stage contains a conditional branch at
0x84 to an exit at 0x12C, after a different path's terminal at 0x128. The old
linear decoder returned success after that first terminal, silently dropping
16 original bytes. The source-derived audit now retains all 316 bytes and both
exits. See `docs/xash3d-preflight.md` and its hash-pinned before/after evidence.

`Gen5ShaderTranslator.TryDecodeProgram` now follows direct scalar branch targets
using the shared IR branch resolver. Conditional branches retain both paths;
unconditional branches stop fallthrough; terminals stop only their own path.
An address worklist and visited instructions bound repeated/back-edge traversal.
Output remains sorted by original PC, with existing flat-address inference
applied in that order. The 16384-instruction bound and its existing `unterminated`
error contract are retained. Failure publishes no partial program.

Instruction-word ownership rejects entry into a literal/second instruction word
and overlapping interpretations. Negative targets, address overflow and unreadable
reachable blocks fail; unreferenced bytes after a terminal are not read. This is
direct control-flow discovery, not support for arbitrary indirect shader calls.

Eight focused cases cover two exits, unreachable poison data, a literal-word
target, a back-edge with an exit, a missing reachable exit, a negative target,
unreferenced padding and an endless branch. Six fail before this change; all
eight pass afterward. Shader suite: 60/60; library suite: 1419/1419.

`Test-TouchePX5-Triangle.ps1 -LatePixelExit` executes a native guest with a pixel
shader whose scalar condition selects either a red path or a blue path after
the first terminal. Both actual 1920x1061 captures pass coverage/color checks;
the runner rejects a shader-color-clear shortcut and retains source/runtime/
guest/capture hashes. Report:
`artifacts/smoke-triangle-late-pixel-exit/verification.json`.

Existing native Gears frame 100 also passes its independent numerical and exact
presented-ticket interior-pixel audit at
`artifacts/gears-native-numerical/20261021-032913-622`: 0/102325 interior pixels
differ, maximum channel error 1; the corrupted-pixel control fails as expected.
This does not certify original Xash3D shader execution, fragment discard or title
startup. One VSTest process could not bind its local socket (10013); an immediate
unchanged repeat runs all shader tests successfully without OS changes.
