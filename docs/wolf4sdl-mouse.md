# Wolf4SDL mouse source profile

Double-click `tools/external-homebrew/Start-Wolf4SDLMouse.cmd` to run the built
mouse profile until the game closes. Click the game window to focus it. Mouse
motion turns left/right, left button fires, W/S move and Space operates doors.
This Wolf engine has no vertical looking. Alt+Tab releases cursor ownership.
The interactive profile uses its own `mouse-play` save/configuration directory.

The user's first physical test confirmed turning and firing in run
`20261008-121910-159`. The first implementation's visible cursor and fast-motion
escape were reported by the user and revised: Windows confines to the foreground
game client rectangle while GLFW hides the cursor, and focus loss/closing releases
ownership. The revised capture requires its own physical confirmation; a passing
library test does not establish cursor behavior on the desktop.

Rebuild with `Build-Wolf4SDL.ps1 -EnableMouseBridge`. This creates a separate
`wolf3d-shareware-userland-mouse.elf`, source adapter and build-report-mouse.json.
The ordinary source ELF remains separate. The public PS5 SDL backend in the
pinned bundle does not poll USB mouse input, so the adapter explicitly wraps the
engine's SDL mouse-state/warp calls and forwards through mouse HLE imports.
The generated libSceMouse.so is a link-time provider only; its placeholders
must never be treated as a runtime firmware implementation.

Mouse packets follow the [public mouse ABI reference](https://github.com/shadps4-emu/shadPS4/blob/main/src/core/libraries/mouse/mouse.h):
timestamp at 0, connected at 8, buttons at 12, signed motion at 16/20, 40 bytes.
Two regression checks cover signed motion, reserved zeros, inactive input and
per-guest handle isolation. Full library tests pass 1244/1244.

This remains an explicit modified homebrew source profile. It does not prove
mouse compatibility for unchanged PS5 games. Wheel/tilt packets and non-Windows
host mouse input are not implemented. Movement currently uses focused position
polling/recentering rather than a raw-input event queue.
