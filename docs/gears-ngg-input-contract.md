# Gears NGG input audit — 2026-10-14

Current output is black despite valid mesh coordinates and successful shader
translation. This file records source evidence and gates for the next change;
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

## Independent ownership failure

Boot `20261014-004305-347` decodes frame 4's token while awaiting frame 3.
The current display event queue coalesces same-ident/filter records. Compare the
real API contract before modifying it or the consumer; exact-token assertions
must remain, and retained buffers must not be reused after ambiguous completion.
