/* Copyright (C) 2026 SharpEmu Emulator Project
 * SPDX-License-Identifier: GPL-2.0-or-later */
ENTRY(_start)
PHDRS {
    text PT_LOAD FILEHDR PHDRS FLAGS(5);
    data PT_LOAD FLAGS(6);
}
SECTIONS {
    . = 0x200000 + SIZEOF_HEADERS;
    .text : { *(.text) *(.rodata) } :text
    . = ALIGN(0x4000);
    .data : { *(.data) *(.bss) } :data
    /DISCARD/ : { *(.comment) *(.note*) }
}
