// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// SDK-free bounded atomic INC/DEC against an image or buffer, with guarded readback.
typedef unsigned int u32;
typedef unsigned long long u64;
#ifdef CLAMP_SIGNED_IMAGE
#define CLAMP_IMAGE_FORMAT 21u
#else
#define CLAMP_IMAGE_FORMAT 20u
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
static volatile u32 image[4] __attribute__((aligned(256)));
static volatile u32 output[80] __attribute__((aligned(256)));
static volatile u32 completed;
static unsigned char attribute[40];
static u64 addresses[1];
static u32 commands[128];
static int n;
static struct { u64 address; u32 count; u32 reserved; } submission;
// s0=input, s4..7=output buffer, s8..15=image (s8..11=input
// buffer in CLAMP_BUFFER mode), s16=workgroup X.
static const u32 seed[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020280,0x7e080200,
#ifdef CLAMP_BUFFER
    0xe0700000,0x80020400,
#else
    0xf0200108,0x00020400,
#endif
    0xbf8c0f70,
    0xe070003c,0x80010400,0xbf810000
};
static const u32 atomic_inc[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020280,0x7e080200,0x7e040210,
#ifdef CLAMP_BUFFER
    0xe0f04000,0x80020400,
#else
    0xf06c2108,0x00020400,
#endif
    0xbf8c0f70,
    0xe0702000,0x80010402,0xbf810000
};
static const u32 atomic_dec[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020280,0x7e080200,0x7e040210,
#ifdef CLAMP_BUFFER
    0xe0f44000,0x80020400,
#else
    0xf0702108,0x00020400,
#endif
    0xbf8c0f70,
    0xe0702000,0x80010402,0xbf810000
};
static const u32 read_image[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020280,
#ifdef CLAMP_BUFFER
    0xe0300000,0x80020400,
#else
    0xf0000108,0x00020400,
#endif
    0xbf8c0f70,
    0xe0700008,0x80010400,0xbf810000
};
static void reg(u32 index,u32 value) {
    commands[n++]=0xc0017602; commands[n++]=index; commands[n++]=value;
}
static void bind(const u32* shader,u32 input) {
    n=0;
    u64 code=(u64)shader, buffer=(u64)output, texture=(u64)image;
    reg(0x20c,(u32)(code>>8)); reg(0x20d,(u32)(code>>40));
    reg(0x213,(16u<<1)|(1u<<7));
    reg(0x207,1); reg(0x208,1); reg(0x209,1);
    reg(0x240,input);
    reg(0x244,(u32)buffer); reg(0x245,(u32)(buffer>>32)|(4u<<16));
    reg(0x246,80); reg(0x247,0);
#ifdef CLAMP_BUFFER
    reg(0x248,(u32)texture); reg(0x249,(u32)(texture>>32)|(4u<<16));
    reg(0x24a,4); reg(0x24b,0);
#else
    reg(0x248,(u32)(texture>>8));
    reg(0x249,(u32)(texture>>40)|(CLAMP_IMAGE_FORMAT<<20)|(3u<<30)); // R32_UINT/SINT, width=4
    reg(0x24a,0); reg(0x24b,0x90000fac); // height=1, 2D
    reg(0x24c,3); reg(0x24d,0); reg(0x24e,0); reg(0x24f,0);
#endif
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
    // Image-mode CPU backing deliberately disagrees with GPU seeds/results.
    // Buffer mode instead verifies guest writeback and the three guard words.
    for(u32 i=0;i<4;i++) image[i]=99+i;
    for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
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
        bind(seed,c[0]);
        int result=dispatch(1,pass*3+1);
        if(result) return result;
        if(output[15]!=c[0]) return 72;
        for(u32 i=0;i<80;i++) if(i!=15 && output[i]!=0xdeadbeef) return 73;
        for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
        bind(c[5] ? atomic_dec : atomic_inc,c[1]);
        result=dispatch(2,pass*3+2);
        if(result) return result;
        if(!((output[0]==c[2] && output[1]==c[3]) ||
             (output[1]==c[2] && output[0]==c[3]))) return 80+pass;
        for(u32 i=2;i<80;i++) if(output[i]!=0xdeadbeef) return 67;
        bind(read_image,0);
        result=dispatch(1,pass*3+3);
        if(result) return result;
        if(output[2]!=c[4]) return 100+pass;
        for(u32 i=3;i<80;i++) if(output[i]!=0xdeadbeef) return 69;
#ifdef CLAMP_BUFFER
        if(image[0]!=c[4]) return 120+pass;
        for(u32 i=1;i<4;i++) if(image[i]!=99+i) return 70;
#else
        for(u32 i=0;i<4;i++) if(image[i]!=99+i) return 70;
#endif
    }
    // 64 independent workgroups share one texel/word, traversing the full
    // 0..7 cycle eight times. Validate returned-value counts without imposing
    // an invocation order, then independently read the final value.
    for(u32 pass=0;pass<2;pass++) {
        for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
        bind(seed,6);
        int result=dispatch(1,34+pass*3);
        if(result) return result;
        if(output[15]!=6) return 72;
        for(u32 i=0;i<80;i++) if(i!=15 && output[i]!=0xdeadbeef) return 73;
        for(u32 i=0;i<80;i++) output[i]=0xdeadbeef;
        bind(pass ? atomic_dec : atomic_inc,7);
        result=dispatch(64,35+pass*3);
        if(result) return result;
        u32 counts[8]={0,0,0,0,0,0,0,0};
        for(u32 i=0;i<64;i++) {
            if(output[i]>7) return 132+pass;
            counts[output[i]]++;
        }
        for(u32 i=0;i<8;i++) if(counts[i]!=8) return 134+pass;
        for(u32 i=64;i<80;i++) if(output[i]!=0xdeadbeef) return 67;
        bind(read_image,0);
        result=dispatch(1,36+pass*3);
        if(result) return result;
        if(output[2]!=6) return 136+pass;
        for(u32 i=64;i<80;i++) if(output[i]!=0xdeadbeef) return 69;
#ifdef CLAMP_BUFFER
        if(image[0]!=6) return 138+pass;
        for(u32 i=1;i<4;i++) if(image[i]!=99+i) return 70;
#else
        for(u32 i=0;i<4;i++) if(image[i]!=99+i) return 70;
#endif
    }
    for(u32 i=0;i<320*180;i++) pixels[i]=0xff20a060;
    if(flip_slot(video,0,1,1)!=0) return 71;
    sleep_slot(200000);
    close_slot(video);
    return 0;
}
