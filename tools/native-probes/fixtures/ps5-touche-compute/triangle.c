// Synthetic gfx10 vertex/pixel programs. All triangle pixels must come from GPU rasterization.
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
static volatile u32 completed;
static u32 commands[256];
#ifdef TEST_TEXTURE
static volatile u32 texel[128] __attribute__((aligned(256)));
#endif
#ifdef TEST_DEPTH
static u32 depth[320*180] __attribute__((aligned(256)));
#endif
static struct { u64 address; u32 count; u32 reserved; } submission;
static const u32 vertex[] __attribute__((aligned(256))) = {
    0x36000a81, // v_and_b32 v0, 1, v5 (vertex index)
    0x36020a82, // v_and_b32 v1, 2, v5
    0x7e000d00, 0x7e020d01, // v_cvt_f32_u32 v0,v0 / v1,v1
    0x100000ff,0x3fc00000, // v_mul_f32 v0,1.5,v0
    0x100202ff,0x3f400000, // v_mul_f32 v1,0.75,v1
    0x060000ff,0xbf400000, // v_add_f32 v0,-0.75,v0
    0x060202ff,0xbf400000, // v_add_f32 v1,-0.75,v1
#ifdef TEST_DEPTH
    0x7e040208, 0x7e0602f2, // merged ES user data starts at s8: z=s8, w=1
#else
    0x7e040280, 0x7e0602f2, // z=0, w=1
#endif
    0xf80008cf,0x03020100, // exp pos0 v0,v1,v2,v3 done
    0xbf810000
};
static const u32 pixel[] __attribute__((aligned(256))) = {
#ifdef TEST_TEXTURE
#ifdef TEST_TEXTURE_SELECT
    0x7e00020c, 0x7e02020c, // u=v=s12
#else
    0x7e0002f0, 0x7e0202f0, // sample at (0.5,0.5)
#endif
    0xf09c0f08,0x00400400, // image_sample_lz v[4:7],v[0:1],s[0:7],s[8:11],dim2D
    0xbf8c0f70, // s_waitcnt vmcnt(0)
    0xf800180f,0x07060504,0xbf810000
#else
    0x7e000200, 0x7e020280, 0x7e040201, 0x7e060202, // R=s0, G=0, B=s1, A=s2
    0xf800180f,0x03020100, 0xbf810000
#endif
};
static int n;
#if INDEX_BITS == 16
static const unsigned short indices[3]={0,1,3};
static const unsigned short stale_indices[3]={0,1,2};
#elif INDEX_BITS == 32
static const u32 indices[3]={0,1,3};
static const u32 stale_indices[3]={0,1,2};
#endif
static void draw(void) {
#ifdef INDEX_BITS
    u64 address=(u64)indices;
    commands[n++]=0xc0002a00; commands[n++]=(INDEX_BITS==32 ? 1 : 0);
    // DRAW_INDEX_2 supplies its own address, without an INDEX_BASE packet.
    commands[n++]=0xc0042700; commands[n++]=3;
    commands[n++]=(u32)address; commands[n++]=(u32)(address>>32);
    commands[n++]=3; commands[n++]=0;
#else
    commands[n++]=0xc0012d00; commands[n++]=3; commands[n++]=2;
#endif
}
static void reg(u32 opcode,u32 index,u32 value) {
    commands[n++]=0xc0010000|(opcode<<8);
    commands[n++]=index; commands[n++]=value;
}
int _start(void) {
    int video=open_slot(0,0,0,(void*)0);
    if(video<=0) return 70;
    if(attribute_slot(attribute,0x80000000,1,0,320,180,320)!=0) return 71;
    addresses[0]=(u64)pixels;
    if(register_slot(video,0,addresses,1,attribute)<0) return 72;
    for(int i=0;i<320*180;i++) pixels[i]=0xff202020;
    if(flip_slot(video,0,1,0)!=0) return 73;
    sleep_slot(3000000);
    u64 vs=(u64)vertex, ps=(u64)pixel, target=(u64)pixels, label=(u64)&completed;
    reg(0x76,0xc8,(u32)(vs>>8)); reg(0x76,0xc9,(u32)(vs>>40));
    reg(0x76,0x8,(u32)(ps>>8)); reg(0x76,0x9,(u32)(ps>>40));
    reg(0x76,0xcb,0); reg(0x76,0xb,3<<1); // PS reads three user SGPRs
#ifdef TEST_DEPTH
    reg(0x76,0xcb,1<<1); reg(0x76,0xcc,0x3e800000); // vertex Z=0.25
    u64 ds=(u64)depth;
    reg(0x69,0x7,319|(179<<16)); reg(0x69,0x10,3); // D32, 320x180
    reg(0x69,0x12,(u32)(ds>>8)); reg(0x69,0x14,(u32)(ds>>8));
    reg(0x69,0x1a,(u32)(ds>>40)); reg(0x69,0x1c,(u32)(ds>>40));
    reg(0x69,0xb,0x3f800000); reg(0x69,0,1); // clear depth to 1
    reg(0x69,0x200,0x16); // test/write enabled, LESS
#endif
    reg(0x76,0xc,0x3f800000); reg(0x76,0xd,0); // red
    reg(0x76,0xe,0x3f800000); // alpha 1
#ifdef TEST_TEXTURE
    texel[0]=0xff0000ff; // RGBA8 red
    u64 texture=(u64)texel;
    reg(0x76,0xb,12<<1);
    reg(0x76,0xc,(u32)(texture>>8));
    reg(0x76,0xd,(u32)(texture>>40)|(56u<<20)); // RGBA8 UNORM, 1x1
    reg(0x76,0xe,0); reg(0x76,0xf,0x90000fac);
    for(int i=4;i<12;i++) reg(0x76,0xc+i,0);
#ifdef TEST_TEXTURE_SELECT
    texel[1]=0xff00ff00; // green traps detect swapped/missing coordinates
    texel[64]=0xff00ff00; texel[65]=0xffff0000; // second row at 256-byte pitch
    reg(0x76,0xb,13<<1);
    reg(0x76,0xd,(u32)(texture>>40)|(56u<<20)|(1u<<30)); // width=2
    reg(0x76,0xe,1u<<14); // height=2
    reg(0x76,0x10,1); // pitch=2 (linear upload rounds to hardware row alignment)
    reg(0x76,0x18,0x3e800000); // sample red at (0.25,0.25)
#endif
#endif
    reg(0x69,0x1b3,0); reg(0x69,0x1b4,0); // constant PS has no interpolated inputs
    reg(0x69,0x318,(u32)(target>>8)); reg(0x69,0x390,(u32)(target>>40));
    reg(0x69,0x3b0,(319<<14)|179); reg(0x69,0x3b8,0);
    reg(0x69,0x31c,10<<2); reg(0x69,0x8e,0xf);
    reg(0x69,0x10f,0x43200000); reg(0x69,0x110,0x43200000); // viewport X scale/offset 160
    reg(0x69,0x111,0x42b40000); reg(0x69,0x112,0x42b40000); // viewport Y scale/offset 90
#ifdef TEST_SCISSOR
    reg(0x69,0xc,80|(45<<16)); reg(0x69,0xd,240|(135<<16));
#endif
    reg(0x79,0x242,4); // triangle list
    draw();
#ifdef TEST_DEPTH
    // DB clear draws initialize the attachment; geometry depth is written by
    // a separate ordinary draw after disabling DEPTH_CLEAR_ENABLE.
    reg(0x69,0,0);
    draw();
#endif
    commands[n++]=0xc0064900; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=1; commands[n++]=0; commands[n++]=0;
    submission.address=(u64)commands; submission.count=(u32)n;
    if(submit_slot(&submission)!=0) return 74;
    for(int i=0;i<5000 && !completed;i++) sleep_slot(1000);
    if(completed!=1) return 75;
    if(flip_slot(video,0,1,1)!=0) return 76;
    sleep_slot(3000000);
    // Reuse the shader and target, changing only the scalar color inputs.
    n=0;
#ifdef TEST_TEXTURE
#ifdef TEST_TEXTURE_SELECT
    reg(0x76,0x18,0x3f400000); // sample blue at (0.75,0.75)
#else
    texel[0]=0xffff0000; // same descriptor/address, now blue
#endif
#else
    reg(0x76,0xc,0); reg(0x76,0xd,0x3f800000); // blue
#endif
#ifdef TEST_DEPTH
    reg(0x69,0,0); reg(0x76,0xcc,0x3f400000); // keep depth; farther Z=0.75 must fail
#endif
#ifdef TEST_BLEND
    reg(0x76,0xe,0x3f000000); // source alpha 0.5
    // RGB: src*srcAlpha + dst*(1-srcAlpha). Alpha: src + dst*(1-srcAlpha).
    reg(0x69,0x1e0,(1u<<30)|(1u<<29)|(5u<<24)|(1u<<16)|(5u<<8)|4u);
#endif
#ifdef INDEX_BITS
    // A stale INDEX_BASE must not override the next DRAW_INDEX_2 inline base.
    u64 stale=(u64)stale_indices;
    commands[n++]=0xc0012600;
    commands[n++]=(u32)stale; commands[n++]=(u32)(stale>>32);
#endif
    draw();
    commands[n++]=0xc0064900; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=2; commands[n++]=0; commands[n++]=0;
    submission.count=(u32)n;
    if(submit_slot(&submission)!=0) return 77;
    for(int i=0;i<5000 && completed!=2;i++) sleep_slot(1000);
    if(completed!=2) return 78;
    if(flip_slot(video,0,1,2)!=0) return 79;
    sleep_slot(3000000);
#if defined(TEST_DEPTH) || defined(TEST_TEXTURE)
    n=0;
#ifdef TEST_DEPTH
    reg(0x76,0xcc,0x3e000000); // nearer blue Z=0.125 must pass
#endif
    // Texture mode repeats unchanged blue after the CPU update.
    draw();
    commands[n++]=0xc0064900; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=3; commands[n++]=0; commands[n++]=0;
    submission.count=(u32)n;
    if(submit_slot(&submission)!=0) return 80;
    for(int i=0;i<5000 && completed!=3;i++) sleep_slot(1000);
    if(completed!=3) return 81;
    if(flip_slot(video,0,1,3)!=0) return 82;
    sleep_slot(3000000);
#endif
    close_slot(video);
    return 0;
}
