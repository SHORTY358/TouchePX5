// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// SDK-free LDS bounded atomics: scalar boundaries and contended shared cycles.
typedef unsigned int u32;
typedef unsigned long long u64;
extern int (*open_slot)(int,int,int,void*);
extern int (*attribute_slot)(void*,u32,u32,u32,u32,u32,u32);
extern int (*register_slot)(int,int,u64*,int,void*);
extern int (*flip_slot)(int,int,int,u64);
extern int (*close_slot)(int);
extern int (*sleep_slot)(u32);
extern int (*submit_slot)(void*);
extern int (*submit_acb_slot)(u32,void*);
static u32 pixels[320*180] __attribute__((aligned(256)));
static volatile u32 output[80] __attribute__((aligned(256)));
static volatile u32 completed;
static unsigned char attribute[40];
static u64 addresses[1];
static u32 commands[128];
static int n;
static struct { u64 address; u32 count; u32 reserved; } submission;
// One invocation initializes LDS, applies INC/DEC twice, then reads it.
// s0=initial, s1=limit, s4..7=output buffer.
static const u32 atomic_inc[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020200,
    0xd8340000,0x00000100,0xbf8a0000,
    0x7e020201,0xd88c0000,0x03000100,0xbf8c0f70,
    0xe0700000,0x80010300,
    0xd88c0000,0x04000100,0xbf8c0f70,
    0xe0700004,0x80010400,
    0xd8d80000,0x05000000,0xbf8c0f70,
    0xe0700008,0x80010500,0xbf810000
};
static const u32 atomic_dec[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020200,
    0xd8340000,0x00000100,0xbf8a0000,
    0x7e020201,0xd8900000,0x03000100,0xbf8c0f70,
    0xe0700000,0x80010300,
    0xd8900000,0x04000100,0xbf8c0f70,
    0xe0700004,0x80010400,
    0xd8d80000,0x05000000,0xbf8c0f70,
    0xe0700008,0x80010500,0xbf810000
};
// 64 local invocations contend on one LDS word. Only lane 0 seeds it and
// writes the final readback; all lanes participate in both barriers.
static const u32 cycle_inc[] __attribute__((aligned(256))) = {
    0x7e0c0300,0x7e000280,0x7e020200,
    0xbefe0481,0xd8b40000,0x07000100,
    0xbefe04c1,0xbf8a0000,
    0x7e020201,0xd88c0000,0x03000100,0xbf8c0f70,
    0xe0702000,0x80010306,
    0xbf8a0000,0xbefe0481,
    0xd8d80000,0x05000000,0xbf8c0f70,
    0xe0700100,0x80010500,0xbf810000
};
static const u32 cycle_dec[] __attribute__((aligned(256))) = {
    0x7e0c0300,0x7e000280,0x7e020200,
    0xbefe0481,0xd8b40000,0x07000100,
    0xbefe04c1,0xbf8a0000,
    0x7e020201,0xd8900000,0x03000100,0xbf8c0f70,
    0xe0702000,0x80010306,
    0xbf8a0000,0xbefe0481,
    0xd8d80000,0x05000000,0xbf8c0f70,
    0xe0700100,0x80010500,0xbf810000
};
// 64 local invocations contend on one LDS word. Only lane 0 seeds it and
// writes the final readback; all lanes participate in both barriers.
static const u32 masked_cycle_inc[] __attribute__((aligned(256))) = {
    0x7e0c0300,0x7e000280,0x7e020200,
    0xbefe0481,0xd8b40000,0x07000100,
    0xbefe04c1,0xbf8a0000,
    0x7e020202,0xbefe0481,0xd8340000,0x00000100,
    0xbefe04c1,0xbf8a0000,
    0x7e020201,0xd88c0000,0x03000100,0xbf8c0f70,
    0xe0702000,0x80010306,
    0xbf8a0000,0xbefe0481,
    0xd8d80000,0x05000000,0xbf8c0f70,
    0xe0700100,0x80010500,0xbf810000
};
static const u32 masked_cycle_dec[] __attribute__((aligned(256))) = {
    0x7e0c0300,0x7e000280,0x7e020200,
    0xbefe0481,0xd8b40000,0x07000100,
    0xbefe04c1,0xbf8a0000,
    0x7e020202,0xbefe0481,0xd8340000,0x00000100,
    0xbefe04c1,0xbf8a0000,
    0x7e020201,0xd8900000,0x03000100,0xbf8c0f70,
    0xe0702000,0x80010306,
    0xbf8a0000,0xbefe0481,
    0xd8d80000,0x05000000,0xbf8c0f70,
    0xe0700100,0x80010500,0xbf810000
};
// Masked store without atomics: no active writer, lane 0, lane 32 or lane 63.
// A single lane seeds 48; after the optional store, all 64 read the shared word.
static const u32 masked_write[] __attribute__((aligned(256))) = {
    0x7e0c0300,0x7e000280,0x7e020200,
    0xbefe0481,0xd8b40000,0x07000100,
    0xbefe04c1,0xbf8a0000,
    0x7e020202,0xbefe0408,0xd8340000,0x00000100,
    0xbefe04c1,0xbf8a0000,
    0xd8d80000,0x05000000,0xbf8c0f70,
    0xe0702000,0x80010506,0xbf810000
};
static void reg(u32 index,u32 value) {
    commands[n++]=0xc0017602; commands[n++]=index; commands[n++]=value;
}
static void bind(const u32* shader,u32 initial,u32 limit,u32 local_size,u32 replacement,u32 mask_low,u32 mask_high) {
    n=0;
    u64 code=(u64)shader, buffer=(u64)output;
    reg(0x20c,(u32)(code>>8)); reg(0x20d,(u32)(code>>40));
    reg(0x213,16u<<1);
    reg(0x207,local_size); reg(0x208,1); reg(0x209,1);
    reg(0x240,initial); reg(0x241,limit); reg(0x242,replacement);
    reg(0x248,mask_low); reg(0x249,mask_high);
    reg(0x244,(u32)buffer); reg(0x245,(u32)(buffer>>32)|(4u<<16));
    reg(0x246,80); reg(0x247,0);
}
static int dispatch(u32 groups,u32 signal) {
    u64 label=(u64)&completed;
    commands[n++]=0xc0031502;
    commands[n++]=groups; commands[n++]=1; commands[n++]=1; commands[n++]=5;
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
    // Independent ISA expectations: initial, limit, old0, old1, final, decrement.
    static const u32 cases[][6] = {
        {6,7,6,7,0,0}, {9,7,9,0,1,0}, {0,0,0,0,0,0},
        {0xfffffffe,0xffffffff,0xfffffffe,0xffffffff,0,0},
        {0x80000000,7,0x80000000,0,1,0},
        {0,7,0,7,6,1}, {9,7,9,7,6,1}, {7,7,7,6,5,1},
        {0,0,0,0,0,1}, {0,0xffffffff,0,0xffffffff,0xfffffffe,1},
        {0x80000000,7,0x80000000,7,6,1}
    };
    for(u32 pass=0;pass<11;pass++) {
        const u32* c=cases[pass];
        for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
        bind(c[5] ? atomic_dec : atomic_inc,c[0],c[1],1,0,0,0);
        int result=dispatch(1,pass+1);
        if(result) return result;
        if(output[0]!=c[2] || output[1]!=c[3]) return 80+pass;
        if(output[2]!=c[4]) return 100+pass;
        for(u32 i=3;i<80;i++) if(output[i]!=0xdeadbeef) return 69;
    }
    for(u32 pass=0;pass<2;pass++) {
        for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
        bind(pass ? cycle_dec : cycle_inc,6,7,64,0,0,0);
        int result=dispatch(1,pass+12);
        if(result) return result;
        u32 counts[8]={0,0,0,0,0,0,0,0};
        for(u32 i=0;i<64;i++) {
            if(output[i]>7) return 132+pass;
            counts[output[i]]++;
        }
        for(u32 i=0;i<8;i++) if(counts[i]!=8) return 134+pass;
        if(output[64]!=6) return 136+pass;
        for(u32 i=65;i<80;i++) if(output[i]!=0xdeadbeef) return 69;
    }
    for(u32 pass=0;pass<2;pass++) {
        for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
        bind(pass ? masked_cycle_dec : masked_cycle_inc,48,7,64,pass ? 3 : 6,0,0);
        int result=dispatch(1,pass+14);
        if(result) return result;
        u32 counts[8]={0,0,0,0,0,0,0,0};
        for(u32 i=0;i<64;i++) {
            if(output[i]>7) return 132+pass;
            counts[output[i]]++;
        }
        for(u32 i=0;i<8;i++) if(counts[i]!=8) return 134+pass;
        if(output[64]!=(pass ? 3u : 6u)) return 136+pass;
        for(u32 i=65;i<80;i++) if(output[i]!=0xdeadbeef) return 69;
    }
    static const u32 mask_cases[][4] = {
        {0,0,6,48}, {1,0,6,6}, {0,1,3,3}, {0,0x80000000,9,9}
    };
    for(u32 pass=0;pass<4;pass++) {
        const u32* c=mask_cases[pass];
        for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
        bind(masked_write,48,7,64,c[2],c[0],c[1]);
        int result=dispatch(1,16+pass);
        if(result) return result;
        for(u32 i=0;i<64;i++) if(output[i]!=c[3]) return 140+pass;
        for(u32 i=64;i<80;i++) if(output[i]!=0xdeadbeef) return 69;
    }
    for(u32 i=0;i<320*180;i++) pixels[i]=0xff20a060;
    if(flip_slot(video,0,1,1)!=0) return 71;
    sleep_slot(200000);
    close_slot(video);
    return 0;
}
