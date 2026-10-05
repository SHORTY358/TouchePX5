/* Copyright (C) 2026 SharpEmu Emulator Project
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Explicit userland source-build profile. SharpEmuRuntime runs ELF
 * preinit/init arrays before this entry and fini arrays after it returns.
 */
.text
.global _start
.type _start,@function
_start:
    push %rbp
    mov %rsp,%rbp
    sub $32,%rsp
    mov %rdi,%rcx
    /* The emulator process frame has argc and three inline argv pointers. */
    mov 8(%rcx),%rax
    mov %rax,(%rsp)
    mov 16(%rcx),%rax
    mov %rax,8(%rsp)
    mov 24(%rcx),%rax
    mov %rax,16(%rsp)
    movq $0,24(%rsp)
    mov (%rcx),%edi
    mov %rsp,%rsi
    xor %edx,%edx
    call main
    leave
    ret
