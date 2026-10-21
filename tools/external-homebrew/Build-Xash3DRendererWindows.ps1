# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
# Generated native overlays retain the upstream GPL-3.0-or-later license.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputRoot,[switch]$ShaderAperture)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=Join-Path $repo 'artifacts/external-homebrew/ps5-xash3d-halflife'
$pin='be0731b4fcc4233349bba5e0d3ff33c79428f695'
if((& git -C $source rev-parse HEAD) -ne $pin -or (& git -C $source status --porcelain)) { throw 'Clean pinned source required' }
$inputs=(Resolve-Path -LiteralPath $InputRoot).Path
$inputReport=Get-Content "$inputs/report.json" -Raw | ConvertFrom-Json
if($inputReport.SourcePin -ne $pin -or $inputReport.GameAssetsUsed) { throw 'Expected pinned synthetic native inputs' }
foreach($entry in $inputReport.Files) { if((Get-FileHash -LiteralPath (Join-Path $inputs $entry.File)).Hash -ne $entry.Sha256) { throw "Native input changed: $($entry.File)" } }
$deps=Join-Path $repo 'artifacts/external-homebrew/gears-toolchain'
$foundation=Join-Path $deps 'native-foundation'
if((& git -C $foundation rev-parse HEAD) -ne '37dd53602bdead63936f718004555ba10154be48') { throw 'Unexpected native foundation pin' }
& "$PSScriptRoot/Test-GearsWindowsCrossCompile.ps1" -VerifySdkOnly
$bin=Join-Path $deps 'clang18/llvm-mingw-20240619-ucrt-x86_64/bin'
$sdk=Join-Path $deps 'sdk-v0.42/ps5-payload-sdk'
$converter=Join-Path $deps 'foundation-demo-windows/ps5-native-tool.exe'
$converterReport=Get-Content "$deps/foundation-demo-windows/report.json" -Raw | ConvertFrom-Json
if((Get-FileHash $converter).Hash -ne $converterReport.ConverterSha256 -or -not $converterReport.OriginalConverterFailureVerified) { throw 'Verified native converter required' }
$out=Join-Path $repo ('artifacts/xash3d-native/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path "$out/native","$out/src","$out/include","$out/obj" -Force | Out-Null
# Explicit emulator-consumer ABI overlay, not the upstream FW12.02 ABI.
# Same proven public init revision and legacy defaults layout as native Gears.
Copy-Item "$source/include/ps5_agc.h" "$out/include/ps5_agc.h"
$header=[IO.File]::ReadAllText("$source/include/ps5_agc_registers.h")
$field='    uint32_t encoded_index;'
if(([regex]::Matches($header,[regex]::Escape($field))).Count -ne 1) { throw 'Review changed type/index layout' }
[IO.File]::WriteAllText("$out/include/ps5_agc_registers.h",$header.Replace($field,$field+"`n    uint32_t reserved;"))
foreach($suffix in @('h','c')) {
    $text=[IO.File]::ReadAllText("$source/src/ps5_color_target.$suffix")
    if($suffix -eq 'h') {
        if(-not $text.Contains('PS5_FW_1202_DEFAULT_COUNT = 137') -or -not $text.Contains('PS5_FW_1202_CX_DEFAULT_COUNT = 84')) { throw 'Review changed default counts' }
        $text=$text.Replace('PS5_FW_1202_DEFAULT_COUNT = 137','PS5_HLE_LEGACY_DEFAULT_COUNT = 127').Replace('PS5_FW_1202_CX_DEFAULT_COUNT = 84','PS5_HLE_LEGACY_CX_DEFAULT_COUNT = 78')
    } else { $text=$text.Replace('PS5_FW_1202_DEFAULT_COUNT','PS5_HLE_LEGACY_DEFAULT_COUNT').Replace('PS5_FW_1202_CX_DEFAULT_COUNT','PS5_HLE_LEGACY_CX_DEFAULT_COUNT') }
    [IO.File]::WriteAllText("$out/src/ps5_color_target.$suffix",$text)
}
$main=[IO.File]::ReadAllText("$source/native/main.c")
$init='sceAgcInit(&resources.agc_state, sizeof(resources.agc_state))'
if(([regex]::Matches($main,[regex]::Escape($init))).Count -ne 1) { throw 'Review changed native initializer' }
$main="#include <stdio.h>`nextern int touche_public_agc_init(unsigned int revision) __asm__(`"sceAgcInit`");`n"+$main.Replace($init,'touche_public_agc_init(8u)')
$failure=[regex]::Match($main,'static int fail_pre_submit\([^\r\n]+\)\r?\n\{')
if(-not $failure.Success) { throw 'Review changed native failure diagnostic location' }
$diagnostic=@'

    char message[192];
    int length = snprintf(message, sizeof(message), "XASH_RENDERER_BOOT_FAILURE step=%s result=%d\n", step, result);
    if (length > 0 && (size_t)length < sizeof(message)) (void)write(2, message, (size_t)length);
'@
$main=$main.Replace($failure.Value,$failure.Value+$diagnostic)
$park=[regex]::Match($main,'static void park\(const char \*reason\)\r?\n\{')
if(-not $park.Success) { throw 'Review changed park handler' }
$main=$main.Replace($park.Value,$park.Value+@'

    char diagnostic[192];
    int length = snprintf(diagnostic, sizeof(diagnostic), "XASH_RENDERER_PARK reason=%s\n", reason ? reason : "unknown");
    if (length > 0 && (size_t)length < sizeof(diagnostic)) (void)write(2, diagnostic, (size_t)length);
'@)
$step='        result = gears_frame_loop_step(&loop);'
if(([regex]::Matches($main,[regex]::Escape($step))).Count -ne 3) { throw 'Review changed renderer loop diagnostics' }
$main=$main.Replace($step,$step+@'

        if (result != 0) {
            GearsFrameRunnerResult diagnostic_run = {0};
            (void)gears_frame_loop_result(&loop, &diagnostic_run);
            char diagnostic[192];
            int length = snprintf(diagnostic, sizeof(diagnostic), "XASH_RENDERER_LOOP_FAILURE result=%d state=%d callback=%d frame=%llu completed=%llu\n", result, (int)diagnostic_run.state, diagnostic_run.callback_result, (unsigned long long)diagnostic_run.failed_frame, (unsigned long long)diagnostic_run.frames_completed);
            if (length > 0 && (size_t)length < sizeof(diagnostic)) (void)write(2, diagnostic, (size_t)length);
        }
'@)
foreach($declaration in @('    const uint64_t feature_frame_index = frame->frame_index;','    const uint64_t feature_frame_index = frame ? frame->frame_index : 0u;')) {
    $match=[regex]::Match($main,[regex]::Escape($declaration)+'\r?\n#endif')
    if(-not $match.Success) { throw 'Review optional frame-feature variable declaration' }
    # Minimal BSP profile does not use the optional feature phase number.
    # Acknowledge that local variable; retain -Werror and every runtime check.
    $main=$main.Replace($match.Value,$match.Value+"`n    (void)feature_frame_index;")
}
if($ShaderAperture) {
    # Original shader table pointers carry only low 32 bits and assume high=2.
    # Request two non-overlapping fixed mappings through the public kernel API;
    # retain the original source's address checks and allocation ownership.
    $allocator=[regex]::Match($main,'static int allocate_direct\(')
    if(-not $allocator.Success) { throw 'Review changed direct allocator' }
    $mapping=@'
static int touche_map_bsp_aperture(void **address, size_t bytes,
    int protection, int flags, int64_t offset, size_t alignment)
{
    if (!address || *address || flags != 0) return -1;
    *address = (void *)(uintptr_t)UINT64_C(0x200000000);
    return sceKernelMapDirectMemory(address, bytes, protection, 0x10,
                                     offset, alignment);
}
static int touche_map_shader_aperture(void **address, size_t bytes,
    int protection, int flags, int64_t offset, size_t alignment)
{
    if (!address || *address || flags != 0) return -1;
    *address = (void *)(uintptr_t)UINT64_C(0x210000000);
    return sceKernelMapDirectMemory(address, bytes, protection, 0x10,
                                     offset, alignment);
}

'@
    $main=$main.Insert($allocator.Index,$mapping)
    $map='NULL, sceKernelAllocateMainDirectMemory, sceKernelMapDirectMemory,'
    if(([regex]::Matches($main,[regex]::Escape($map))).Count -ne 1) { throw 'Review direct allocation ops' }
    $main=$main.Replace($map,@'
NULL, sceKernelAllocateMainDirectMemory,
        address == &resources.bsp ? touche_map_bsp_aperture :
        address == &resources.shader ? touche_map_shader_aperture :
        sceKernelMapDirectMemory,
'@)
}
[IO.File]::WriteAllText("$out/native/main.c",$main)
$flags=@('-target','x86_64-sie-ps5','-fvisibility-nodllstorageclass=default','-isysroot',$sdk,'-isystem',"$sdk/target/include/c++/v1",'-isystem',"$sdk/target/include",'-fno-stack-protector','-fno-plt','-femulated-tls','-O2','-Wall','-Wextra','-Werror','-ffunction-sections','-fdata-sections',"-I$out/include","-I$out/src","-I$out/native","-I$source/include","-I$source/src","-I$source/native","-I$source/native/ps5log","-I$inputs/generated",'-DPS5_BSP_VIEWER=1','-DPS5_BSP_NOCLIP=1','-DPS5_BSP_TEXTURED=1')
# SDK v0.42 math.h lists qualified _Generic associations that C11 lvalue
# conversion can never select. Clang 18 diagnoses the macro at each use site.
# Suppress only that SDK compatibility diagnostic; preserve all runtime checks.
$flags+='-Wno-unreachable-code-generic-assoc'
$build=[IO.File]::ReadAllText("$source/tools/build_native.sh")
$sources=@([regex]::Match($build,'(?s)sources=\((.*?)\)').Groups[1].Value.Trim() -split '\s+' | Where-Object { $_ -ne '\' })
if($sources.Count -ne 60 -or @($sources | Where-Object { $_ -notmatch '^(src|native)/[a-zA-Z0-9_./]+\.c$' }).Count) { throw 'Review changed native source inventory' }
# Upstream's native list includes ref_agc_skybox.c but omits its cache
# dependency (the upstream host skybox rule includes both). Link the genuine
# implementation rather than an unresolved-symbol override or a fake stub.
$extraSources=@('src/ref_agc_gpu_texture_cache.c')
$objects=@();$entries=@()
foreach($file in $sources+$extraSources+@('native/ps5log/ps5log.c','native/ps5log/ps5log_ps5_net.c')) {
    $sourceFile=if($file -eq 'native/main.c' -or $file -eq 'src/ps5_color_target.c') { Join-Path $out $file } else { Join-Path $source $file }
    $object=Join-Path $out ('obj/'+$file.Replace('/','_')+'.o')
    $extra=if($file -eq 'native/ps5log/ps5log.c') { @('-include',"$source/native/ps5log/ps5log_ps5_net.h") } else { @() }
    & "$bin/clang.exe" @flags @extra -std=c11 -c $sourceFile -o $object > ($object+'.log') 2>&1
    if($LASTEXITCODE -ne 0) { throw "Native renderer compilation failed: $file ($object.log)" }
    $objects+=$object;$entries+=@{Source=$file;InputSha256=(Get-FileHash $sourceFile).Hash;ObjectSha256=(Get-FileHash $object).Hash}
}
& "$bin/clang.exe" @flags -c "$inputs/shader_assets.S" -o "$out/obj/shader_assets.o" > "$out/assets.log" 2>&1
if($LASTEXITCODE -ne 0) { throw 'Original shader assembly failed' }
$objects+="$out/obj/shader_assets.o"
& "$bin/clang++.exe" @flags -std=c++20 -fno-exceptions -fno-rtti -c "$foundation/tooling/native/app_crt.cpp" -o "$out/obj/crt.o" > "$out/crt.log" 2>&1
if($LASTEXITCODE -ne 0) { throw 'Native CRT compilation failed' }
foreach($library in @('libSceAgc','libSceAgcDriver')) {
    & "$bin/clang.exe" @flags -std=c11 -fPIC -c "$source/native/stubs/$library.c" -o "$out/obj/$library.o" > "$out/$library-compile.log" 2>&1
    if($LASTEXITCODE -ne 0) { throw "Native stub compilation failed: $library" }
    & "$bin/ld.lld.exe" -m elf_x86_64 --shared -soname "$library.prx" -o "$out/$library.so" "$out/obj/$library.o" > "$out/$library-link.log" 2>&1
    if($LASTEXITCODE -ne 0) { throw "Native stub link failed: $library" }
}
$libraries=@(Get-ChildItem "$sdk/target/lib" -Filter '*.so' | Sort-Object Name | ForEach-Object FullName)
& "$bin/ld.lld.exe" -m elf_x86_64 -pie -z max-page-size=0x4000 -mllvm -emulated-tls --hash-style=gnu -T "$foundation/tooling/native/ps5-pie.ld" --eh-frame-hdr --version-script "$foundation/tooling/native/app-symbols.map" -e _start -o "$out/llvm-pie.elf" "$out/obj/crt.o" @objects --as-needed @libraries "$out/libSceAgc.so" "$out/libSceAgcDriver.so" > "$out/link.log" 2>&1
if($LASTEXITCODE -ne 0) { throw "Native renderer link failed: $out/link.log" }
& $converter link --in "$out/llvm-pie.elf" --out "$out/eboot.elf" --stub-dir "$sdk/target/lib" --module-sdk 0x02000009 --stub "$out/libSceAgc.so" --stub "$out/libSceAgcDriver.so" --companion-sdk 0x08050001 --file-name eboot.elf > "$out/convert.log" 2>&1
if($LASTEXITCODE -ne 0) { throw "Native renderer conversion failed: $out/convert.log" }
$app=Join-Path $out 'app/PPSA99996'
New-Item -ItemType Directory -Path "$app/sce_sys","$app/sce_module" -Force | Out-Null
& $converter self --sign --in "$out/eboot.elf" --out "$app/eboot.bin" --magic 0x1D3D154F > "$out/self.log" 2>&1
if($LASTEXITCODE -ne 0) { throw 'Native renderer SELF packaging failed' }
& $converter self --inspect --file "$app/eboot.bin" > "$out/inspect.log" 2>&1
if($LASTEXITCODE -ne 0 -or [IO.File]::ReadAllText("$out/inspect.log") -notmatch 'integrity: valid') { throw 'Native renderer SELF integrity check failed' }
Copy-Item "$source/sce_sys/param.json","$source/sce_sys/icon0.png" "$app/sce_sys"
Copy-Item "$deps/runtime-windows/libc-a.prx" "$app/sce_module/libc.prx"
Copy-Item "$inputs/map/map.ps5bsp" "$app/map.ps5bsp"
@{SourcePin=$pin;NativeRendererBuilt=$true;NativeRendererExecuted=$false;InputRoot=$inputs;App=$app
    GameAssetsUsed=$false;OriginalMainSha256=(Get-FileHash "$source/native/main.c").Hash;OverlayMainSha256=(Get-FileHash "$out/native/main.c").Hash
    SelfSha256=(Get-FileHash "$app/eboot.bin").Hash;ConverterSha256=(Get-FileHash $converter).Hash;Objects=$entries
    AppFiles=@(Get-ChildItem -LiteralPath $app -Recurse -File | ForEach-Object { @{Path=[IO.Path]::GetRelativePath($app,$_.FullName);Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    BuilderSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
    ConsumerAbi='Explicit init revision 8 and legacy HLE 127/78/12-byte defaults; not original FW12.02 ABI'
    CompilerFlags=$flags;SdkCompatibilityDiagnostic='unreachable-code-generic-assoc in SDK v0.42 math.h only; runtime checks unchanged'
    AdditionalUpstreamSources=$extraSources
    ShaderAperture=[bool]$ShaderAperture;ApertureScope='Optional fixed BSP 0x200000000 / shader 0x210000000 mappings through public API; original address checks retained'
    HostContractsPassed=$false;Scope='Native renderer-only diagnostic with original shaders and synthetic BSP, not full Xash3D engine or Half-Life'
} | ConvertTo-Json -Depth 6 | Set-Content "$out/native-report.json" -Encoding utf8
Write-Output "PASS diagnostic native Xash3D BSP renderer built, not executed: $out"
