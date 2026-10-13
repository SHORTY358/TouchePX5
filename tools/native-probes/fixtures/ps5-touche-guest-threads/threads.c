/* Copyright (C) 2026 Touche PX5 Project
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Original local probe; public PS5 ABI names, unsigned native SCE ELF. */
extern int scePthreadCreate(void **thread, const void *attributes,
                           void *(*entry)(void *), void *argument, const char *name);
extern int scePthreadJoin(void *thread, void **result);
extern void scePthreadExit(void *result);
extern int sceKernelUsleep(unsigned int microseconds);
extern unsigned long long sceKernelGetProcessTime(void);
extern int sceKernelSendNotificationRequest(int device, void *request, int size, int flags);

static unsigned int start_gate, finished, counter;
static unsigned int totals[2];
static void notify(const char *message) {
    char request[3120];
    for (int i=0; i<3120; i++) request[i]=0;
    for (int i=0; message[i] && i<3074; i++) request[45+i]=message[i];
    sceKernelSendNotificationRequest(0,request,3120,0);
}
static void *worker(void *argument) {
    unsigned long index=(unsigned long)argument-1;
    unsigned long long begin=sceKernelGetProcessTime();
    while (!__atomic_load_n(&start_gate,__ATOMIC_ACQUIRE)) {
        if (sceKernelGetProcessTime()-begin>5000000ULL) return (void*)0xBAD;
        sceKernelUsleep(100);
    }
    for (unsigned int i=0; i<1000; i++) {
        __atomic_fetch_add(&counter,1,__ATOMIC_RELAXED);
        totals[index]++;
        if ((i&63)==0) sceKernelUsleep(100);
    }
    __atomic_fetch_add(&finished,1,__ATOMIC_RELEASE);
    void *result=(void*)(0x12345678CAFE0000ULL+index+1);
#ifdef EXIT_SECOND_WORKER
    if (index==1) { scePthreadExit(result); return (void*)0xBAD; }
#endif
    return result;
}
int main(void) {
    void *threads[2]={0,0}, *results[2]={0,0};
    if (scePthreadCreate(&threads[0],0,worker,(void*)1,"probe-worker-1")!=0) {
        notify("guest_threads: create 1 failed"); return 1;
    }
    if (scePthreadCreate(&threads[1],0,worker,(void*)2,"probe-worker-2")!=0) {
        __atomic_store_n(&start_gate,1,__ATOMIC_RELEASE);
        notify("guest_threads: create 2 failed"); return 2;
    }
    if (!threads[0] || !threads[1] || threads[0]==threads[1]) {
        __atomic_store_n(&start_gate,1,__ATOMIC_RELEASE);
        notify("guest_threads: invalid handles"); return 3;
    }
    __atomic_store_n(&start_gate,1,__ATOMIC_RELEASE);
    unsigned long long begin=sceKernelGetProcessTime();
    while (__atomic_load_n(&finished,__ATOMIC_ACQUIRE)!=2) {
        if (sceKernelGetProcessTime()-begin>5000000ULL) {
            notify("guest_threads: workers timed out"); return 4;
        }
        sceKernelUsleep(100);
    }
    if (scePthreadJoin(threads[0],&results[0])!=0 || scePthreadJoin(threads[1],&results[1])!=0) {
        notify("guest_threads: join failed"); return 5;
    }
    if (counter!=2000 || totals[0]!=1000 || totals[1]!=1000) {
        notify("guest_threads: shared data failed"); return 6;
    }
    if ((unsigned long long)results[0]!=0x12345678CAFE0001ULL || (unsigned long long)results[1]!=0x12345678CAFE0002ULL) {
        char message[]="guest_threads: returns=0000000000000000,0000000000000000";
        const char digits[]="0123456789ABCDEF";
        for (int r=0;r<2;r++) for (int i=0;i<16;i++)
            message[23+r*17+i]=digits[((unsigned long long)results[r]>>(60-i*4))&15];
        notify(message); return 7;
    }
    notify("guest_threads: PASS workers=2 counter=2000 totals=1000,1000 returns=12345678CAFE0001,12345678CAFE0002");
    return 0;
}
