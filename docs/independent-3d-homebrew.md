# Independent GPU homebrew checkpoint

The next target after Wolf is the unchanged `examples/gpu_cube/main.c` from
[ps5link-sdk](https://github.com/Rufidj/ps5link-sdk), pinned to
`ea771e535378740b6a058b8e5419eb8a0e0e0ec8`. Its vertex/index buffers, matrices,
lighting shaders and tiled render target exercise the real AGC path. Unlike
our synthetic triangle fixtures, this source was written independently of this
emulator. Its startup and cube source are not patched to skip failures.

`tools/external-homebrew/Build-PS5LinkCube.ps1` builds the host linker and
unchanged PS5 source on Windows. The cube only needs upstream mesh shader
containers, so it does not require assembling the SDK's additional texture
shaders. Upstream keeps the PSSL source, GPL-3.0 license and attribution with
those containers. No proprietary SDK or firmware is added.

The result is an unsigned SCE ELF for the emulator, not a signed console package.
The first native run of `artifacts/external-homebrew/ps5link-cube/app.elf` reached
main and exited with guest status 1 / host exit 4 before rendering. Logs remain
in that directory. The two missing NIDs independently match the public name
hash algorithm:

| NID | Name | Observed call |
| --- | --- | --- |
| `zl7hupSO0C0` | `sceKernelSendNotificationRequest` | notification size `0xC30` |
| `kW3GLb7QfPg` | `sceAgcInit` | state pointer, defaults revision 8 |

The emulator's existing `AgcExports.Init` is registered as `23LRUSvYu1M`
and already documents a collision with the actual catalog function. The cube's
prototype names its second argument `size`, but
[SharpProspero AgcDevice](https://github.com/SvenGDK/SharpProspero/blob/main/src/SharpProspero/Graphics/Agc/AgcDevice.cs)
uses the real call with defaults revision 8 and a caller-owned eight-byte state
word. Thus the initial suspicion of incompatible arguments is not established.
The confirmed gap is the missing NID; the existing export also merely validates
arguments and returns success without initializing the state. Establish the
state write and register-default selection before binding the real NID. No cube
pixels or successful rendering are claimed yet.

[AGC Gears](https://github.com/mpereiraesaa/ps5-agc-gears) at
`1ae1f9182abd2770c131b97419034fb85173c2dc` is retained as a later independent
depth-tested scene. Its native build requires public GFX1013 LLPC and a pinned
native foundation; those dependencies have not been built here. The simpler
cube already exposes the first actual compatibility gap without that setup.
