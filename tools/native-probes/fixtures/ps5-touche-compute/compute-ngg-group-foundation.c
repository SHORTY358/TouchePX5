// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Independent wave32 group probe: local-index tail mask, shared LDS and guarded readback.
typedef unsigned int u32;
typedef unsigned long long u64;
#ifdef NGG_FORCE_ALL_ACTIVE
#define GROUP_TAIL_COMPARE 0x7d880cc0 /* deliberate negative control: 64 > local index */
#else
#define GROUP_TAIL_COMPARE 0x7d880c02 /* live count in s2 > local index in v6 */
#endif
extern int (*open_slot)(int,int,int,void*);
extern int (*attribute_slot)(void*,u32,u32,u32,u32,u32,u32);
extern int (*register_slot)(int,int,u64*,int,void*);
extern int (*flip_slot)(int,int,int,u64);
extern int (*close_slot)(int);
extern int (*sleep_slot)(u32);
extern int (*submit_slot)(void*);
extern int (*submit_acb_slot)(u32,void*);
static u32 pixels[320*180] __attribute__((aligned(256)));
static volatile u32 output[528] __attribute__((aligned(256)));
static volatile u32 completed;
static unsigned char attribute[40];
static u64 addresses[1];
static u32 commands[128];
static int n;
static struct { u64 address; u32 count; u32 reserved; } submission;
// Paired stores use indices 1/3; independent dword reads check scaling.
static const u32 write2[] __attribute__((aligned(256))) = {
    0x7e0c0300,0x7e020200,0xbefe0481,0x7e000280,
    0xd8b40000,0x07000100,0x7e0002ff,0x00000004,
    0xd8b40000,0x07000100,0x7e0002ff,0x00000008,
    0xd8b40000,0x07000100,0x7e0002ff,0x0000000c,
    0xd8b40000,0x07000100,0x7e0002ff,0x00000010,
    0xd8b40000,0x07000100,0xbefe04c1,0xbf8a0000,
    0x7e000280,0x7e100286,0x7e16028c,0x7e120280,GROUP_TAIL_COMPARE,0xbefe046a,0x7e120281,
    0xd8380301,0x000b0800,0xbefe04c1,0xbf8a0000,
    0x7e000280,0xd8d80000,0x0c000000,0x7e0002ff,
    0x00000004,0xd8d80000,0x0d000000,0x7e0002ff,
    0x00000008,0xd8d80000,0x0e000000,0x7e0002ff,
    0x0000000c,0xd8d80000,0x0f000000,0x7e0002ff,
    0x00000010,0xd8d80000,0x10000000,0xbf8c0f70,
    0xe0782000,0x80010c06,0xe0702010,0x80011006,0xe0702014,0x80010606,0xe0702018,0x80010906,
    0xbf810000,
};
static const u32 write2_st64[] __attribute__((aligned(256))) = {
    0x7e0c0300,0x7e020200,0xbefe0481,0x7e000280,
    0xd8b40000,0x07000100,0x7e0002ff,0x00000100,
    0xd8b40000,0x07000100,0x7e0002ff,0x00000200,
    0xd8b40000,0x07000100,0x7e0002ff,0x00000300,
    0xd8b40000,0x07000100,0x7e0002ff,0x00000400,
    0xd8b40000,0x07000100,0xbefe04c1,0xbf8a0000,
    0x7e000280,0x7e100286,0x7e16028c,0x7e120280,GROUP_TAIL_COMPARE,0xbefe046a,0x7e120281,
    0xd83c0301,0x000b0800,0xbefe04c1,0xbf8a0000,
    0x7e000280,0xd8d80000,0x0c000000,0x7e0002ff,
    0x00000100,0xd8d80000,0x0d000000,0x7e0002ff,
    0x00000200,0xd8d80000,0x0e000000,0x7e0002ff,
    0x00000300,0xd8d80000,0x0f000000,0x7e0002ff,
    0x00000400,0xd8d80000,0x10000000,0xbf8c0f70,
    0xe0782000,0x80010c06,0xe0702010,0x80011006,0xe0702014,0x80010606,0xe0702018,0x80010906,
    0xbf810000,
};
static void reg(u32 index,u32 value) {
    commands[n++]=0xc0017602; commands[n++]=index; commands[n++]=value;
}
static void bind(const u32* shader,u32 live_count,u32 unused) {
    n=0;
    u64 code=(u64)shader, buffer=(u64)output;
    reg(0x20c,(u32)(code>>8)); reg(0x20d,(u32)(code>>40));
    reg(0x213,16u<<1);
    reg(0x207,64); reg(0x208,1); reg(0x209,1);
    reg(0x240,48);
    reg(0x244,(u32)buffer); reg(0x245,(u32)(buffer>>32)|(32u<<16));
    reg(0x246,66); reg(0x247,0);
    reg(0x242,live_count); (void)unused;
}
static int dispatch(u32 groups,u32 signal) {
    u64 label=(u64)&completed;
    commands[n++]=0xc0031502;
    commands[n++]=groups; commands[n++]=1; commands[n++]=1; commands[n++]=5|(1u<<15);
    commands[n++]=0xc0064902; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=signal; commands[n++]=0; commands[n++]=0;
    submission.address=(u64)commands; submission.count=(u32)n;
#ifdef IMAGE_ATOMIC_COMPUTE_QUEUE
    if(submit_acb_slot(7,&submission)!=0) return 64;
#else
    if(submit_slot(&submission)!=0) return 64;
#endif
    for(u32 i=0;i<5000 && completed!=signal;i++) sleep_slot(1000);
    return completed==signal ? 0 : 65;
}
int _start(void) {
    int video=open_slot(0,0,0,(void*)0);
    if(video<=0) return 60;
    if(attribute_slot(attribute,0x80000000,1,0,320,180,320)!=0) return 61;
    addresses[0]=(u64)pixels;
    if(register_slot(video,0,addresses,1,attribute)<0) return 62;
    for(u32 i=0;i<320*180;i++) pixels[i]=0xff102030;
    if(flip_slot(video,0,1,0)!=0) return 63;
    sleep_slot(300000);
    static const u32 masks[][2]={{0,0},{13,0},{32,0},{33,0},{63,0},{64,0}};
    for(u32 width=0;width<2;width++) {
        const u32* code=width==0 ? write2 : write2_st64;
        for(u32 pass=0;pass<6;pass++) {
            for(u32 i=0;i<528;i++) output[i]=0xdeadbeef;
            bind(code,masks[pass][0],masks[pass][1]);
            int result=dispatch(1,width*6+pass+1);
            if(result) return result;
            for(u32 invocation=0;invocation<64;invocation++) {
                for(u32 component=0;component<4;component++) {
                    u32 expected=pass && component==1 ? 6 : pass && component==3 ? 12 : 48;
                    if(output[invocation*8+component]!=expected) return 150+width*6+pass;
                }
                if(output[invocation*8+4]!=48) return 162+width*6+pass;
                if(output[invocation*8+5]!=invocation) return 70;
                if(output[invocation*8+6]!=(invocation<masks[pass][0] ? 1u : 0u)) return 72;
                for(u32 component=7;component<8;component++)
                    if(output[invocation*8+component]!=0xdeadbeef) return 69;
            }
            for(u32 i=512;i<528;i++) if(output[i]!=0xdeadbeef) return 69;
        }
    }
    for(u32 i=0;i<320*180;i++) pixels[i]=0xff20a060;
    if(flip_slot(video,0,1,1)!=0) return 71;
    sleep_slot(200000);
    close_slot(video);
    return 0;
}
