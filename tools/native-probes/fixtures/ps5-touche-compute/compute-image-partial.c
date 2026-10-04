// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// SDK-free partial R32_UINT CPU DMA with an independent GPU-only neighbor.
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
static volatile u32 image[4] __attribute__((aligned(256)));
static volatile u32 output[16] __attribute__((aligned(256)));
static volatile u32 completed;
static unsigned char attribute[40];
static u64 addresses[1];
static u32 commands[128];
static int n;
static struct { u64 address; u32 count; u32 reserved; } submission;
// s0=input, s4..7=buffer, s8..15=image, s16=workgroup X.
static const u32 seed[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020280,0x7e080200,
    0xf0200108,0x00020400,0xbf8c0f70,
    0xe070003c,0x80010400,0xbf810000
};
static const u32 read_image[] __attribute__((aligned(256))) = {
    0x7e000200,0x7e020280,
    0xf0000108,0x00020400,0xbf8c0f70,
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
    reg(0x246,16); reg(0x247,0);
    reg(0x248,(u32)(texture>>8));
#ifdef PARTIAL_SIGNED_IMAGE
    reg(0x249,(u32)(texture>>40)|(21u<<20)|(3u<<30)); // R32_SINT, width=4
#else
    reg(0x249,(u32)(texture>>40)|(20u<<20)|(3u<<30)); // R32_UINT, width=4
#endif
    reg(0x24a,0); reg(0x24b,0x90000fac); // height=1, 2D
    reg(0x24c,3); reg(0x24d,0); reg(0x24e,0); reg(0x24f,0);
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
    // CPU backing deliberately disagrees with the GPU seed and all sums.
    for(u32 i=0;i<4;i++) image[i]=99+i;
    for(u32 i=0;i<16;i++) output[i]=0xdeadbeef;
    if(flip_slot(video,0,1,0)!=0) return 63;
    sleep_slot(300000);
#ifdef PARTIAL_UNALIGNED_BYTE
    const u32 seed_value=0x11223344;
    const u32 expected_first=0x11227b44;
    const u32 expected_second=100;
#else
    const u32 seed_value=7;
    const u32 expected_first=7;
    const u32 expected_second=123;
#endif
    bind(seed,seed_value);
    int result=dispatch(1,1);
    if(result) return result;
    if(output[15]!=seed_value) return 72;
    for(u32 i=0;i<15;i++) if(output[i]!=0xdeadbeef) return 73;
    for(u32 i=0;i<16;i++) output[i]=0xdeadbeef;
    bind(read_image,0);
    // A managed DMA changes only the second image word. The first remains GPU 7,
    // while its stale CPU backing stays 99 and must never replace that GPU texel.
#ifdef PARTIAL_UNALIGNED_BYTE
    // Change byte 1 inside GPU-only texel 0. Its other three bytes must come
    // from the GPU value, not the stale CPU word 99. CPU becomes 0x00007b63;
    // the required GPU value is 0x11227b44.
    u64 changed=(u64)image+1;
#else
    u64 changed=(u64)&image[1];
#endif
    commands[n++]=0xc0055000; commands[n++]=2u<<29;
#ifdef PARTIAL_UNALIGNED_BYTE
    commands[n++]=0x7b; commands[n++]=0;
#else
    commands[n++]=123; commands[n++]=0;
#endif
    commands[n++]=(u32)changed; commands[n++]=(u32)(changed>>32);
#ifdef PARTIAL_UNALIGNED_BYTE
    commands[n++]=1;
#else
    commands[n++]=4;
#endif
    result=dispatch(1,2);
    if(result) return result;
    for(u32 i=0;i<16;i++) if(i!=2 && output[i]!=0xdeadbeef) return 81;
#ifdef PARTIAL_UNALIGNED_BYTE
    if(image[0]!=0x7b63 || image[1]!=100 || image[2]!=101 || image[3]!=102) return 82;
#else
    if(image[0]!=99 || image[1]!=123 || image[2]!=101 || image[3]!=102) return 82;
#endif
    if(output[2]!=expected_first) {
#ifdef PARTIAL_UNALIGNED_BYTE
        // Distinguish the known stale-CPU replacement from other failures.
        if(output[2]==0x7b63) return 85;
#endif
        return 80;
    }
    output[2]=0xdeadbeef;
    bind(read_image,1);
    result=dispatch(1,3);
    if(result) return result;
    if(output[2]!=expected_second) return 83;
    for(u32 i=0;i<16;i++) if(i!=2 && output[i]!=0xdeadbeef) return 84;
    for(u32 i=0;i<320*180;i++) pixels[i]=0xff20a060;
    if(flip_slot(video,0,1,1)!=0) return 71;
    sleep_slot(200000);
    close_slot(video);
    return 0;
}
