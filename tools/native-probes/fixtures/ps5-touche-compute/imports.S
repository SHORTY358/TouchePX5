.section .guest_dynstr,"a"
.globl guest_dynstr, guest_dynstr_end, guest_lib_name, guest_module_name
guest_dynstr: .byte 0
guest_lib_name: .asciz "libSceVideoOut"
guest_module_name: .asciz "libSceVideoOut"
kernel_name: .asciz "libKernel"
agc_name: .asciz "libSceAgcDriver"

.macro IMPORT slot, nid, index
.section .guest_dynstr,"a"
\slot\()_name: .asciz "\nid"
.section .guest_dynsym,"a"
.long \slot\()_name-guest_dynstr
.byte 0x12,0
.short 0
.quad 0,0
.section .guest_pltgot,"aw"
.globl \slot
\slot: .quad 0
.section .guest_jmprela,"a"
.quad \slot, (\index << 32) | 7, 0
.endm

.section .guest_dynsym,"a"
.balign 8
.globl guest_dynsym, guest_dynsym_end
guest_dynsym: .zero 24
.section .guest_pltgot,"aw"
.balign 8
.globl guest_pltgot
guest_pltgot: .zero 24
.section .guest_jmprela,"a"
.balign 8
.globl guest_jmprela, guest_jmprela_end
guest_jmprela:
IMPORT open_slot, "Up36PTk687E#B#B", 1
IMPORT attribute_slot, "i6-sR91Wt-4#B#B", 2
IMPORT register_slot, "w3BY+tAEiQY#B#B", 3
IMPORT flip_slot, "U46NwOiJpys#B#B", 4
IMPORT close_slot, "uquVH4-Du78#B#B", 5
IMPORT sleep_slot, "1jfXLRVzisc#C#C", 6
IMPORT submit_slot, "UglJIZjGssM#D#D", 7
IMPORT submit_acb_slot, "gSRnr79F8tQ#D#D", 8
#ifdef FLIP_LIFECYCLE
IMPORT status_slot, "SbU3dwp80lQ#B#B", 9
IMPORT pending_slot, "zgXifHT9ErY#B#B", 10
IMPORT create_eq_slot, "D0OdFMjp46I#C#C", 11
IMPORT wait_eq_slot, "fzyMKs9kim0#C#C", 12
IMPORT delete_eq_slot, "jpFjmgAC5AE#C#C", 13
IMPORT add_flip_event_slot, "HXzjK9yI30k#B#B", 14
IMPORT attribute2_slot, "PjS5uASwcV8#B#B", 15
IMPORT register2_slot, "rKBUtgRrtbk#B#B", 16
#endif
.section .guest_dynstr,"a"
guest_dynstr_end:
.section .guest_dynsym,"a"
guest_dynsym_end:
.section .guest_jmprela,"a"
guest_jmprela_end:
.section .guest_rela,"a"
.globl guest_rela, guest_rela_end
guest_rela:
guest_rela_end:
.section .guest_dynamic,"aw"
.balign 8
.quad 0x61000035, guest_dynstr_offset
.quad 0x61000037, guest_dynstr_end-guest_dynstr
.quad 0x61000039, guest_dynsym_offset
.quad 0x6100003b, 24
.quad 0x6100003f, guest_dynsym_end-guest_dynsym
.quad 0x61000027, guest_pltgot
.quad 0x6100002b, 7
.quad 0x61000029, guest_jmprela_offset
.quad 0x6100002d, guest_jmprela_end-guest_jmprela
.quad 0x6100000f, 0x0001010100000000 + guest_module_name_offset
.quad 0x61000015, 0x0001000100000000 + guest_lib_name_offset
.quad 0x6100000f, 0x0002010100000000 + (kernel_name-guest_dynstr)
.quad 0x61000015, 0x0002000100000000 + (kernel_name-guest_dynstr)
.quad 0x6100000f, 0x0003010100000000 + (agc_name-guest_dynstr)
.quad 0x61000015, 0x0003000100000000 + (agc_name-guest_dynstr)
.quad 0,0
