// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

// SDK-free lifecycle oracle. The same framebuffer is reused ONLY after its real
// flip event. The runner defers presentation; submission alone must not signal.
typedef unsigned int flip_u32;
typedef unsigned long long flip_u64;
extern int (*flip_slot)(int,int,int,flip_u64);
extern int (*status_slot)(int,void*);
extern int (*pending_slot)(int);
extern int (*create_eq_slot)(flip_u64*,const char*);
extern int (*wait_eq_slot)(flip_u64,void*,int,int*,flip_u32*);
extern int (*delete_eq_slot)(flip_u64);
extern int (*add_flip_event_slot)(flip_u64,int,void*);
static int checked_flip(int,int,int,flip_u64);
int (*lifecycle_flip_slot)(int,int,int,flip_u64);

#define flip_slot lifecycle_flip_slot
#define _start triangle_entry
#define FRAME_PAUSE_US 10000
#include "triangle.c"
#undef _start
#undef flip_slot

static u64 event_queue;
static u64 expected_count;
static u64 status_words[16];
static u64 event_words[4];

static int checked_flip(int video,int index,int mode,u64 arg) {
    if(expected_count==0 && add_flip_event_slot(event_queue,video,(void*)0x1234)!=0) return 101;
#ifdef ORDERED_FLIP
    if(expected_count>0) {
        u32 packet[6]={0xc004105c,(u32)video,(u32)index,(u32)mode,(u32)arg,(u32)(arg>>32)};
        struct { u64 address; u32 count; u32 reserved; } flip_submission={(u64)packet,6,0};
        if(submit_slot(&flip_submission)!=0) return 102;
        if(expected_count==1) {
            // Overwrite the live GPU image behind the already queued capture.
            // The delayed/retried first GPU flip must remain red, never green.
            u64 target=(u64)pixels;
            u32 overwrite[7]={0xc0055000,2u<<29,0xff00ff00,0,
                (u32)target,(u32)(target>>32),sizeof(pixels)};
            flip_submission.address=(u64)overwrite; flip_submission.count=7;
            if(submit_slot(&flip_submission)!=0) return 115;
        }
    } else
#endif
    if(flip_slot(video,index,mode,arg)!=0) return 103;

    // The renderer intentionally delays every tracked frame by 300ms.
    // An implementation that acknowledges submission fails this check.
#ifdef WINDOW_RECOVERY
    // The operator minimizes during the pause after frame 1. Frame 2 must
    // stay pending for four seconds while the real native window is unavailable.
    if(expected_count==1) {
        for(int pause=0;pause<400;pause++) sleep_slot(10000);
    } else sleep_slot(50000);
#else
    sleep_slot(50000);
#endif
    if(status_slot(video,status_words)!=0) return 104;
    if(status_words[0]!=expected_count || ((u32*)status_words)[0x34/4]!=1) return 105;
    if(pending_slot(video)!=1) return 106;
    int count=0; u32 timeout=0;
    wait_eq_slot(event_queue,event_words,1,&count,&timeout);
    if(count!=0) return 107;

#ifdef WINDOW_RECOVERY
    const int max_retries=6000;
#else
    const int max_retries=1500;
#endif
    for(int retry=0;retry<max_retries;retry++) {
        count=0; timeout=0;
        wait_eq_slot(event_queue,event_words,1,&count,&timeout);
        if(count) break;
        sleep_slot(10000);
    }
    if(count!=1 || event_words[3]!=0x1234 || (event_words[2]>>16)!=arg) return 108;
    expected_count++;
    if(status_slot(video,status_words)!=0 || status_words[0]!=expected_count ||
       status_words[0x18/8]!=arg || ((u32*)status_words)[0x34/4]!=0 ||
       (int)((u32*)status_words)[0x38/4]!=index || pending_slot(video)!=0) return 109;
    if(status_words[0x28/8]<status_words[0x40/8] || !status_words[0x40/8]) return 110;
    count=0; timeout=0;
    wait_eq_slot(event_queue,event_words,1,&count,&timeout);
    if(count) return 111;
#ifdef WINDOW_RECOVERY
    if(expected_count==1) {
        for(int pause=0;pause<3000;pause++) sleep_slot(10000);
    }
#endif
    return 0;
}

int _start(void) {
    // The minimal fixture linker has no dynamic data relocations. Materialize
    // the callback from RIP-relative code after the loader chooses its base.
    lifecycle_flip_slot=checked_flip;
    if(create_eq_slot(&event_queue,"flip-lifecycle")!=0) return 100;
    int result;
#ifdef CPU_FLIP
    int video=open_slot(0,0,0,(void*)0);
    if(video<=0) return 112;
    if(attribute_slot(attribute,0x80000000,1,0,320,180,320)!=0) return 113;
    addresses[0]=(u64)pixels;
    if(register_slot(video,0,addresses,1,attribute)<0) return 114;
    const u32 colors[6]={0xffff0000,0xff00ff00,0xff0000ff,0xffffff00,0xff00ffff,0xffff00ff};
    result=0;
#ifdef BURST_FLIP
    if(add_flip_event_slot(event_queue,video,(void*)0x1234)!=0) return 101;
    // Six accepted CPU snapshots exceed the old four-frame pruning limit.
    for(u32 frame=0;frame<6;frame++) {
        for(u32 i=0;i<320*180;i++) pixels[i]=colors[frame];
        if(flip_slot(video,0,1,frame)!=0) return 116;
    }
    for(int retry=0;retry<1500;retry++) {
        if(status_slot(video,status_words)!=0) return 117;
        if(status_words[0]+((u32*)status_words)[0x34/4]!=6) return 118;
        if(status_words[0]==6) break;
        sleep_slot(10000);
    }
    if(status_words[0]!=6 || status_words[0x18/8]!=5 || pending_slot(video)!=0) return 119;
#else
    for(u32 frame=0;frame<3;frame++) {
        for(u32 i=0;i<320*180;i++) pixels[i]=colors[frame];
        result=checked_flip(video,0,1,frame);
        if(result) break;
    }
#endif
    close_slot(video);
#else
    result=triangle_entry();
#endif
    // Presentation acceptance precedes the optional host readback/file write.
    // Keep the process alive for diagnostics after the final lifecycle assertion.
    sleep_slot(2000000);
    delete_eq_slot(event_queue);
    return result;
}
