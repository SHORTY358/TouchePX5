# CPU-authored linear BC7 mip chains — 2026-10-19

The format was already recognized by the texture descriptor and Vulkan backend,
but the full CPU mip-chain path admitted only RGBA8, BC1 and BC3. An unchanged
native BC7 fixture sampled the lower blue level as its base instead of the red
base level. Before-change runtime/input/capture hashes and log are retained in
`artifacts/bc7-before/report.json`. The guest ELF hash is identical before/after.

The full-chain reader now admits unified format 181 (BC7 UNORM). It retains the
existing GFX10 reverse mip storage, ceiling-rounded compressed storage extents,
256-byte block-row alignment and texel-unit Vulkan copy row lengths. BC7 uses
16-byte 4x4 blocks: see [Vulkan formats](https://docs.vulkan.org/spec/latest/chapters/formats.html).
No CPU BC7 decoder is introduced; Vulkan performs decompression and sampling.

`Test-TouchePX5-Triangle.ps1 -MipBc7` builds a native producer, samples base LOD0,
samples LOD1, then rewrites only the lower block and samples it again. Its
independently expected colors/alpha are red 254/128, blue 254/128 and red 254/64.
The fixture uses BC7 mode 6, identical seven-bit endpoints with zero p-bits and
indices; exact bit allocation follows [Microsoft's mode reference](https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format-mode-reference#mode-6).
This explains the even endpoint value 254 rather than RGB565's 255.

`artifacts/smoke-triangle-texture-select-mip-bc7/verification.json` passes three
1920x1061 captures, including geometry/color/alpha checks, blank/wrong-color/
opaque-alpha negative controls, one exact-content cache hit and two uploads.
The existing BC3 native scenario also passes. One intervening BC7 attempt had
only two captures because the native window became unavailable/minimized; it
was not accepted as image verification. The subsequent complete run passes.

Unit cases cover odd block dimensions, truncated chains publishing no partial
result, snapshot ownership and rows wider than 256 bytes. Library suite: 1414.
Only CPU-authored linear non-array sampled mip chains are added. BC7 sRGB,
tiled/array/storage/GPU-written mip chains and non-mode-6 numerical decoding
fixtures remain outside this evidence.
