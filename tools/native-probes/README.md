# Native GPU checks

Run from the source checkout:

```powershell
.\tools\native-probes\Test-TouchePX5-NativeGpu.ps1
```

The runner uses the existing Release executable and installed LLVM tools; it does not install
anything or rebuild. Reports are saved under `artifacts/native-gpu/<timestamp>/report.json`,
with the source revision, executable hash, individual results and copied guest logs.

- Compute: a synthetic gfx10 program runs twice on Vulkan with different inputs and reused
  memory. Checks FMA, signed multiplication, EXEC masking, ordered completion and CPU readback.
- Raster: synthetic gfx10 vertex/pixel programs draw a triangle, then redraw the same target
  through the same shader with changed scalar inputs: red becomes blue. Both Vulkan captures
  are checked for color, shape and area. The checker requires exact interior/exterior colors
  outside a two-source-pixel band accounting for bilinear presentation scaling. Every pixel
  is checked for valid color channels. Blank and wrong-color negative controls must fail.

Verified on GTX 1060 3 GB at 1920x1061: 2,037,120 pixels per raster capture, 563,461 fully
colored triangle pixels in each. These are controlled compute/raster tests, not commercial
game compatibility. Depth, textures and more complex geometry remain
separate next steps.

Raster outputs: `artifacts/smoke-triangle/triangle-red.png`, `triangle-blue.png`,
`triangle.log` and `verification.json`; unique raw capture directories are retained too.
Compute log: `artifacts/smoke-compute/compute.log`.

The initial triangle fixture omitted PS input masks and shader user-SGPR resource registers.
The corrected fixture supplies those registers explicitly. No emulator implementation change
was required to pass these GPU probes.

These files are kept here because Windows denied writes to the original OneDrive fixture
folder. Security settings were not changed. The old OneDrive triangle script remains an
unfinished earlier version; use these runners.
## Indexed and clipped geometry

`Test-TouchePX5-Triangle.ps1 -Scissor` intersects raster coverage with [80,240) x [45,135)
while retaining an explicit full-surface viewport. `-IndexBits 16` or `-IndexBits 32` uses
indices 0,1,3 to draw the mirrored triangle, which cannot pass if the index buffer is ignored.
The second indexed draw first sets a deliberately wrong INDEX_BASE and requires the inline
DRAW_INDEX_2 base to replace it. The combined GPU runner covers ordinary and clipped draws,
16-bit indices and 32-bit indices with clipping.

The indexed probe failed before the DRAW_INDEX_2 fix: the parser read the draw count but
ignored the inline index base and maximum count. The corrected parser validates these fields
before updating queue state. The 16-bit real-GPU probe then passed for both colors; all 1,225
unit tests also passed after rebuilding. Full combined-run results live in the timestamped report.

`-Blend` draws half-transparent blue over opaque red with separate alpha factors. The second
capture must be purple (red/blue 127 or 128, alpha 255 in the interior); the old red target
contents must survive for blending to succeed. The checker rejects incorrect channels,
coverage and area, and permits only the narrow filtered edge band. This passed on the real
Vulkan device. The combined runner includes this as its sixth case.
