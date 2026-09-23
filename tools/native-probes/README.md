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
game compatibility. Depth, textures, clipping, blending and more complex geometry remain
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