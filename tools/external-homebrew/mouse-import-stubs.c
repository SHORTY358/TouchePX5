// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Link-time symbol provider only. Guest imports must resolve to the HLE exports;
// these placeholders are not copied into or executed by the emulator.
__attribute__((visibility("default"))) int sceMouseInit(void) { return -1; }
__attribute__((visibility("default"))) int sceMouseOpen(void) { return -1; }
__attribute__((visibility("default"))) int sceMouseRead(void) { return -1; }
