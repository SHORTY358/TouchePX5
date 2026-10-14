// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Independent compute output -> vertex fetch bridge, not an NGG implementation.
typedef unsigned int u32;
typedef unsigned long long u64;
extern int (*open_slot)(int,int,int,void*);
extern int (*attribute_slot)(void*,u32,u32,u32,u32,u32,u32);
extern int (*register_slot)(int,int,u64*,int,void*);
extern int (*flip_slot)(int,int,int,u64);
extern int (*close_slot)(int);
extern int (*sleep_slot)(u32);
extern int (*submit_slot)(void*);
static u32 pixels[320*180] __attribute__((aligned(256)));
static unsigned char attribute[40];
static u64 addresses[1];
static volatile u32 output[28] __attribute__((aligned(256)));
static volatile u32 completed;
static u32 commands[256];
static int n;
static struct {u64 address;u32 count;u32 reserved;} submission;
static const u32 input[12] __attribute__((aligned(256))) = {
    0xbf400000,0xbf400000,0,0x3f800000,
    0x3f400000,0xbf400000,0,0x3f800000,
    0xbf400000,0x3f400000,0,0x3f800000
};
static const u32 producer[] __attribute__((aligned(256))) = {
    0x7e0c0300, // preserve local invocation index in v6
    0x7d880c00,0xbefe046a, // live count s0 > index; exact tail mask
    0xe0382000,0x80020006,0xbf8c0f70, // load four raw position words from s8..11
    0xe0782000,0x80030006, // write explicit position record to s12..15
    0xbefe04c1,0xbf8a0000,0xbf810000 // restore all participants, barrier
};
static const u32 vertex[] __attribute__((aligned(256))) = {
    0xe00c2000,0x80020005,0xbf8c0f70, // typed XYZW from s8..11, source ID v5
    0xf80008cf,0x03020100,0xbf810000
};
static const u32 pixel[] __attribute__((aligned(256))) = {
    0x7e0002f2,0x7e020280,0x7e040280,0x7e0602f2,
    0xf800180f,0x03020100,0xbf810000
};
static void reg(u32 op,u32 index,u32 value) {
    commands[n++]=0xc0010000|(op<<8); commands[n++]=index; commands[n++]=value;
}
static void descriptor(u32 first,u64 address,u32 stride,u32 records,u32 format) {
    reg(0x76,first,(u32)address); reg(0x76,first+1,(u32)(address>>32)|(stride<<16));
    reg(0x76,first+2,records); reg(0x76,first+3,(format<<12)|0xfac);
}
static int finish(u32 signal) {
    u64 label=(u64)&completed;
    commands[n++]=0xc0064900; commands[n++]=0x14;commands[n++]=1u<<29;
    commands[n++]=(u32)label;commands[n++]=(u32)(label>>32);
    commands[n++]=signal;commands[n++]=0;commands[n++]=0;
    submission.address=(u64)commands;submission.count=(u32)n;
    if(submit_slot(&submission)) return 64;
    for(u32 i=0;i<5000 && completed!=signal;i++) sleep_slot(1000);
    return completed==signal ? 0 : 65;
}
int _start(void) {
    int video=open_slot(0,0,0,(void*)0);
    if(video<=0) return 60;
    if(attribute_slot(attribute,0x80000000,1,0,320,180,320)) return 61;
    addresses[0]=(u64)pixels;
    if(register_slot(video,0,addresses,1,attribute)<0) return 62;
    for(u32 i=0;i<320*180;i++) pixels[i]=0xff202020;
    if(flip_slot(video,0,1,0)) return 63;
    sleep_slot(300000);
    for(u32 i=0;i<28;i++) output[i]=0xdeadbeef;
    u64 cs=(u64)producer;
    n=0;reg(0x76,0x20c,(u32)(cs>>8));reg(0x76,0x20d,(u32)(cs>>40));
    reg(0x76,0x213,16<<1);reg(0x76,0x207,64);reg(0x76,0x208,1);reg(0x76,0x209,1);
    reg(0x76,0x240,3);descriptor(0x248,(u64)input,16,3,77);
    descriptor(0x24c,(u64)output,16,3,77);
#ifndef BRIDGE_SKIP_PRODUCER
    commands[n++]=0xc0031502;commands[n++]=1;commands[n++]=1;commands[n++]=1;commands[n++]=5|(1<<15);
#endif
    int result=finish(1);if(result) return result;
    for(u32 i=0;i<12;i++) if(output[i]!=input[i]) return 66;
    for(u32 i=12;i<28;i++) if(output[i]!=0xdeadbeef) return 67;
    n=0;u64 vs=(u64)vertex,ps=(u64)pixel,target=(u64)pixels;
    reg(0x76,0xc8,(u32)(vs>>8));reg(0x76,0xc9,(u32)(vs>>40));reg(0x76,0xcb,4<<1);
    descriptor(0xcc,(u64)output,16,3,77);
    reg(0x76,0x8,(u32)(ps>>8));reg(0x76,0x9,(u32)(ps>>40));reg(0x76,0xb,0);
    reg(0x69,0x1b3,0);reg(0x69,0x1b4,0);
    reg(0x69,0x318,(u32)(target>>8));reg(0x69,0x390,(u32)(target>>40));
    reg(0x69,0x3b0,(319<<14)|179);reg(0x69,0x3b8,0);reg(0x69,0x31c,10<<2);reg(0x69,0x8e,0xf);
    reg(0x69,0x10f,0x43200000);reg(0x69,0x110,0x43200000);
    reg(0x69,0x111,0x42b40000);reg(0x69,0x112,0x42b40000);reg(0x79,0x242,4);
    commands[n++]=0xc0012d00;commands[n++]=3;commands[n++]=2;
    result=finish(2);if(result) return result;
    if(flip_slot(video,0,1,1)) return 68;
    sleep_slot(800000);close_slot(video);return 0;
}
