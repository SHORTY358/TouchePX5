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
Only CPU-authored linear non-array sampled mip chains are added.

## sRGB follow-up

Unified format 182 (BC7 sRGB, NumberType 9) now uses the same full-chain storage
reader. The unchanged native guest first failed its red-base pixel oracle on the
previous runtime; `artifacts/bc7-srgb-before/report.json` retains the failure,
guest/runtime hashes and original capture hashes. After the fix the same guest
hash passes `Test-TouchePX5-Triangle.ps1 -MipBc7 -MipSrgb`, recorded in
`artifacts/smoke-triangle-texture-select-mip-srgb-bc7/verification.json`.

Encoded RGB endpoints 128/128/64 must decode to approximately 55/55/13 on the
UNORM output, while alpha remains exactly 128/128/64. The three native captures
pass independent whole-frame coverage/color/alpha checks. A non-decoding color
oracle, an opaque-alpha oracle with the correct RGB decoding, a blank image and
the wrong expected color all fail. This distinguishes color-space behavior from
mere upload success. One cache hit and two full-chain uploads are required.
BC7 UNORM and RGBA8 sRGB native regressions pass; library suite: 1416/1416.

Tiled/array/storage/GPU-written mip chains and non-mode-6 numerical decoding
fixtures remain outside this evidence.

## Fractional LOD and trilinear filtering

`Test-TouchePX5-Triangle.ps1 -MipBc7 -MipSrgb -MipTrilinear` selects linear
minification/magnification and mip interpolation. The first draw samples LOD0;
the next two use LOD0.5 before/after rewriting the lower level. Independent RGB
expectations decode each endpoint before interpolation and quantize only the
final RGBA8 UNORM result. Expected RGBA is approximately (55,0,0,128),
(28,0,28,128), then (34,0,0,96). The existing Vulkan sampler path already supports
this behavior; no runtime change was needed after the sRGB-chain fix.

`artifacts/smoke-triangle-texture-select-mip-srgb-bc7-trilinear/verification.json`
passes three 1920x1061 native captures, one cache hit and two uploads. Negative
controls reject filtering encoded RGB before decoding, either nearest mip,
stale lower-mip content, opaque alpha, missing RGB decoding and blank frames.
BC7 sRGB nearest-mip regression also passes with the extended runner.
The new case is included in the native GPU aggregate.

See the primary [Khronos sampling specification](https://github.com/KhronosGroup/Vulkan-Docs/blob/main/chapters/textures.adoc)
for mip weights and filtering. Constant-color blocks isolate inter-level
filtering; this does not independently certify varying texel interpolation,
implicit derivative LOD, anisotropy or other BC7 block modes.

## BC1/BC3 sRGB follow-up

The same omission affected unified formats 170 (BC1 sRGB, 8-byte blocks) and
174 (BC3 sRGB, 16-byte blocks). Both now admit NumberType 9 in the bounded
CPU-authored linear sampled-chain path. Before-change failures and hashes are
retained in `artifacts/bc1-srgb-before/report.json` and
`artifacts/bc3-srgb-before/report.json`; guest hashes match the passing inputs.

`Test-TouchePX5-Triangle.ps1 -MipBc1 -MipSrgb` and `-MipBc3 -MipSrgb` both pass
three 1920x1061 captures. RGB565 endpoint fields 16 and 8 expand to encoded
132 and 66, so decoded expected channels on the UNORM target are 59/59/14.
BC1 alpha stays 255; BC3 alpha is independently checked at 128/128/64.
Both reject blank/wrong-color/non-decoding expectations; BC3 also rejects an
opaque-alpha expectation with correct RGB decoding. One unchanged reuse and
two uploads are required. Native BC3 UNORM regression passes.

Reports are under `artifacts/smoke-triangle-texture-select-mip-srgb-bc1` and
`artifacts/smoke-triangle-texture-select-mip-srgb-bc3`. Odd block extents,
truncation and wide 16-byte-block rows are covered; library suite 1419/1419.
These fixed endpoint-zero blocks do not certify every BC1/BC3 selector or alpha
mode. Storage, array, tiled and GPU-authored chains remain outside this addition.
