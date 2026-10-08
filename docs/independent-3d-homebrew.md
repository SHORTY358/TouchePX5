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

Further ABI inspection changes the first diagnosis. The cube and SharpProspero
call `sceAgcInit(state, revision)`, but
[OpenAGC game_compat.c](https://github.com/OpenAGC/OpenAGC/blob/main/src/game_compat.c)
at `3c7e8072f0d7684e930454794fd5c02056946d73` explicitly distinguishes:

- `kW3GLb7QfPg`: `sceAgcInit(uint32_t version)`;
- `23LRUSvYu1M`: `sceAgcInit_0090(void *state, uint32_t version)`.

The existing emulator export is the latter two-argument shape, despite its
short working name. An alias would therefore use the wrong register. The
upstream cube's two-argument declaration is incompatible with its linker's
chosen NID according to that reference. This is an upstream sample ABI issue
as well as a genuinely missing emulator export.

`Build-PS5LinkCube.ps1 -CorrectInitAbi` creates a distinct `app-init-abi.elf`,
changing only the declaration and call to `sceAgcInit(8)` in a generated copy.
The upstream checkout and original ELF remain unchanged. Its native rerun
confirms RDI=8 at the unresolved real NID; it still exits before rendering.
The next emulator change must select the per-guest defaults revision through
this one-argument entry point, with meaningful invalid-version and isolation
checks, then repeat this corrected-input run. Neither profile has rendered
cube pixels yet. The native initialization state and firmware behavior still
need careful scope boundaries; references are not console validation.

[AGC Gears](https://github.com/mpereiraesaa/ps5-agc-gears) at
`1ae1f9182abd2770c131b97419034fb85173c2dc` is retained as a later independent
depth-tested scene. Its native build requires public GFX1013 LLPC and a pinned
native foundation; those dependencies have not been built here. The simpler
cube already exposes the first actual compatibility gap without that setup.
