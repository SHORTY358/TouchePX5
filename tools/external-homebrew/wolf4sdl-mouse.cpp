// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Explicit source-profile adapter, not an unchanged-release compatibility fix.
#include <SDL.h>
#include <stdint.h>
#include <stddef.h>
// Public ABI reference: shadps4-emu/shadPS4 src/core/libraries/mouse/mouse.h.
struct MouseData {
    uint64_t timestamp;
    uint8_t connected;
    uint32_t buttons;
    int32_t dx, dy, wheel, tilt;
    uint8_t reserved[8];
};
static_assert(sizeof(MouseData) == 40 && offsetof(MouseData, buttons) == 12);
extern "C" int sceMouseInit();
extern "C" int sceMouseOpen(int user, int type, int index, void* options);
extern "C" int sceMouseRead(int handle, MouseData* data, int count);
static int handle = -1, x = 320, y = 200;
extern "C" Uint32 __wrap_SDL_GetMouseState(int* outX, int* outY) {
    if (handle < 0 && sceMouseInit() == 0) handle = sceMouseOpen(0x10000000, 0, 0, nullptr);
    MouseData data = {};
    if (handle >= 0 && sceMouseRead(handle, &data, 1) > 0 && data.connected) {
        x += data.dx; y += data.dy;
    }
    if (outX) *outX = x;
    if (outY) *outY = y;
    return data.connected ? data.buttons : 0;
}
extern "C" void __wrap_SDL_WarpMouseInWindow(SDL_Window*, int newX, int newY) {
    x = newX; y = newY;
}
