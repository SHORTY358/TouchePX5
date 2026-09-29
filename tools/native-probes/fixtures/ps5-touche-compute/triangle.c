// Synthetic gfx10 vertex/pixel programs. All triangle pixels must come from GPU rasterization.
typedef unsigned int u32;
typedef unsigned long long u64;
#ifndef FRAME_PAUSE_US
#define FRAME_PAUSE_US 3000000
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
#ifdef TEST_DMA_IMAGE
static u32 dma_image_source[320*180] __attribute__((aligned(256)));
#ifdef TEST_DMA_IMAGE_SPAN
#define DMA_FIRST_PIXEL (320*45+80)
#define DMA_END_PIXEL (320*135+240)
#else
#define DMA_FIRST_PIXEL 0
#define DMA_END_PIXEL (320*90)
#endif
#endif
static unsigned char attribute[40];
static u64 addresses[1];
static volatile u32 completed;
static u32 commands[512];
#ifdef TEST_TEXTURE
#ifdef TEST_MIP_2D
static volatile u32 texel[384] __attribute__((aligned(256)));
#else
static volatile u32 texel[192] __attribute__((aligned(256)));
#endif
#endif
#ifdef TEST_DEPTH
static u32 depth[320*180] __attribute__((aligned(256)));
#endif
static struct { u64 address; u32 count; u32 reserved; } submission;
#ifdef TEST_DMA_IMAGE_CROSS_QUEUE
static u32 dma_image_producer[512], dma_image_consumer[15];
static volatile u32 dma_image_ready;
#endif
#ifdef TEST_CROSS_QUEUE
static u32 producer_commands[4][64];
static u32 producer_lengths[4];
static volatile u32 producer_labels[4];
static u32 producer_pending, producer_submitted;
#endif
static int submit_graphics(void) {
#ifdef TEST_DMA_IMAGE_CROSS_QUEUE
    if(completed>=1) {
        u32 before=completed, expected=before+1, count=submission.count;
        if(count<8 || count>512) return 94;
        for(u32 i=0;i<count;i++) dma_image_producer[i]=commands[i];
        dma_image_ready=0;
        u64 ready=(u64)&dma_image_ready, done=(u64)&completed;
        // Producer completion signals a separate label. Only the waiting
        // graphics consumer can publish the completion observed by the guest.
        dma_image_producer[count-5]=(u32)ready;
        dma_image_producer[count-4]=(u32)(ready>>32);
        u32 consumer[]={0xc0053c00,0x13,(u32)ready,(u32)(ready>>32),expected,0xffffffff,4,
            0xc0064900,0x14,1u<<29,(u32)done,(u32)(done>>32),expected,0,0};
        for(u32 i=0;i<15;i++) dma_image_consumer[i]=consumer[i];
        // The minimal guest linker does not emit relocations for pointer-valued
        // constant aggregates. Materialize this address at runtime instead.
        volatile u64 consumer_address=(u64)dma_image_consumer;
        struct {u64 address;u32 count;u32 reserved;} wait_packet={consumer_address,15,0};
        int result=submit_slot(&wait_packet);
        if(result) return result;
        sleep_slot(20000);
        if(completed!=before) return 95;
        struct {u64 address;u32 count;u32 reserved;} producer_packet={(u64)dma_image_producer,count,0};
        return submit_acb_slot(7,&producer_packet);
    }
#endif
#ifdef TEST_CROSS_QUEUE
    u32 before=completed;
#endif
    int result=submit_slot(&submission);
#ifdef TEST_CROSS_QUEUE
    if(result) return result;
    // The consumer must suspend; no producer has been submitted yet.
#ifdef TEST_CROSS_QUEUE_REUSE
    sleep_slot(750000); // exceed the default historical-signal fallback age
#else
    sleep_slot(20000);
#endif
    if(completed!=before) return 90;
    while(producer_submitted<producer_pending) {
        u32 index=producer_submitted++;
        struct { u64 address; u32 count; u32 reserved; } packet={
            (u64)producer_commands[index],producer_lengths[index],0};
        result=submit_acb_slot(7,&packet);
        if(result) return result;
    }
#endif
    return result;
}
static const u32 vertex[] __attribute__((aligned(256))) = {
    0x36000a81, // v_and_b32 v0, 1, v5 (vertex index)
    0x36020a82, // v_and_b32 v1, 2, v5
    0x7e000d00, 0x7e020d01, // v_cvt_f32_u32 v0,v0 / v1,v1
#ifdef TEST_TEXTURE_UV
    0x7e0c0300, 0x100e02f0, // v6=u=v0; v7=v=v1*0.5
#endif
    0x100000ff,0x3fc00000, // v_mul_f32 v0,1.5,v0
    0x100202ff,0x3f400000, // v_mul_f32 v1,0.75,v1
    0x060000ff,0xbf400000, // v_add_f32 v0,-0.75,v0
    0x060202ff,0xbf400000, // v_add_f32 v1,-0.75,v1
#ifdef TEST_DEPTH
    0x7e040208, 0x7e0602f2, // merged ES user data starts at s8: z=s8, w=1
#else
    0x7e040280, 0x7e0602f2, // z=0, w=1
#endif
#ifdef TEST_TEXTURE_UV
#ifdef TEST_PERSPECTIVE
    0x06100cf2, // v8=1+u: W is 1,2,1 at the three vertices
    0x10000108,0x10020308,0x7e060308, // scale clip X/Y by W; v3=W
#endif
    0xf800020f,0x03020706, // exp param0 v6,v7,v2,v3
#endif
    0xf80008cf,0x03020100, // exp pos0 v0,v1,v2,v3 done
    0xbf810000
};
static const u32 pixel[] __attribute__((aligned(256))) = {
#ifdef TEST_TEXTURE
#ifdef TEST_TEXTURE_SELECT
#ifdef TEST_TEXTURE_UV
    0xc8000000,0xc8010001, // v_interp_p1/p2 v0, attr0.x
    0xc8040100,0xc8050101, // v_interp_p1/p2 v1, attr0.y
#else
    0x7e00020c, 0x7e02020c, // u=v=s12
#endif
#else
    0x7e0002f0, 0x7e0202f0, // sample at (0.5,0.5)
#endif
#ifdef TEST_MIP
#ifdef TEST_MIP_2D
    0x7e0002ff,0x3f400000,0x7e0202ff,0x3f400000, // u=v=0.75 independent of LOD
#endif
    0x7e04020c, // v2=explicit LOD from s12
    0xf0900f08,0x00400400, // image_sample_l v[4:7],v[0:2],s[0:7],s[8:11],dim2D
#else
    0xf09c0f08,0x00400400, // image_sample_lz v[4:7],v[0:1],s[0:7],s[8:11],dim2D
#endif
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
#ifdef TEST_INDIRECT
// The command processor copies firstInstance into the selected user register.
// This test deliberately treats its raw float bits as vertex depth.
#ifdef TEST_INDIRECT_GPU_ARGS
static volatile u32 indirect_args[][4] __attribute__((aligned(16))) = {
#else
static const u32 indirect_args[][4] __attribute__((aligned(16))) = {
#endif
    {0,0,0,0}, // ignoring the nonzero packet offset draws nothing
    {3,1,0,0x3e800000}, {3,1,0,0x3e800000},
    {3,1,0,0x3f400000}, {3,1,0,0x3e000000}
};
static u32 indirect_draw;
#ifdef TEST_INDIRECT_GPU_ARGS
static const u32 args_shader[] __attribute__((aligned(256))) = {
    0xBFA10001, 0x7E000200,
    0xE070000C,0x80020000, // write only firstInstance at offset 12
    0xBF810000
};
static const u32 args_depth[4]={0x3e800000,0x3e800000,0x3f400000,0x3e000000};
#endif
#ifdef TEST_INDIRECT_MULTI
static const unsigned short multi_indices[3]={0,1,2};
#ifdef TEST_INDIRECT_GPU_COUNT
static volatile u32 gpu_count[16] __attribute__((aligned(256)));
static const u32 count_shader[] __attribute__((aligned(256))) = {
    0xBFA10001, 0x7E000200, // v0 = raw s0
    0xE0700000,0x80020000, // buffer_store_dword v0, s[8:11], offset=0
    0xBF810000
};
// GPU count=2 includes empty+real draw, but excludes a third poison draw.
static const u32 multi_args[][8] __attribute__((aligned(16))) = {
    {0}, {0}, {3,1,0,0,0x3e800000}, {3,1,0,0,0},
    {0}, {3,1,0,0,0x3e800000}, {3,1,0,0,0},
    {0}, {3,1,0,0,0x3f400000}, {3,1,0,0,0},
    {0}, {3,1,0,0,0x3e000000}, {3,1,0,0,0}
};
#else
// Two records per packet, 32-byte stride: empty record then a real indexed draw.
static const u32 multi_args[][8] __attribute__((aligned(16))) = {
    {0}, {0}, {3,1,0,0,0x3e800000}, {0}, {3,1,0,0,0x3e800000},
    {0}, {3,1,0,0,0x3f400000}, {0}, {3,1,0,0,0x3e000000}
};
#endif
#endif
#endif
static void draw(void) {
#ifdef TEST_INDIRECT
#ifdef TEST_INDIRECT_MULTI
    u64 address=(u64)multi_args, index=(u64)multi_indices;
#ifdef TEST_INDIRECT_GPU_COUNT
    u64 code=(u64)count_shader, count_buffer=(u64)gpu_count;
    gpu_count[0]=1; // stale CPU count selects only the empty record
    commands[n++]=0xc0027602; commands[n++]=0x20c;
    commands[n++]=(u32)(code>>8); commands[n++]=(u32)(code>>40);
    commands[n++]=0xc0017602; commands[n++]=0x213; commands[n++]=12<<1;
    commands[n++]=0xc0037602; commands[n++]=0x207;
    commands[n++]=1; commands[n++]=1; commands[n++]=1;
    commands[n++]=0xc0017602; commands[n++]=0x240; commands[n++]=2;
    commands[n++]=0xc0047602; commands[n++]=0x248;
    commands[n++]=(u32)count_buffer; commands[n++]=(u32)(count_buffer>>32);
    commands[n++]=64; commands[n++]=0;
    commands[n++]=0xc0031502;
    commands[n++]=1; commands[n++]=1; commands[n++]=1; commands[n++]=5;
#endif
    commands[n++]=0xc0017600; commands[n++]=0xcc; commands[n++]=0x3f800000;
    commands[n++]=0xc0002a00; commands[n++]=0; // 16-bit indices
    commands[n++]=0xc0012600; commands[n++]=(u32)index; commands[n++]=(u32)(index>>32);
    commands[n++]=0xc0001300; commands[n++]=3;
    commands[n++]=0xc0021100; commands[n++]=1;
    commands[n++]=(u32)address; commands[n++]=(u32)(address>>32);
    commands[n++]=0xc0083800;
#ifdef TEST_INDIRECT_GPU_COUNT
    commands[n++]=(++indirect_draw)*96-64;
    commands[n++]=0xcd; commands[n++]=0xcc; commands[n++]=1u<<30;
    commands[n++]=3; commands[n++]=(u32)count_buffer; commands[n++]=(u32)(count_buffer>>32);
#else
    commands[n++]=(++indirect_draw)*64-32;
    commands[n++]=0xcd; commands[n++]=0xcc; commands[n++]=0;
    commands[n++]=2; commands[n++]=0; commands[n++]=0;
#endif
    commands[n++]=32; commands[n++]=0;
#else
    u64 address=(u64)indirect_args;
    u32 record=++indirect_draw;
#ifdef TEST_INDIRECT_GPU_ARGS
    u64 code=(u64)args_shader, buffer=(u64)&indirect_args[record][0];
    indirect_args[record][3]=0x3f800000; // nonempty record, stale Z=1
#ifdef TEST_CROSS_QUEUE
    int producer_start=n;
#endif
    commands[n++]=0xc0027602; commands[n++]=0x20c;
    commands[n++]=(u32)(code>>8); commands[n++]=(u32)(code>>40);
    commands[n++]=0xc0017602; commands[n++]=0x213; commands[n++]=12<<1;
    commands[n++]=0xc0037602; commands[n++]=0x207;
    commands[n++]=1; commands[n++]=1; commands[n++]=1;
    commands[n++]=0xc0017602; commands[n++]=0x240; commands[n++]=args_depth[record-1];
    commands[n++]=0xc0047602; commands[n++]=0x248;
    commands[n++]=(u32)buffer; commands[n++]=(u32)(buffer>>32);
    commands[n++]=16; commands[n++]=0;
    commands[n++]=0xc0031502;
    commands[n++]=1; commands[n++]=1; commands[n++]=1; commands[n++]=5;
#ifdef TEST_CROSS_QUEUE
    u32 index=record-1, length=(u32)(n-producer_start);
    for(u32 i=0;i<length;i++) producer_commands[index][i]=commands[producer_start+i];
#ifdef TEST_CROSS_QUEUE_REUSE
    u32 label_index=index%2;
#else
    u32 label_index=index;
#endif
    producer_labels[label_index]=0;
    u64 ready=(u64)&producer_labels[label_index];
    u32* producer=producer_commands[index];
#if defined(TEST_CROSS_QUEUE_WRITE_DATA) || defined(TEST_CROSS_QUEUE_DMA)
    // Signal and reset the same label with WRITE_DATA or consecutive DMA fills. The
    // suspended consumer must retain the signal even though final memory is 0.
    // Producer 1 can finish before its wait is reached behind the first draw;
    // retain that signal instead of demanding historical-value replay.
    if(index!=1) {
#ifdef TEST_CROSS_QUEUE_DMA
        for(u32 reset=0;reset<2;reset++) {
            producer[length++]=0xc0055002; producer[length++]=2u<<29;
            producer[length++]=reset ? 0 : 1; producer[length++]=0;
            producer[length++]=(u32)ready; producer[length++]=(u32)(ready>>32);
            producer[length++]=4;
        }
#else
        producer[length++]=0xc0043702; producer[length++]=(5u<<8)|(1u<<16);
        producer[length++]=(u32)ready; producer[length++]=(u32)(ready>>32);
        producer[length++]=1; producer[length++]=0;
#endif
    } else
#endif
    {
        producer[length++]=0xc0064902; producer[length++]=0x14; producer[length++]=1u<<29;
        producer[length++]=(u32)ready; producer[length++]=(u32)(ready>>32);
        producer[length++]=1; producer[length++]=0; producer[length++]=0;
    }
    producer_lengths[index]=length; producer_pending=record;
    n=producer_start;
    // WAIT_REG_MEM: equal, memory space, 32-bit signal from the ACB producer.
    commands[n++]=0xc0053c00; commands[n++]=0x13;
    commands[n++]=(u32)ready; commands[n++]=(u32)(ready>>32);
    commands[n++]=1; commands[n++]=0xffffffff; commands[n++]=4;
#endif
#endif
    commands[n++]=0xc0017600; commands[n++]=0xcc; commands[n++]=0x3f800000;
    commands[n++]=0xc0021100; commands[n++]=1;
    commands[n++]=(u32)address; commands[n++]=(u32)(address>>32);
    commands[n++]=0xc0032400; commands[n++]=record*16;
    commands[n++]=0xcd; commands[n++]=0xcc; commands[n++]=2;
#endif
#else
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
    sleep_slot(FRAME_PAUSE_US);
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
#ifdef TEST_TEXTURE_UV
    texel[1]=0xffff0000; texel[65]=0xffffffff; // red/blue, green/white
#endif
    reg(0x76,0xb,13<<1);
    reg(0x76,0xd,(u32)(texture>>40)|(56u<<20)|(1u<<30)); // width=2
    reg(0x76,0xe,1u<<14); // height=2
    reg(0x76,0x10,1); // pitch=2 (linear upload rounds to hardware row alignment)
    reg(0x76,0x18,0x3e800000); // sample red at (0.25,0.25)
#ifdef TEST_MIP
    // GFX10 linear allocation: smaller mip precedes mip0 (AMD AddrLib).
    texel[64]=texel[65]=texel[128]=texel[129]=0xff0000ff; // base level red at +256
    texel[0]=0xffff0000; // 1x1 mip at allocation start
#ifdef TEST_MIP_SRGB
    reg(0x76,0xd,(u32)(texture>>40)|(130u<<20)|(1u<<30)); // RGBA8 sRGB
    texel[64]=texel[65]=texel[128]=texel[129]=0x80000080; // R=128, A=128
    texel[0]=0x80800000; // B=128, A=128
#endif
#ifdef TEST_MIP_2D
    // Two padded rows for mip1, then four padded rows for mip0.
    texel[0]=0xff0000ff; texel[1]=texel[64]=0xff00ff00; texel[65]=0xffff0000;
    for(u32 y=0;y<4;y++) for(u32 x=0;x<4;x++) texel[128+y*64+x]=0xff0000ff;
    reg(0x76,0xd,(u32)(texture>>40)|(56u<<20)|(3u<<30)); // width=4
    reg(0x76,0xe,3u<<14); // height=4
    reg(0x76,0x10,3); // pitch=4, hardware rows still 256 bytes
#ifdef TEST_MIP_ODD
    // Mip1 storage is ceil(5/2)xceil(3/2)=3x2, but its sampled extent
    // is floor(5/2)x floor(3/2)=2x1. Poison unused physical texels green.
    texel[0]=0xff0000ff; texel[1]=0xffff0000; texel[2]=0xff00ff00;
    texel[64]=texel[65]=texel[66]=0xff00ff00;
    for(u32 y=0;y<3;y++) for(u32 x=0;x<5;x++) texel[128+y*64+x]=0xff0000ff;
    reg(0x76,0xd,(u32)(texture>>40)|(56u<<20)); // width-1 low 2 bits = 0
    reg(0x76,0xe,1u|(2u<<14)); // width-1 high bits = 1, height-1 = 2
    reg(0x76,0x10,4); // pitch=5
#endif
#endif
    reg(0x76,0xf,0x90010fac); // last mip=1
#ifdef TEST_MIP_BASE_ONLY
    reg(0x76,0xf,0x90000fac); // resource has two levels, but the view exposes only mip0
#endif
#ifdef TEST_MIP_LOWER_VIEW
    reg(0x76,0xf,0x90011fac); // view exposes only mip1; shader LOD0 is relative to this view
#endif
    reg(0x76,0x11,1<<4); // max mip=1
    reg(0x76,0x15,256u<<12); // sampler max LOD=1
    reg(0x76,0x16,1u<<26); // nearest mip selection
    reg(0x76,0x18,0); // sample mip0
#ifdef TEST_MIP_BC3
    // BC3: alpha endpoints/selectors precede RGB565 endpoints/selectors.
    // Each 16-byte block selects endpoint zero for every color and alpha.
    for(u32 i=0;i<sizeof(texel)/sizeof(texel[0]);i++) texel[i]=0;
    texel[0]=128; texel[2]=0x001f; // blue lower mip, alpha 128
    for(u32 y=0;y<2;y++) for(u32 x=0;x<2;x++) {
        u32 block=64+y*64+x*4;
        texel[block]=128; texel[block+2]=0xf800;
    }
    reg(0x76,0xd,(u32)(texture>>40)|(173u<<20)|(3u<<30));
    reg(0x76,0xe,1u|(7u<<14)); // 8x8 base, 4x4 lower level
    reg(0x76,0x10,7);
#endif
#ifdef TEST_MIP_BC1
    // BC1: 8-byte blocks, 256-byte rows. 4x4 mip1 precedes 8x8 mip0.
    for(u32 i=0;i<sizeof(texel)/sizeof(texel[0]);i++) texel[i]=0;
    texel[0]=0x001f; // blue RGB565 endpoint; every two-bit selector is zero
    texel[64]=texel[66]=texel[128]=texel[130]=0xf800; // four red blocks
    reg(0x76,0xd,(u32)(texture>>40)|(169u<<20)|(3u<<30));
    reg(0x76,0xe,1u|(7u<<14)); // width=8, height=8
    reg(0x76,0x10,7); // pitch=8 before block-row alignment
#ifdef TEST_MIP_2D
    // 16x16 red base follows an 8x8 lower level with four distinct blocks.
    for(u32 i=0;i<sizeof(texel)/sizeof(texel[0]);i++) texel[i]=0;
    texel[0]=0xf800; texel[2]=texel[64]=0x07e0; texel[66]=0x001f;
    for(u32 y=0;y<4;y++) for(u32 x=0;x<4;x++) texel[128+y*64+x*2]=0xf800;
    reg(0x76,0xe,3u|(15u<<14)); // width=16, height=16
    reg(0x76,0x10,15);
#endif
#endif
#endif
#ifdef TEST_LINEAR_FILTER
    reg(0x76,0x14,2|(2<<3)|(2<<6)); // clamp to edge on every axis
    reg(0x76,0x16,(1u<<20)|(1u<<22)); // linear minification/magnification
#endif
#endif
#endif
    reg(0x69,0x1b3,0); reg(0x69,0x1b4,0); // constant PS has no interpolated inputs
#ifdef TEST_TEXTURE_UV
    reg(0x69,0x1b3,1); reg(0x69,0x1b4,1); // perspective sample barycentrics
    reg(0x69,0x191,0); // attr0 maps to param0, smooth interpolation
#endif
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
    if(submit_graphics()!=0) return 74;
    for(int i=0;i<5000 && !completed;i++) sleep_slot(1000);
    if(completed!=1) return 75;
    if(flip_slot(video,0,1,1)!=0) return 76;
    sleep_slot(FRAME_PAUSE_US);
    // Reuse the shader and target, changing only the scalar color inputs.
    n=0;
#ifdef TEST_TEXTURE
#ifdef TEST_TEXTURE_SELECT
    reg(0x76,0x18,0x3f400000); // sample blue at (0.75,0.75)
#ifdef TEST_MIP
    reg(0x76,0x18,0x3f800000); // sample mip1
#endif
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
#ifdef TEST_DMA_IMAGE
    // The first draw established a backend image at target. Fill must update
    // both CPU memory and that existing GPU image before the next flip.
    commands[n++]=0xc0055000; commands[n++]=2u<<29;
    commands[n++]=0xffff0000; commands[n++]=0;
#ifdef TEST_DMA_IMAGE_PREFIX
    u64 dma_target=target+DMA_FIRST_PIXEL*4;
    commands[n++]=(u32)dma_target; commands[n++]=(u32)(dma_target>>32);
    commands[n++]=(DMA_END_PIXEL-DMA_FIRST_PIXEL)*4;
#else
    commands[n++]=(u32)target; commands[n++]=(u32)(target>>32);
    commands[n++]=sizeof(pixels);
#endif
#else
    draw();
#endif
#ifdef TEST_DMA_IMAGE_DRAW
    reg(0x76,0xc,0x3f800000); reg(0x76,0xd,0); // red over blue DMA prefix
    reg(0x76,0xe,0x3f000000);
    reg(0x69,0x1e0,(1u<<30)|(1u<<29)|(5u<<24)|(1u<<16)|(5u<<8)|4u);
    draw();
#endif
    commands[n++]=0xc0064900; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=2; commands[n++]=0; commands[n++]=0;
    submission.count=(u32)n;
    if(submit_graphics()!=0) return 77;
    for(int i=0;i<5000 && completed!=2;i++) sleep_slot(1000);
    if(completed!=2) return 78;
#ifdef TEST_DMA_IMAGE
    for(u32 i=0;i<320*180;i++) {
#ifdef TEST_DMA_IMAGE_PREFIX
        if(((volatile u32*)pixels)[i]!=(i>=DMA_FIRST_PIXEL && i<DMA_END_PIXEL ? 0xffff0000 : 0xff202020)) return 92;
#else
        if(((volatile u32*)pixels)[i]!=0xffff0000) return 92;
#endif
    }
#endif
    if(flip_slot(video,0,1,2)!=0) return 79;
    sleep_slot(FRAME_PAUSE_US);
#if defined(TEST_DEPTH) || defined(TEST_TEXTURE) || defined(TEST_DMA_IMAGE)
#ifdef TEST_MIP_CACHE_PRESSURE
    // Distinct snapshots exceed the bounded host cache. Mutate padding in the
    // lower mip so the displayed blue texel remains unchanged. Wait for each
    // submission to consume its snapshot before writing the next version.
    for(u32 version=1;version<=40;version++) {
        texel[2]=version;
        n=0;
        draw();
        commands[n++]=0xc0064900; commands[n++]=0x14; commands[n++]=1u<<29;
        commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
        commands[n++]=100+version; commands[n++]=0; commands[n++]=0;
        submission.count=(u32)n;
        if(submit_graphics()!=0) return 83;
        for(int i=0;i<5000 && completed!=100+version;i++) sleep_slot(1000);
        if(completed!=100+version) return 84;
    }
#endif
    n=0;
#ifdef TEST_MIP
#ifdef TEST_MIP_BC1
#ifdef TEST_MIP_2D
    texel[66]=0xf800; // lower mip's bottom-right block changes to red
#else
    texel[0]=0xf800; // only the lower compressed block changes to red
#endif
#elif defined(TEST_MIP_BC3)
    texel[0]=64; texel[2]=0xf800; // lower mip becomes red, alpha 64
#elif defined(TEST_MIP_2D)
#ifdef TEST_MIP_ODD
    texel[1]=0xff0000ff; // second visible texel; storage padding stays green
#else
    texel[65]=0xff0000ff; // rewrite bottom-right texel of 2x2 lower mip
#endif
#else
    texel[0]=0xff0000ff; // rewrite only mip1; leave LOD=1 and descriptor unchanged
#ifdef TEST_MIP_SRGB
    texel[0]=0x80000040; // R=64, A=128: RGB must decode, alpha must not
#endif
#endif
#endif
#ifdef TEST_DEPTH
    reg(0x76,0xcc,0x3e000000); // nearer blue Z=0.125 must pass
#endif
    // Texture mode repeats unchanged blue after the CPU update.
#ifdef TEST_DMA_IMAGE
    for(u32 i=0;i<320*180;i++) dma_image_source[i]=0xff0000ff;
    u64 source=(u64)dma_image_source;
    commands[n++]=0xc0055000; commands[n++]=0;
    commands[n++]=(u32)source; commands[n++]=(u32)(source>>32);
#ifdef TEST_DMA_IMAGE_PREFIX
    commands[n++]=(u32)dma_target; commands[n++]=(u32)(dma_target>>32);
    commands[n++]=(DMA_END_PIXEL-DMA_FIRST_PIXEL)*4;
#else
    commands[n++]=(u32)target; commands[n++]=(u32)(target>>32);
    commands[n++]=sizeof(pixels);
#endif
#else
    draw();
#endif
#ifdef TEST_DMA_IMAGE_DRAW
    reg(0x76,0xc,0); reg(0x76,0xd,0x3f800000); // blue over red DMA prefix
    draw();
#endif
    commands[n++]=0xc0064900; commands[n++]=0x14; commands[n++]=1u<<29;
    commands[n++]=(u32)label; commands[n++]=(u32)(label>>32);
    commands[n++]=3; commands[n++]=0; commands[n++]=0;
    submission.count=(u32)n;
    if(submit_graphics()!=0) return 80;
    for(int i=0;i<5000 && completed!=3;i++) sleep_slot(1000);
    if(completed!=3) return 81;
#ifdef TEST_DMA_IMAGE
    for(u32 i=0;i<320*180;i++) {
#ifdef TEST_DMA_IMAGE_PREFIX
        if(((volatile u32*)pixels)[i]!=(i>=DMA_FIRST_PIXEL && i<DMA_END_PIXEL ? 0xff0000ff : 0xff202020)) return 93;
#else
        if(((volatile u32*)pixels)[i]!=0xff0000ff) return 93;
#endif
        dma_image_source[i]=0xff00ff00; // completed copy must be independent of source reuse
    }
#endif
    if(flip_slot(video,0,1,3)!=0) return 82;
    sleep_slot(FRAME_PAUSE_US);
#endif
#if defined(TEST_CROSS_QUEUE_WRITE_DATA) || defined(TEST_CROSS_QUEUE_DMA)
    for(u32 i=0;i<4;i++) if(producer_labels[i]!=(i==1 ? 1u : 0u)) return 91;
#endif
    close_slot(video);
    return 0;
}
