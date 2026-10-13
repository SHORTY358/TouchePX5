# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([switch]$ReverseObjects,[switch]$FreezeProducerPalette)
$ErrorActionPreference='Stop'
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
if($ReverseObjects) { $suffix+='-reverse' }
if($FreezeProducerPalette) {
    Replace-Checked 'unsigned int phase=__atomic_load_n(&job->phase,__ATOMIC_ACQUIRE);' 'unsigned int phase=1; /* negative control: ignore requested palette changes */'
    $suffix+='-frozen-static'
}
[IO.File]::WriteAllText("$out/main-$suffix.c",$text)
& 'C:/BuildTools/VC/Tools/Llvm/x64/bin/clang.exe' --target=x86_64-sie-ps5 -O2 -fPIC -fno-stack-protector -mno-red-zone -I $out -I "$root/artifacts/external-homebrew/ps5link-texture-shaders" -c "$out/main-$suffix.c" -o "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Guest producer compilation failed' }
& "$out/link_real.exe" "$out/app-$suffix.elf" "$out/crt1.o" "$out/main-$suffix.o"
if($LASTEXITCODE) { throw 'Guest producer ELF link failed' }
$report | Add-Member GuestTextureProducers 2
$report | Add-Member JobsPerProducer 300
$report | Add-Member ProducerHandoff 'per-slot request/complete semaphores; main waits CPU completion before submit; GPU retirement before slot reuse'
$report | Add-Member ProducerNoRedZone $true
$report | Add-Member FrozenProducerPalette ([bool]$FreezeProducerPalette)
$report | Add-Member ParentSourceSha256 ((Get-FileHash $parentSource).Hash)
$report | Add-Member StartupObjectSha256 ((Get-FileHash "$out/crt1.o").Hash)
$report | Add-Member LinkerSha256 ((Get-FileHash "$out/link_real.exe").Hash)
$report.GeneratedSourceSha256=(Get-FileHash "$out/main-$suffix.c").Hash
$report.ElfSha256=(Get-FileHash "$out/app-$suffix.elf").Hash
$report | ConvertTo-Json | Set-Content "$out/build-report-$suffix.json" -Encoding utf8
