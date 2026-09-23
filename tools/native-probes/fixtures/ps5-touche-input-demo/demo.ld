PHDRS {
    text PT_LOAD FILEHDR PHDRS FLAGS(5);
    data PT_LOAD FLAGS(6);
    dynlib 0x61000000 FLAGS(4);
    dynamic PT_DYNAMIC FLAGS(6);
}
SECTIONS {
    . = 0x200000 + SIZEOF_HEADERS;
    .text : { *(.text .text.*) *(.rodata .rodata.*) } :text
    . = ALIGN(0x4000);
    .guest_pltgot : { *(.guest_pltgot) } :data
    .data : { *(.data .data.*) } :data
    .guest_dynamic : { *(.guest_dynamic) } :data :dynamic
    .got : { *(.got .got.*) } :data
    .bss (NOLOAD) : { *(.bss .bss.*) *(COMMON) } :data
    . = ALIGN(0x4000);
    .guest_dynlib : {
        guest_dynlib_start = .;
        *(.guest_dynstr) *(.guest_dynsym) *(.guest_rela) *(.guest_jmprela)
        guest_dynlib_end = .;
    } :dynlib
    guest_dynstr_offset = guest_dynstr - guest_dynlib_start;
    guest_dynsym_offset = guest_dynsym - guest_dynlib_start;
    guest_rela_offset = guest_rela - guest_dynlib_start;
    guest_jmprela_offset = guest_jmprela - guest_dynlib_start;
    guest_lib_name_offset = guest_lib_name - guest_dynstr;
    guest_module_name_offset = guest_module_name - guest_dynstr;
    /DISCARD/ : { *(.comment) *(.note*) *(.eh_frame*) }
}
