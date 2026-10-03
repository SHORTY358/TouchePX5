// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
// SDK-free R32_UINT ACB image atomics with a waiting graphics-queue consumer.
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
static volatile u32 ready;
static u32 producer_commands[128], consumer_commands[128];
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
static const u32 atomic_add[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020280,0x7e080200,0x7e040210,
    0xf0442108,0x00020400,0xbf8c0f70,
    0xe0702000,0x80010402,0xbf810000
};
static const u32 read_image[] __attribute__((aligned(256))) = {
    0x7e000280,0x7e020280,
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
    reg(0x249,(u32)(texture>>40)|(20u<<20)|(3u<<30)); // R32_UINT, width=4
    reg(0x24a,0); reg(0x24b,0x90000fac); // height=1, 2D
    reg(0x24c,3); reg(0x24d,0); reg(0x24e,0); reg(0x24f,0);
}
static void append_dispatch(u32 groups, volatile u32* label_word,u32 signal) {
    u64 label=(u64)label_word;
    commands[n++]=0xc0031502;
    commands[n++]=groups; commands[n++]=1; commands[n++]=1; commands[n++]=5;
    commands[n++]=0xc0064902; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=signal; commands[n++]=0; commands[n++]=0;
}
static int wait_completed(u32 signal) {
    for(u32 i=0;i<5000 && completed!=signal;i++) sleep_slot(1000);
    return completed==signal ? 0 : 65;
}
static int seed_image(void) {
    bind(seed,7);
    append_dispatch(1,&completed,1);
    submission.address=(u64)commands; submission.count=(u32)n;
    if(submit_acb_slot(7,&submission)!=0) return 64;
    return wait_completed(1);
}
static int atomic_then_waiting_read(u32 increment,u32 signal) {
    ready=0;
    u32 previous=completed;
    bind(atomic_add,increment);
    append_dispatch(2,&ready,signal);
    u32 producer_count=(u32)n;
    for(u32 i=0;i<producer_count;i++) producer_commands[i]=commands[i];
    bind(read_image,0);
    append_dispatch(1,&completed,signal+1);
    u32 read_count=(u32)n;
    u64 label=(u64)&ready;
    consumer_commands[0]=0xc0053c00; consumer_commands[1]=0x13;
    consumer_commands[2]=(u32)label; consumer_commands[3]=(u32)(label>>32);
    consumer_commands[4]=signal; consumer_commands[5]=0xffffffff;
    consumer_commands[6]=4;
    for(u32 i=0;i<read_count;i++) consumer_commands[7+i]=commands[i];
    // Submit the graphics consumer first: it must suspend until the ACB producer.
    volatile u64 consumer_address=(u64)consumer_commands;
    submission.address=consumer_address; submission.count=7+read_count;
    if(submit_slot(&submission)!=0) return 64;
    sleep_slot(20000);
    if(completed!=previous || output[2]!=0xdeadbeef) return 74;
    submission.address=(u64)producer_commands; submission.count=producer_count;
    if(submit_acb_slot(7,&submission)!=0) return 64;
    return wait_completed(signal+1);
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
    int result=seed_image();
    if(result) return result;
    if(output[15]!=7) return 72;
    for(u32 i=0;i<15;i++) if(output[i]!=0xdeadbeef) return 73;
    u32 expected=7;
    for(u32 pass=0;pass<2;pass++) {
        for(u32 i=0;i<16;i++) output[i]=0xdeadbeef;
        u32 increment=pass ? 5 : 3;
        result=atomic_then_waiting_read(increment,pass+1);
        if(result) return result;
        // Invocation order is free; the two returned old values must be unique.
        if(!((output[0]==expected && output[1]==expected+increment) ||
             (output[1]==expected && output[0]==expected+increment))) return 66;
        expected+=2*increment;
        if(output[2]!=expected) return 68;
        for(u32 i=3;i<16;i++) if(output[i]!=0xdeadbeef) return 69;
        for(u32 i=0;i<4;i++) if(image[i]!=99+i) return 70;
    }
    for(u32 i=0;i<320*180;i++) pixels[i]=0xff20a060;
    if(flip_slot(video,0,1,1)!=0) return 71;
    sleep_slot(200000);
    close_slot(video);
    return 0;
}
