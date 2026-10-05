/* Copyright (C) 2026 SharpEmu Emulator Project
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Regression for the payload SDK ABI; this is not third-party game evidence.
 */
.text
.global _start
.type _start,@function
_start:
    /* Same entry signature as the SDK, with balanced SysV stack alignment. */
    push %rbp
    mov %rsp,%rbp
    push %r15
    push %r14
    push %r13
    push %r12
    push %rbx
    push %rax
    mov %rdi,%rbx
    mov (%rbx),%r12

    mov $1,%edi
    lea resolver_name(%rip),%rsi
    lea resolved(%rip),%rdx
    call *%r12
    test %eax,%eax
    jne failed
    cmpq $0,resolved(%rip)
    je failed
    cmp %r12,resolved(%rip)
    je failed

    /* SDK helper returns the address directly; name is read-only data. */
    mov $-1,%edi
    mov $1,%esi
    lea resolver_name(%rip),%rdx
    call kernel_dynlib_dlsym
    cmp resolved(%rip),%rax
    jne failed
    mov %rax,%r13

    /* The returned resolver itself must be callable with its public ABI. */
    mov $1,%edi
    lea resolver_name(%rip),%rsi
    lea resolved(%rip),%rdx
    call *%r13
    test %eax,%eax
    jne failed
    cmp %r13,resolved(%rip)
    jne failed

    /* Another process and the alternate payload environment are not aliases. */
    mov $123,%edi
    mov $1,%esi
    lea resolver_name(%rip),%rdx
    call kernel_dynlib_dlsym
    test %rax,%rax
    jne failed
    mov $-1,%edi
    mov $0x2001,%esi
    lea resolver_name(%rip),%rdx
    call kernel_dynlib_dlsym
    test %rax,%rax
    jne failed
    mov $-1,%edi
    mov $1,%esi
    mov $1,%edx
    call kernel_dynlib_dlsym
    test %rax,%rax
    jne failed
    mov $-1,%edi
    mov $1,%esi
    lea missing_name(%rip),%rdx
    call kernel_dynlib_dlsym
    test %rax,%rax
    jne failed

    mov $1,%edi
    lea missing_name(%rip),%rsi
    lea resolved(%rip),%rdx
    call *%r12
    test %eax,%eax
    je failed
    cmpq $0,resolved(%rip)
    jne failed
    mov $1,%edi
    lea resolver_name(%rip),%rsi
    xor %edx,%edx
    call *%r12
    test %eax,%eax
    je failed

    /* Invalid name and output pointers must not fall through to host memory. */
    mov $1,%edi
    mov $1,%esi
    lea resolved(%rip),%rdx
    call *%r12
    test %eax,%eax
    je failed
    mov $1,%edi
    lea resolver_name(%rip),%rsi
    mov $1,%edx
    call *%r12
    test %eax,%eax
    je failed

#ifdef PROBE_SYSCALL
    /* Raw syscall arguments differ from SysV: argument four is R10. */
    lea 0xA(%r12),%r11
    mov $0x289,%eax
    mov $2,%edi
    mov $8,%esi
    mov $0x12345678,%edx
    mov $0xABCDEF,%r10d
    call *%r11
    jmp failed /* Unsupported calls must unwind to the host, never return. */
#endif
    xor %eax,%eax
    jmp done
failed:
    mov $31,%eax
done:
    add $8,%rsp
    pop %rbx
    pop %r12
    pop %r13
    pop %r14
    pop %r15
    pop %rbp
    ret

.p2align 4
.global kernel_dynlib_dlsym
.type kernel_dynlib_dlsym,@function
kernel_dynlib_dlsym:
    /* Loader must intercept the SDK helper; reaching this is a failure. */
    xor %eax,%eax
    ret
    .fill 29,1,0x90

.section .rodata
resolver_name: .asciz "sceKernelDlsym"
missing_name: .asciz "payload_symbol_does_not_exist"
.data
.p2align 3
resolved: .quad 0x1234
