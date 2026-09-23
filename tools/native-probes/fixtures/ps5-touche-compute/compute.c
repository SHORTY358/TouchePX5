// SDK-free guest: submit a synthetic gfx10 compute shader and check GPU readback.
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
static volatile u32 output[16] __attribute__((aligned(256)));
static volatile u32 completed;
static u32 commands[64];
static struct { u64 address; u32 count; u32 reserved; } submission;
// Based on Gen5ComputeFixtures.ExecStore; s0 supplies the changing float input.
static const u32 shader[] __attribute__((aligned(256))) = {
    0xBFA10001,
    0x7E000200, 0x7E0202FF,0x40100000, 0x7E0402FF,0x41200000,
    0x56040300,
    0x7E0602FF,0x7FFFFFFF, 0x7E0802FF,0x00010003,
    0xD56C0005,0x00020903, 0xD56B0006,0x00020903,
    0xE0700000,0x80020200, 0xE0700004,0x80020500, 0xE0700008,0x80020600,
    0xBEFE0380, 0xE070000C,0x80020200,
    0xBEFE03C1, 0xE0700010,0x80020000, 0xBF810000
};
int _start(void) {
    int video=open_slot(0,0,0,(void*)0);
    if(video<=0) return 60;
    if(attribute_slot(attribute,0x80000000,1,0,320,180,320)!=0) return 61;
    addresses[0]=(u64)pixels;
    if(register_slot(video,0,addresses,1,attribute)<0) return 62;
    for(int i=0;i<320*180;i++) pixels[i]=0xff102030;
    if(flip_slot(video,0,1,0)!=0) return 63;
    // Start the Vulkan presentation worker before compute work is queued.
    sleep_slot(300000);
    u64 code=(u64)shader, buffer=(u64)output, label=(u64)&completed;
    int result=0;
    for(int pass=0;pass<2;pass++) {
    for(int i=0;i<16;i++) output[i]=0xdeadbeef;
    completed=0;
    u32 input=pass ? 0x40000000 : 0x3fc00000;
    int n=0;
    commands[n++]=0xc0027602; commands[n++]=0x20c;
    commands[n++]=(u32)(code>>8); commands[n++]=(u32)(code>>40);
    commands[n++]=0xc0017602; commands[n++]=0x213; commands[n++]=12<<1;
    commands[n++]=0xc0037602; commands[n++]=0x207;
    commands[n++]=1; commands[n++]=1; commands[n++]=1;
    commands[n++]=0xc0017602; commands[n++]=0x240; commands[n++]=input;
    commands[n++]=0xc0047602; commands[n++]=0x248;
    commands[n++]=(u32)buffer; commands[n++]=(u32)(buffer>>32);
    commands[n++]=64; commands[n++]=0;
    commands[n++]=0xc0031502;
    commands[n++]=1; commands[n++]=1; commands[n++]=1; commands[n++]=5;
    // Standard RELEASE_MEM publishes prior GPU writes before the completion label.
    commands[n++]=0xc0064902; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=1; commands[n++]=0; commands[n++]=0;
    submission.address=(u64)commands; submission.count=(u32)n;
    if(submit_slot(&submission)!=0) return 64;
    for(int i=0;i<5000 && !completed;i++) sleep_slot(1000);
    if(completed!=1) result=65;
    else if(output[0]!=(pass ? 0x41680000 : 0x41560000)) result=66;
    else if(output[1]!=0x00008001 || output[2]!=0x7ffefffd) result=67;
    else if(output[3]!=0xdeadbeef || output[4]!=input) result=68;
    for(int i=5;i<16;i++) if(output[i]!=0xdeadbeef) result=69;
    if(result) break;
    }
    for(int i=0;i<320*180;i++) pixels[i]=result ? 0xffa02020 : 0xff20a060;
    flip_slot(video,0,1,1);
    sleep_slot(200000);
    close_slot(video);
    return result;
}
