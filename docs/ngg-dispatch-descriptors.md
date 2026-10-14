# Explicit passthrough subgroup transport plan

This is a TouchePX5 host transport layout, **not a Sony/AMD hardware descriptor**.
`Gen5NggPassthroughBatch` packs caller-supplied no-API-GS passthrough groups with
a common wave size and full-wave thread extent. Original Gears mode is proven
in `gears-ngg-input-contract.md`; that does not establish full game execution.

Each group has four uint descriptor words:

| Word | Meaning | Units |
| --- | --- | --- |
| 0 | Explicit merged GroupInfo count/ordered fields | GFX10 packed count fields |
| 1 | First source word in SourceWords | uint words, relative to source table |
| 2 | First reserved vertex output slot | records, not guest vertex IDs |
| 3 | First reserved primitive output slot | records |

Every group reserves ThreadCount source records of nine words and ThreadCount
output slots for each output stream. Short/empty groups retain padding; no
compaction or topology renumbering occurs. Group g has source offset
g * ThreadCount * 9 and output slot base g * ThreadCount. Source vertex/instance
IDs and primitive IDs remain explicit caller data, with local connectivity
checked against the group's live vertex count. Ordered IDs are caller supplied,
not derived from group index and not a hardware scheduling implementation.
Arrays are owned snapshots; subsequent caller source-array edits do not change
the transport plan. Arithmetic/allocation size calculations are checked.

Current host checks cover different live counts and source IDs in two groups,
independent source/output bases, topology remapping, empty groups, execution
layout mismatches and caller-array ownership. The optional Vulkan compute
`nggBatchInputs` mode now loads these canonical descriptors. Its older single
source mode still accepts only subgroup (0,0,0). The two input modes are exclusive.

GPU descriptor loading checks actual table/source/output lengths and count
bounds uniformly for each entire group; bad groups select the default exit
before guest blocks/barriers. Descriptor offsets/bases must match canonical
reserved ranges. Cached vertex counts govern primitive validity even when the
guest changes s2. Triangle LiveVertexCount=0 selects descriptor-derived counts.
Dispatch limits must cover complete padded groups; partial groups reject.

Independent GTX 1060 proof is in
`artifacts/ngg-source-inputs/20261014-165927-675/execution-report.json`: different
group identities/counts/ordered values/topology, raw export output and a barrier
pass. Bad source/output offsets, oversized counts, truncated sources and partial
dispatch limits preserve rejected output slots. The standalone checker now
initializes the required 12-byte dispatch-limit push constants. Library suite:
1388 tests. No original Gears shader or graphics draw was executed by this audit.

Completed canonical transport gates and remaining integration:

1. Add explicit retained descriptor/source bindings and bounded group count;
   reject overlap with writable outputs and scalar-register input storage.
2. Check descriptor extent uniformly before reading any fields. Reject entire
   invalid groups through the dispatcher's default exit, before barriers.
3. Validate the group's counts against common ThreadCount, source extent and
   reserved output ranges; check actual array lengths and overflow on GPU.
4. Inject s2/s3 from that group's counts/ordered field and dynamic local wave
   ID; load v0..v8 from its source offset. Preserve s8+ user data.
5. Use descriptor output bases for position/parameter and primitive records;
   validity derives from that group's actual live vertex count.
6. Verify two groups with distinct topology, IDs, counts and ordered fields on
   real GPU. Missing/truncated/overlapping descriptors must fail or reject
   whole groups without writes, stale validity acceptance or barrier hangs.
The independent audit passes gates 1-6 for this canonical allocation. Next run
the original retained Gears shader/data through the compute path and inspect
numeric outputs before the graphics bridge; fail loudly on unsupported behavior.

7. Establish producer completion and descriptor ownership before consuming
   remapped indices. Only then combine with the indexed graphics bridge and
   original Gears shader; do not substitute host-produced scene geometry.
