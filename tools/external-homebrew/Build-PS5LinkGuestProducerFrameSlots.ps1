# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$ReverseObjects,[switch]$FreezeProducerPalette,[switch]$BatchProducerRequests,[switch]$SharedTextureHalves,[switch]$SkipSecondHalfWrites,[switch]$SharedTextureColumns,[switch]$IndependentProducerPalettes)
$ErrorActionPreference='Stop'
$sharedTexture=$SharedTextureHalves -or $SharedTextureColumns
if($SharedTextureHalves -and $SharedTextureColumns) { throw 'Choose rows or columns for shared ownership' }
if($sharedTexture -and -not $BatchProducerRequests) { throw 'Shared texture requires paired requests and retirement of both GPU consumers' }
if($SkipSecondHalfWrites -and (-not $sharedTexture -or $FreezeProducerPalette)) { throw 'Missing-half control requires shared texture without a frozen palette' }
if($IndependentProducerPalettes -and (-not $SharedTextureColumns -or $FreezeProducerPalette)) { throw 'Independent palettes require shared columns without globally frozen producers' }
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$out=Join-Path $root 'artifacts/external-homebrew/ps5link-cube'
$parent='texture-frame-slots'
if($ReverseObjects) { $parent+='-reverse' }
$report=Get-Content "$out/build-report-$parent.json" -Raw | ConvertFrom-Json
$parentSource=Join-Path $out "main-$parent.c"
if((Get-FileHash $parentSource).Hash -ne $report.GeneratedSourceSha256) { throw 'Parent source provenance mismatch; build frame slots first' }
$text=[IO.File]::ReadAllText($parentSource)
function Replace-Checked([string]$needle,[string]$replacement) {
    if(-not $script:text.Contains($needle)) { throw "Parent source contract changed: $needle" }
    $script:text=$script:text.Replace($needle,$replacement)
}
Replace-Checked 'int main(void) {' @'
extern int scePthreadCreate(void **, const void *, void *(*)(void *), void *, const char *);
extern int scePthreadJoin(void *, void **);
extern int sceKernelCreateSema(unsigned int *, const char *, unsigned int, int, int, void *);
extern int sceKernelWaitSema(unsigned int, int, unsigned int *);
extern int sceKernelSignalSema(unsigned int, int);
extern int sceKernelDeleteSema(unsigned int);
typedef struct {
    const unsigned char *source;
    unsigned char *destination;
    unsigned int request, complete, phase, jobs, slot;
} TextureProducer;
static TextureProducer producers[2];
static void *produce_texture(void *argument) {
    TextureProducer *job=argument;
    for (unsigned int frame=0; frame<300; frame++) {
        unsigned int timeout=5000000;
        if (sceKernelWaitSema(job->request,1,&timeout)!=0) return (void*)0xBAD1;
        unsigned int phase=__atomic_load_n(&job->phase,__ATOMIC_ACQUIRE);
        for (unsigned int i=0; i<64*64*4; i+=4) {
            for (unsigned int c=0; c<3; c++) job->destination[i+c]=job->source[i+(c+phase)%3];
            job->destination[i+3]=255;
        }
        __atomic_fetch_add(&job->jobs,1,__ATOMIC_RELEASE);
        if (sceKernelSignalSema(job->complete,1)!=0) return (void*)0xBAD2;
    }
    return (void*)(0x12345678CAFE0001ULL+job->slot);
}
int main(void) {
'@
Replace-Checked 'const int FRAMES = 600;' @'
void *producer_threads[2]={0,0};
    for (unsigned int slot=0; slot<2; slot++) {
        TextureProducer *job=&producers[slot];
        job->source=pixels; job->destination=mutable_texture[slot].ptr; job->slot=slot;
        if (sceKernelCreateSema(&job->request,slot ? "texture-request-1" : "texture-request-0",1,0,1,0)!=0 ||
            sceKernelCreateSema(&job->complete,slot ? "texture-complete-1" : "texture-complete-0",1,0,1,0)!=0 ||
            scePthreadCreate(&producer_threads[slot],0,produce_texture,job,slot ? "texture-producer-1" : "texture-producer-0")!=0) {
            notify("mesh_test: producer setup failed"); return 10;
        }
    }
    const int FRAMES = 600;
'@
Replace-Checked @'
        for (unsigned int i = 0; i < 64 * 64 * 4; i += 4) {
            for (unsigned int c = 0; c < 3; c++) second_pixels[i+c] = pixels[i+(c+phase)%3];
            second_pixels[i+3] = 255;
        }
'@ @'
        TextureProducer *job=&producers[slot];
        __atomic_store_n(&job->phase,phase,__ATOMIC_RELEASE);
        unsigned int producer_timeout=5000000;
        if (sceKernelSignalSema(job->request,1)!=0 ||
            sceKernelWaitSema(job->complete,1,&producer_timeout)!=0 ||
            __atomic_load_n(&job->jobs,__ATOMIC_ACQUIRE)!=(unsigned int)(frame/2+1)) {
            notify("mesh_test: producer handoff failed"); return 11;
        }
'@
Replace-Checked 'notify("mesh_test: fin");' @'
for (unsigned int slot=0; slot<2; slot++) {
        void *result=0;
        if (scePthreadJoin(producer_threads[slot],&result)!=0 ||
            (unsigned long long)result!=0x12345678CAFE0001ULL+slot ||
            __atomic_load_n(&producers[slot].jobs,__ATOMIC_ACQUIRE)!=300 ||
            sceKernelDeleteSema(producers[slot].request)!=0 ||
            sceKernelDeleteSema(producers[slot].complete)!=0) {
            notify("mesh_test: producer join or cleanup failed"); return 12;
        }
    }
    notify("guest_texture_producers: PASS workers=2 jobs=300,300 returns=12345678CAFE0001,12345678CAFE0002");
    notify("mesh_test: fin");
'@
$suffix='texture-guest-producers'
if($BatchProducerRequests) {
    Replace-Checked 'static TextureProducer producers[2];' @'
static TextureProducer producers[2];
static unsigned int batch_ready[2], batch_start, half_ready[2], half_checks[2];
static int wait_generation(unsigned int *value,unsigned int generation) {
    unsigned long long begin=sceKernelGetProcessTime();
    while (__atomic_load_n(value,__ATOMIC_ACQUIRE)<generation) {
        if (sceKernelGetProcessTime()-begin>5000000ULL) return 0;
        sceKernelUsleep(100);
    }
    return 1;
}
'@
    Replace-Checked 'unsigned int phase=__atomic_load_n(&job->phase,__ATOMIC_ACQUIRE);' @'
unsigned int phase=__atomic_load_n(&job->phase,__ATOMIC_ACQUIRE);
        __atomic_store_n(&batch_ready[job->slot],frame+1,__ATOMIC_RELEASE);
        if (!wait_generation(&batch_start,frame+1)) return (void*)0xBAD3;
'@
    Replace-Checked 'for (unsigned int i=0; i<64*64*4; i+=4) {' @'
for (unsigned int i=0; i<64*64*4; i+=4) {
            if (i==64*64*2) {
                __atomic_store_n(&half_ready[job->slot],frame+1,__ATOMIC_RELEASE);
                if (!wait_generation(&half_ready[job->slot^1],frame+1)) return (void*)0xBAD4;
                __atomic_fetch_add(&half_checks[job->slot],1,__ATOMIC_RELEASE);
            }
'@
    Replace-Checked 'for (int frame = 0; frame < FRAMES; frame++) {' @'
for (int frame = 0; frame < FRAMES; frame++) {
        if ((frame&1)==0) {
            unsigned int generation=(unsigned int)(frame/2+1);
            unsigned int batch_phase=(1u+(unsigned int)(frame/150))%3;
            /* Neither producer may overwrite a texture still sampled by GPU. */
            for (unsigned int owner=0; owner<2; owner++) {
                if (occupied[owner]) {
                    if (!retire_frame_slot(fences[owner])) {
                        notify("mesh_test: batch GPU retirement timed out"); return 13;
                    }
                    occupied[owner]=0; retired++; reuses++;
                }
                __atomic_store_n(&producers[owner].phase,batch_phase,__ATOMIC_RELEASE);
                if (sceKernelSignalSema(producers[owner].request,1)!=0) {
                    notify("mesh_test: batch request failed"); return 14;
                }
            }
            /* Both jobs must have started before either is allowed to fill. */
            if (!wait_generation(&batch_ready[0],generation) ||
                !wait_generation(&batch_ready[1],generation)) {
                notify("mesh_test: batch ready timed out"); return 15;
            }
            __atomic_store_n(&batch_start,generation,__ATOMIC_RELEASE);
            for (unsigned int owner=0; owner<2; owner++) {
                unsigned int timeout=5000000;
                if (sceKernelWaitSema(producers[owner].complete,1,&timeout)!=0 ||
                    __atomic_load_n(&producers[owner].jobs,__ATOMIC_ACQUIRE)!=generation) {
                    notify("mesh_test: batch completion failed"); return 16;
                }
            }
        }
'@
    Replace-Checked @'
        TextureProducer *job=&producers[slot];
        __atomic_store_n(&job->phase,phase,__ATOMIC_RELEASE);
        unsigned int producer_timeout=5000000;
        if (sceKernelSignalSema(job->request,1)!=0 ||
            sceKernelWaitSema(job->complete,1,&producer_timeout)!=0 ||
            __atomic_load_n(&job->jobs,__ATOMIC_ACQUIRE)!=(unsigned int)(frame/2+1)) {
            notify("mesh_test: producer handoff failed"); return 11;
        }
'@ '        /* Both slot textures were completed at the start of this frame pair. */'
    Replace-Checked 'notify("guest_texture_producers: PASS workers=2 jobs=300,300 returns=12345678CAFE0001,12345678CAFE0002");' @'
if (batch_ready[0]!=300 || batch_ready[1]!=300 || batch_start!=300 ||
        half_ready[0]!=300 || half_ready[1]!=300 || half_checks[0]!=300 || half_checks[1]!=300) {
        notify("mesh_test: batch producer accounting failed"); return 17;
    }
    notify("guest_texture_batch: PASS batches=300 outstanding=2 half_checks=300,300");
    notify("guest_texture_producers: PASS workers=2 jobs=300,300 returns=12345678CAFE0001,12345678CAFE0002");
'@
    $suffix+='-batch'
}
if($sharedTexture) {
    Replace-Checked 'ALLOC_OR_FAIL(mutable_texture[1], 64 * 64 * 4, DIRECT_MEM_MIN_ALIGN, "texture-slot1");' 'mutable_texture[1]=mutable_texture[0]; /* both GPU slots sample one allocation */'
    Replace-Checked 'job->source=pixels; job->destination=mutable_texture[slot].ptr; job->slot=slot;' 'job->source=pixels+slot*64*64*2; job->destination=(unsigned char*)mutable_texture[slot].ptr+slot*64*64*2; job->slot=slot;'
    Replace-Checked 'for (unsigned int i=0; i<64*64*4; i+=4) {' 'for (unsigned int i=0; i<64*64*2; i+=4) {'
    Replace-Checked 'if (i==64*64*2)' 'if (i==64*64)'
    if($SharedTextureColumns) {
        Replace-Checked 'job->source=pixels+slot*64*64*2; job->destination=(unsigned char*)mutable_texture[slot].ptr+slot*64*64*2; job->slot=slot;' 'job->source=pixels+slot*32*4; job->destination=(unsigned char*)mutable_texture[slot].ptr+slot*32*4; job->slot=slot;'
        Replace-Checked @'
            for (unsigned int c=0; c<3; c++) job->destination[i+c]=job->source[i+(c+phase)%3];
            job->destination[i+3]=255;
'@ @'
            unsigned int offset=(i/(32*4))*64*4+i%(32*4);
            for (unsigned int c=0; c<3; c++) job->destination[offset+c]=job->source[offset+(c+phase)%3];
            job->destination[offset+3]=255;
'@
    }
    if($SkipSecondHalfWrites) {
        $fill=if($SharedTextureColumns) { 'for (unsigned int c=0; c<3; c++) job->destination[offset+c]=job->source[offset+(c+phase)%3];' } else { 'for (unsigned int c=0; c<3; c++) job->destination[i+c]=job->source[i+(c+phase)%3];' }
        Replace-Checked $fill ('if (job->slot==1) continue; /* negative control: owned half stays stale */'+[Environment]::NewLine+'            '+$fill)
    }
    $suffix+=if($SharedTextureColumns) { '-shared-columns' } else { '-shared-halves' }
}
if($IndependentProducerPalettes) {
    Replace-Checked '__atomic_store_n(&producers[owner].phase,batch_phase,__ATOMIC_RELEASE);' '__atomic_store_n(&producers[owner].phase,(1u+(unsigned int)(frame/(owner ? 100 : 150)))%3,__ATOMIC_RELEASE);'
    $suffix+='-independent'
}
if($ReverseObjects) { $suffix+='-reverse' }
if($FreezeProducerPalette) {
    Replace-Checked 'unsigned int phase=__atomic_load_n(&job->phase,__ATOMIC_ACQUIRE);' 'unsigned int phase=1; /* negative control: ignore requested palette changes */'
    $suffix+='-frozen-static'
}
if($SkipSecondHalfWrites) { $suffix+='-missing-half' }
& "$PSScriptRoot/Build-PS5LinkDeterministicLinker.ps1"
$linker=Join-Path $root 'artifacts/external-homebrew/ps5link-deterministic-linker/link_real.exe'
[IO.File]::WriteAllText("$out/main-$suffix.c",$text)
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -mno-red-zone -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-$suffix.c" -o "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Guest producer compilation failed' }
& $linker "$out/app-$suffix.elf" "$out/crt1.o" "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Guest producer ELF link failed' }
$report | Add-Member GuestTextureProducers 2
$report | Add-Member JobsPerProducer 300
$report | Add-Member ProducerHandoff 'per-slot request/complete semaphores; main waits CPU completion before submit; GPU retirement before slot reuse'
$report | Add-Member ProducerNoRedZone $true
$report | Add-Member FrozenProducerPalette ([bool]$FreezeProducerPalette)
$report | Add-Member BatchProducerRequests ([bool]$BatchProducerRequests)
$report | Add-Member OutstandingProducerJobs $(if($BatchProducerRequests) { 2 } else { 1 })
$report | Add-Member SharedTextureHalves ([bool]$SharedTextureHalves)
$report | Add-Member SharedTextureColumns ([bool]$SharedTextureColumns)
$report | Add-Member IndependentProducerPalettes ([bool]$IndependentProducerPalettes)
$report | Add-Member SkipSecondHalfWrites ([bool]$SkipSecondHalfWrites)
if($BatchProducerRequests) { $report.ProducerHandoff='two requests before wait; both-started and half-fill barriers; main waits both CPU completions; both GPU slots retired before refill' }
if($sharedTexture) {
    $report.MutableTexturePerSlot=$false
    $report.FrameOwnership='one shared texture; disjoint top/bottom 8192-byte producer ranges; both GPU consumers retired before refill'
    if($SharedTextureColumns) { $report.FrameOwnership='one shared texture; disjoint left/right 128-byte row spans sharing four OS pages; both GPU consumers retired before refill' }
}
$report | Add-Member ParentSourceSha256 ((Get-FileHash $parentSource).Hash)
$report | Add-Member StartupObjectSha256 ((Get-FileHash "$out/crt1.o").Hash)
$report | Add-Member LinkerSha256 ((Get-FileHash $linker).Hash)
$report | Add-Member DeterministicAlignmentPadding $true
$report.GeneratedSourceSha256=(Get-FileHash "$out/main-$suffix.c").Hash
$report.ElfSha256=(Get-FileHash "$out/app-$suffix.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-$suffix.json" -Encoding utf8
