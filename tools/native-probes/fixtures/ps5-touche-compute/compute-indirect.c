// SDK-free compute-to-dispatch test; counters verify actual GPU invocation counts.
typedef unsigned int u32;
typedef unsigned long long u64;
#ifndef INDIRECT_BASE_RELATIVE
#define INDIRECT_BASE_RELATIVE 0
#endif
#ifndef INDIRECT_COMPUTE_QUEUE
#define INDIRECT_COMPUTE_QUEUE 0
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
static unsigned char attribute[40];
static u64 addresses[1];
static volatile u32 counters[5][16] __attribute__((aligned(256)));
static volatile u32 dimensions[32] __attribute__((aligned(256)));
static volatile u32 completed;
static u32 commands[128];
static int n;
static struct { u64 address; u32 count; u32 reserved; } submission;
static const u32 producer[] __attribute__((aligned(256))) = {
    0xBFA10001,0x7E000200,0xE0700000,0x80020000,0xBF810000
};
// BUFFER_ATOMIC_ADD encoding independently checked against LLVM gfx10 MC tests.
static const u32 consumer[] __attribute__((aligned(256))) = {
    0xBFA10001,0x7E000281,0xE0C80000,0x80020000,0xBF810000
};
static void bind(u64 code,u64 buffer,u32 input) {
    commands[n++]=0xc0027602; commands[n++]=0x20c;
    commands[n++]=(u32)(code>>8); commands[n++]=(u32)(code>>40);
    commands[n++]=0xc0017602; commands[n++]=0x213; commands[n++]=12<<1;
    commands[n++]=0xc0037602; commands[n++]=0x207;
    commands[n++]=1; commands[n++]=1; commands[n++]=1;
    commands[n++]=0xc0017602; commands[n++]=0x240; commands[n++]=input;
    commands[n++]=0xc0047602; commands[n++]=0x248;
    commands[n++]=(u32)buffer; commands[n++]=(u32)(buffer>>32);
    commands[n++]=64; commands[n++]=0;
}
static void direct(void) {
    commands[n++]=0xc0031502;
    commands[n++]=1; commands[n++]=1; commands[n++]=1; commands[n++]=5;
}
int _start(void) {
    int video=open_slot(0,0,0,(void*)0);
    if(video<=0) return 80;
    if(attribute_slot(attribute,0x80000000,1,0,320,180,320)!=0) return 81;
    addresses[0]=(u64)pixels;
    if(register_slot(video,0,addresses,1,attribute)<0) return 82;
    for(int i=0;i<320*180;i++) pixels[i]=0xff102030;
    if(flip_slot(video,0,1,0)!=0) return 83;
    sleep_slot(300000);
    int result=0;
    const u32 expectedCounts[5]={1,2,1,2,0};
    const u32 initialCounts[5]={1,1,2,0,2};
    for(int pass=0;pass<5;pass++) {
        u32 expected=expectedCounts[pass];
        int axis=INDIRECT_BASE_RELATIVE ? pass%3 : 0;
        int dimOffset=INDIRECT_BASE_RELATIVE ? 4 : 0;
        volatile u32* dims=&dimensions[dimOffset];
        for(int i=0;i<16;i++) {
            counters[pass][i]=0xdeadbeef;
        }
        for(int i=0;i<32;i++) dimensions[i]=0xdeadbeef;
        // A wrong base-relative offset reads a plausible but incorrect 1x1x1.
        if(INDIRECT_BASE_RELATIVE) dimensions[0]=dimensions[1]=dimensions[2]=1;
        counters[pass][0]=0;
        dims[0]=dims[1]=dims[2]=1;
        dims[axis]=initialCounts[pass]; // stale nonzero and zero sizes
        completed=0; n=0;
        if(pass) {
            bind((u64)producer,(u64)&dims[axis],expected);
            direct();
        }
        bind((u64)consumer,(u64)counters[pass],0);
        if(pass) {
            if(INDIRECT_BASE_RELATIVE) {
                u64 base=(u64)dimensions;
                commands[n++]=0xc0021102; commands[n++]=1;
                commands[n++]=(u32)base; commands[n++]=(u32)(base>>32);
                commands[n++]=0xc0011602; commands[n++]=16; commands[n++]=0x41;
            } else {
                u64 address=(u64)dims;
                commands[n++]=0xc0021602;
                commands[n++]=(u32)address; commands[n++]=(u32)(address>>32); commands[n++]=5;
            }
        } else direct(); // independently establish that the atomic consumer works
        u64 label=(u64)&completed;
        commands[n++]=0xc0064902; commands[n++]=0x14; commands[n++]=1u<<29;
        commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
        commands[n++]=1; commands[n++]=0; commands[n++]=0;
        submission.address=(u64)commands; submission.count=(u32)n;
        int submitted=INDIRECT_COMPUTE_QUEUE
            ? submit_acb_slot(7,&submission) : submit_slot(&submission);
        if(submitted!=0) return 84;
        for(int i=0;i<5000 && completed!=1;i++) sleep_slot(1000);
        if(completed!=1) result=85;
        else if(counters[pass][0]!=expected) result=90+pass;
        else if(pass && dims[axis]!=expected) result=110+pass;
        for(int i=0;i<3;i++) if(i!=axis && dims[i]!=1) result=120;
        for(int i=1;i<16;i++) if(counters[pass][i]!=0xdeadbeef) result=121;
        for(int i=0;i<32;i++) {
            if(i>=dimOffset && i<dimOffset+3) continue;
            u32 guard=INDIRECT_BASE_RELATIVE && i<3 ? 1 : 0xdeadbeef;
            if(dimensions[i]!=guard) result=122;
        }
        if(result) break;
    }
    for(int i=0;i<320*180;i++) pixels[i]=result ? 0xffa02020 : 0xff20a060;
    flip_slot(video,0,1,1); sleep_slot(200000); close_slot(video);
    return result;
}
