# Native GPU triangle probe

Run `tools/native-probes/Test-TouchePX5-Triangle.ps1` from PowerShell after building the emulator.
Requires the existing local .NET build and LLVM tools at the paths used by the runner.

This SDK-free synthetic guest submits gfx10 vertex/pixel programs through AGC, draws a triangle
on the Vulkan device, presents it and checks the swapchain readback. The capture checker checks
all pixel colors, exact interior/exterior coverage outside a two-source-pixel filter edge band,
and triangle area. The edge band accounts for the presenter's bilinear non-integer scaling.
A blank frame was separately confirmed to fail the checker.

Verified 2026-09-23 on GTX 1060 3 GB: 2,037,120 output pixels, 563,461 fully red pixels.
The previous fixture omitted PS input masks and user-SGPR resource registers; this runner sets
them explicitly, including zero values. No emulator behavior was changed for this probe.

Outputs: C:/Dev/TouchePX5/artifacts/smoke-triangle/triangle.log and triangle.png;
timestamped GPU captures are retained beneath that directory.
This is one synthetic raster draw, not commercial game compatibility.

The working copy was moved into this source repository because writes to the original OneDrive
fixture path were denied. Security settings were not changed. The old OneDrive triangle script
is an unfinished earlier version; use this runner.
