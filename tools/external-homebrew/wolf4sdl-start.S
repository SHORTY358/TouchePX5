/* Copyright (C) 2026 SharpEmu Emulator Project
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Explicit userland source-build profile. The generic runtime leaves main
 * startup guest-driven; this entry performs the open SDK's init-array calls.
 */
.text
.global _start
.type _start,@function
_start:
    push %rbp
    mov %rsp,%rbp
    push %r12
    push %r13
    push %r14
    push %r15
    sub $32,%rsp
    mov %rdi,%rcx
    /* Explicit source profile: argv is inline after argc/reserved.
     * The runner opts into a bounded extended array with a NULL terminator.
     */
    mov (%rcx),%r12d
    lea 8(%rcx),%r13
    lea __init_array_start(%rip),%r14
    lea __init_array_end(%rip),%r15
.Linit_next:
    cmp %r15,%r14
    jae .Lmain
    mov (%r14),%rax
    add $8,%r14
    test %rax,%rax
    jz .Linit_next
    cmp $-1,%rax
    je .Linit_next
    mov %r12d,%edi
    mov %r13,%rsi
    xor %edx,%edx
    call *%rax
    jmp .Linit_next
.Lmain:
    mov %r12d,%edi
    mov %r13,%rsi
    xor %edx,%edx
    call main
    add $32,%rsp
    pop %r15
    pop %r14
    pop %r13
    pop %r12
    pop %rbp
    ret
