# Wolf4SDL mouse source profile

Double-click `tools/external-homebrew/Start-Wolf4SDLMouse.cmd` to run the built
mouse profile until the game closes. Left-click inside the game to capture the mouse. Mouse
motion turns left/right, left button fires, W/S move and Space operates doors.
This Wolf engine has no vertical looking. F8 or Alt+Tab releases cursor ownership.
After release, the title bar can be dragged normally. Activation and title-bar
interaction do not capture; another left click inside the game is required.
The interactive profile uses its own `mouse-play` save/configuration directory.
It keeps the engine's default fullscreen SDL surface so the game fills the host
framebuffer. `--windowed-mouse` is deliberately omitted: the pinned PS5 backend
centers its 640x400 surface in the 1920x1080 framebuffer instead of scaling it.

The user's first physical test confirmed turning and firing in run
`20261008-121910-159`. The first implementation's visible cursor and fast-motion
escape were reported by the user. The later position-polling/client-rectangle
confinement attempt also failed the user's physical check. It has been replaced
with GLFW Raw mode (Disabled fallback), which owns hiding and relative capture.
Window-thread motion events accumulate in a synchronized buffer for guest reads;
no desktop cursor warping or global ClipCursor restriction is used now.
Focus loss restores Normal mode and resets motion; closing detaches/disposes
the input context. The user reports the Raw revision works, but title-bar dragging
previously captured immediately. Capture now requires a focused client-area left
click. F8 releases without recapturing while focused. This final title-bar fix
has passed regression checks and the user confirms the result works.

Rebuild with `Build-Wolf4SDL.ps1 -EnableMouseBridge`. This creates a separate
`wolf3d-shareware-userland-mouse.elf`, source adapter and build-report-mouse.json.
The ordinary source ELF remains separate. The public PS5 SDL backend in the
pinned bundle does not poll USB mouse input, so the adapter explicitly wraps the
engine's SDL mouse-state/warp calls and forwards through mouse HLE imports.
The generated libSceMouse.so is a link-time provider only; its placeholders
must never be treated as a runtime firmware implementation.

Mouse packets follow the [public mouse ABI reference](https://github.com/shadps4-emu/shadPS4/blob/main/src/core/libraries/mouse/mouse.h):
timestamp at 0, connected at 8, buttons at 12, signed motion at 16/20, 40 bytes.
Four regression checks cover signed motion, reserved zeros, inactive input,
per-guest handle isolation, large relative movement, one-time consumption,
fractional remainder, focus reset and click-only capture after window activation
or release. Full library tests pass 1246/1246; CLI build has zero warnings/errors.

This remains an explicit modified homebrew source profile. It does not prove
mouse compatibility for unchanged PS5 games. Wheel/tilt packets and non-Windows
host mouse input are not implemented. Relative capture currently applies to the
standalone GLFW presenter; the embedded GUI surface needs its own input adapter.
