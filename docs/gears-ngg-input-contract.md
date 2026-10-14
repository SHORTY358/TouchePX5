# Gears NGG input audit — 2026-10-14

Current Gears output is black despite valid mesh coordinates; its original
graphics workgroup barrier is explicitly rejected. This file records source evidence and gates for the next change;
it does not certify NGG execution.

## Evidence

- Pinned LLPC `lgc/lowering/ShaderMerger.cpp` GFX10 special-input map:
  s0/s1 user-data table, s2 merged group info, s3 merged wave info,
  s4 off-chip LDS base, s5 scratch offset, s6/s7 GS address. User data follows.
- `lgc/lowering/NggPrimShader.cpp` describes vertex/primitive subgroup/wave
  counts, relative thread IDs and conditions guarding ES/GS/exports. Not every
  NGG mode is equivalent to an ordinary independent Vulkan vertex invocation.
- Actual GS code begins `BEFE03C1,9383FF03,00040018,9386FF02,00090016,
  9381FF02,0009000C`: it consumes s3 and s2 before guarded vertex/position work.
- Existing evaluation starts scalar registers at zero, fills s8+ user data,
  and Vulkan initializes only vertex v5 and instance v8. Merged inputs are absent.
- Position export target 12 is recognized; target 20 is a primitive export and
  must not be mistaken for another position. Gears also exports parameters 32/33.

## Implementation gates

GFX10 compiler fields now verified from `NggPrimShader::initWaveThreadInfo`:
group primitives s2[30:22], group vertices s2[20:12], ordered wave ID s2[11:0];
wave vertices s3[7:0], wave primitives s3[15:8], group-relative wave ID s3[27:24].
Thread ID is wave ID times guest wave size plus mbcnt-derived lane ID. Gears GS
is wave32/WGP; the pixel shader is separately wave64.

The previous translated vertex SPIR-V is invalid: actual captured barrier at
byte 15128 uses execution scope Workgroup (2), memory scope Workgroup (2),
semantics 0x108. [Vulkan rule 04682](https://github.khronos.org/Vulkan-Site/spec/latest/appendices/spirvenv.html)
requires Subgroup execution scope for vertex/fragment barriers. Current runtime
rejects this unsupported graphics workgroup barrier explicitly; compute remains
unchanged. This does not implement NGG, and replacing the scope with Subgroup or
removing the barrier would not supply missing cross-invocation semantics.

A compute-expansion path is a candidate because it can represent group sharing,
guest wave/lane identity and workgroup barriers. It must output explicit vertex
and primitive data for a subsequent graphics draw, with real synchronization and
resource lifetime. Prototype on an independent group before routing Gears.

1. Determine the exact bit layout from this pinned compiler's extraction sites
   and cross-check hardware ABI. Retain compiler revision and actual shader words.
2. Identify the validated no-culling/no-GS-amplification Gears path explicitly.
   Reject unsupported topology/stage modes rather than silently generalize.
3. Define bounded group partitioning from actual source vertex count and topology;
   map relative wave/group IDs and source vertex/instance IDs without losing
   triangle boundaries. Handle tails and draws spanning several groups.
4. Separate hardware system inputs from guest user-data s8+ and runtime scalar
   buffers. Include derived configuration in shader/cache inputs where needed.
5. Validate guest EXEC masks, mbcnt lane numbering, branching and barriers in the
   existing Vulkan vertex mapping. If independent invocations cannot reproduce
   the required sharing, choose a stage/lowering that can; do not bypass guards.
6. Test full and tail groups, one/multiple waves, source index preservation and
   unchanged non-NGG behavior. Verify actual translated results, not only decode.
7. Repeat the same actual Gears shader/title pixel captures and independently
   inspect geometry, depth, color, animation and ownership. Successful submission
   alone remains insufficient.

## Verified compute foundation

The independent native `compute-ngg-group-foundation.c` probe now passes on the
actual GPU: two guest wave32 groups within one 64-invocation compute workgroup,
live tails 0/13/32/33/63/64, exact local IDs and active flags, shared paired LDS
and two barriers, per-record/trailing guards. Twelve numerical dispatches pass
without CPU replacement. An intentionally all-active predicate fails at native
return 150. Existing paired-LDS baseline still passes.

This does not feed NGG group system inputs or execute original Gears shaders.
The next bridge must write bounded position/parameter outputs, preserve source
vertex/primitive identity and tails, and synchronize that buffer before graphics
consumption. Evidence is in `artifacts/smoke-compute-bounded-atomics-lds-paired-ngg-foundation`.

## Verified output bridge

`Test-ComputeVertexBridge.ps1` now builds/runs an independent guest with a 64-local
wave32 producer and three live position records. GPU output is numerically checked,
trailing guards stay intact, and a separate vertex shader fetches the produced
buffer for a real triangle draw. Actual final swapchain pixels pass an analytic
interior/exterior oracle; the image was also visually inspected. Omitting the
producer intentionally fails native output checking (66), so zero/untouched
buffers are not accepted. Source fixture is `compute-vertex-bridge.c`.

The optional `-ColorParameters` variant now carries position plus param0 (eight
words, stride 32). Compute output words and guards pass native checks; actual
fragment interpolation passes an independent barycentric RGB pixel oracle and
visual inspection. Deliberately swapped parameter export channels fail the pixel
oracle while buffer checks still pass. Position-only mode remains validated.

The optional `-Connectivity` variant now produces separate GPU index words
0,1,3 alongside four position/parameter records. A decoy at record 2 makes ignoring
the generated topology visibly wrong. Actual indexed draw trace, consumed index
bytes, native buffer/guard checks and analytic interpolated pixels all pass.
Deliberately consuming 0,1,2 fails the pixel oracle despite correct GPU outputs.

The transport foundation is complete for this independent triangle. Next work
must address original NGG compute-expansion translation and system inputs, not
more transport variants without a failure to resolve. Gears and its retirement
remain unverified; original NGG primitive export is not yet decoded into this
host index output, and culling/amplification modes are not implemented.

## Explicit translator export transport

Optional `Gen5ComputeExportOutput` now maps raw position export 12 and parameter
exports 32+ to a bounded existing writable output binding. Logical record index
comes from a 1D workgroup/local ID; guest EXEC, logical capacity, group overflow
bound and actual array length all guard writes. Disabled components stay intact.
Compressed exports fail explicitly; primitive exports require the separate
explicit triangle transport below. Ordinary compute exports
without this transport fail instead of disappearing. Local Y/Z must be one;
nonzero dispatched group Y/Z rows are suppressed to avoid output aliasing.

The lowering now passes actual GTX 1060 Vulkan execution through
`Test-ComputeExports.ps1`: all 40 words match, including disabled parameter
channels, inactive EXEC, zero enable mask and 16 tail guards. Two 64-local groups
exercise a three-record capacity. Deliberately compiling capacity four against
the same expected three-record output fails at exactly six guard words.
Original arithmetic GPU conformance remains passing. Evidence:
`artifacts/compute-export-gpu/20261014-132943-696/execution-report.json`.
Input is synthetic IR through the real translator, not original Gears or guest
EXP decoding. Next add primitive decoding and the actual NGG system-input/
dispatch adapter. Prior transport pixels do not validate original Gears.

## Triangle primitive decoding contract

Pinned LLPC `23a0757d922da7eb06c6a626d48553e0fb99fde0`,
`lgc/lowering/NggPrimShader.cpp:3290-3340` and `:3380-3390`, documents pre-GFX12
connectivity: index0 bits 8:0, index1 18:10, index2 28:20, null bit 31.
Indices have **nine** bits, separated by ten-bit strides. Host reference decoder
`Gen5TriangleConnectivity` retains the packed word and does not interpret gap
bits. Consumption requires a non-null primitive and all three indices below the
actual live subgroup vertex count (1..512); degenerate triangles are preserved.
Seven regression cases pass as part of the 1311-test library suite.

`Gen5ComputeTriangleOutput` now writes explicit five-word primitive records on
GPU: packed word, three decoded subgroup-relative indices and a consumable flag.
The caller supplies live vertex count (uniform across these dispatch groups),
record capacity, binding and first-word offset. Null or out-of-range indices
clear validity, without erasing the packed source. Mask zero/EXEC suppress writes;
non-source0/compressed forms fail. Logical/tail/array bounds protect stores;
overlap with position records and invalid backing/configuration fail before
shader generation. Original EXP target 20 is still unsupported without this
explicit configuration.

The consumer must clear validity words for a new dispatch and accept only
flag == 1 after producer completion. EXEC/zero-mask suppression preserves old
storage, so treating arbitrary nonzero words or stale flags as valid is unsafe.

Actual GTX 1060 evidence is in
`artifacts/compute-export-gpu/20261014-134326-020/execution-report.json`:
normal/null/out-of-range/gap-bit cases pass; permitting one extra primitive
record intentionally causes five tail mismatches. Two hand-assembled guest word
cases pass actual decoder -> translator -> Vulkan dispatch, including null and
EXEC/zero-mask suppression. A 67-record case verifies separate records in two
workgroups with all 887 words and guards checked. Original arithmetic baseline
passes and the full library suite is 1327 tests.

This contract applies to explicitly selected triangle topology only. It does not
establish line/point decoding, edge flags, GS amplification, compaction or global
vertex mapping. This output is not connected to Gears or to the indexed draw
bridge. Next establish real merged inputs/source IDs and group ownership/
remapping; varying per-group live counts require a dispatch adapter.

## Merged count packing reference

`Gen5NggMergedGroup` now implements only the fields decoded by the pinned
`NggPrimShader::initWaveThreadInfo` (lines 2866-2907). Caller-supplied subgroup
counts, full-wave thread extent, wave32/64 and ordered wave ID are checked before
packing. The subgroup counts are nine bits (maximum 511, not 512); ordered ID
is twelve bits and wave ID four. Per-wave vertex/primitive tails are separate.
Six wave32 live-count cases (0/13/32/33/63/64), wave64/max-field checks and
invalid-layout cases pass in the 1343-test library suite.

Optional Vulkan compute `nggCountInputs` now injects those count fields into
runtime s2/s3 after generic initialization. s3 wave ID and independent tails
derive from local X. Thread extent/wave layout must match the compile layout;
triangle output's live count must agree. User data s8+ is preserved. Counts and
ordered ID are uniform caller-provided values for every workgroup; this is not
an actual draw scheduler or variable per-group descriptor mechanism.

`Test-NggCountInputs.ps1` now passes guest decode -> translator -> GTX 1060
reads for all six wave32 tails, wave64 128-thread/67-vertex/65-primitive tails,
and 512-thread/511-vertex groups (all 16 wave IDs). Every invocation's count
words, s8 and local source ID plus guards are checked. Omitting injection fails
128 expected values. Evidence:
`artifacts/ngg-count-inputs/20261014-142017-491/execution-report.json`.
Full library suite is 1349 tests; CLI builds cleanly.

Source IDs, per-workgroup dispatch/ordered ownership and host vertex bases
remain unimplemented. This is not a complete merged shader ABI: scratch, ring
state and other fields/modes require their own contract. Next validate actual
source reads and remapped connectivity before wiring original Gears.

## Independent ownership failure

Boot `20261014-004305-347` decodes frame 4's token while awaiting frame 3.
The current display event queue coalesces same-ident/filter records. Compare the
real API contract before modifying it or the consumer; exact-token assertions
must remain, and retained buffers must not be reused after ambiguous completion.
