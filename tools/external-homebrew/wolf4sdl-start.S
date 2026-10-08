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
    /* The emulator process frame has argc and three inline argv pointers. */
    mov 8(%rcx),%rax
    mov %rax,(%rsp)
    mov 16(%rcx),%rax
    mov %rax,8(%rsp)
    mov 24(%rcx),%rax
    mov %rax,16(%rsp)
    movq $0,24(%rsp)
    mov (%rcx),%r12d
    mov %rsp,%r13
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
