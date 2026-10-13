# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
# The generated converter overlay/application retain upstream GPL-3.0-or-later.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deps=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
$foundation=Join-Path $deps 'native-foundation'
$native=Join-Path $foundation 'tooling/native'
& "$PSScriptRoot/Test-GearsWindowsCrossCompile.ps1" -VerifySdkOnly
& "$PSScriptRoot/Get-GearsClang18.ps1"
$sdk=Join-Path $deps 'sdk-v0.42/ps5-payload-sdk'
$bin=Join-Path $deps 'clang18/llvm-mingw-20240619-ucrt-x86_64/bin'
$out=Join-Path $deps 'foundation-demo-windows'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$runtime=Join-Path $deps 'runtime-windows'
if((Get-FileHash "$runtime/libc-a.prx").Hash -ne 'E6FF45D16ADF687855CC3B33B0C8A4132B6504360B221E0A34C7E99FB3BA0036') { throw 'Build/verify the pinned foundation runtime first' }
$objects=@(); $inputs=@()
foreach($source in @('src/main.cpp','src/demo_renderer.cpp','tooling/native/app_crt.cpp','tooling/native/app_cpp_runtime.cpp')) {
    $object=Join-Path $out ($source.Replace('/','_')+'.o')
    & "$bin/clang++.exe" -target x86_64-sie-ps5 -fvisibility-nodllstorageclass=default -isysroot $sdk -isystem "$sdk/target/include/c++/v1" -isystem "$sdk/target/include" -fno-stack-protector -fno-plt -femulated-tls -std=c++20 -O2 -Wall -Wextra -fno-exceptions -fno-rtti -ffunction-sections -fdata-sections -c "$foundation/$source" -o $object > "$object.log" 2>&1
    if($LASTEXITCODE) { throw "Compile failed: $source" }
    $objects+=$object; $inputs+=@{Source=$source; Sha256=(Get-FileHash "$foundation/$source").Hash}
}
$libraries=@(Get-ChildItem "$sdk/target/lib" -Filter '*.so' | Sort-Object Name | ForEach-Object FullName)
$linkFlags=@('-m','elf_x86_64','-pie','-z','max-page-size=0x4000','-mllvm','-emulated-tls','--hash-style=gnu','-T',"$native/ps5-pie.ld",'--eh-frame-hdr','--version-script',"$native/app-symbols.map",'-e','_start')
& "$bin/ld.lld.exe" @linkFlags -o "$out/llvm-pie.elf" @objects --as-needed @libraries > "$out/link.log" 2>&1
if($LASTEXITCODE) { throw 'PIE link failed' }
$before=[IO.File]::ReadAllText("$native/sce_module_writer.cpp")
$old='const Section &relro_source = section(image, ".data.rel.ro");'
if(($before.Split(@($old),[StringSplitOptions]::None)).Length -ne 2) { throw 'Review changed converter source before applying the local overlay' }
$new=@'
const auto relro_iterator = std::find_if(
        image.sections.begin(), image.sections.end(), [&](const Section &candidate) {
            return candidate.allocated() && !candidate.no_bits() &&
                   relro_section(candidate.name) && candidate.address == relro_start;
        });
    require(relro_iterator != image.sections.end(), "RELRO mapping has no stored start section");
    const Section &relro_source = *relro_iterator;
'@
# LLD omits unused .data.rel.ro. Select the actual stored RELRO start (e.g. GOT).
# Keep the original start equality and mapped LOAD congruence checks intact.
[IO.File]::WriteAllText("$out/sce_module_writer.cpp",$before.Replace($old,$new))
& "$bin/clang++.exe" -std=c++20 -O2 -Wall -Wextra -Werror -static -I $native -I "$runtime/zlib-build" -I "$runtime/zlib-1.3.2" "$native/native_app_builder.cpp" "$native/self_container.cpp" "$native/elf_object.cpp" "$out/sce_module_writer.cpp" "$runtime/zlib-build/libzs.a" -o "$out/ps5-native-tool.exe" > "$out/tool-build.log" 2>&1
if($LASTEXITCODE) { throw 'Overlay converter build failed' }
$convertFlags=@('--stub-dir',"$sdk/target/lib",'--module-sdk','0x02000009','--companion-sdk','0x08050001','--file-name','eboot.elf')
& "$runtime/ps5-native-tool.exe" link --in "$out/llvm-pie.elf" --out "$out/original-converter.elf" @convertFlags > "$out/original-negative.log" 2>&1
if($LASTEXITCODE -eq 0 -or [IO.File]::ReadAllText("$out/original-negative.log") -notmatch 'lacks required section .data.rel.ro') { throw 'Original converter no longer reproduces the expected missing-section failure' }
$title=(Get-Content "$foundation/sce_sys/param.json" -Raw | ConvertFrom-Json).titleId
if($title -notmatch '^PPSA\d{5}$') { throw 'Invalid pinned foundation title ID' }
$app=Join-Path $out "app/$title"
New-Item -ItemType Directory -Path "$app/sce_sys","$app/sce_module" -Force | Out-Null
foreach($copy in @('a','b')) {
    & "$out/ps5-native-tool.exe" link --in "$out/llvm-pie.elf" --out "$out/eboot-$copy.elf" @convertFlags > "$out/convert-$copy.log" 2>&1
    if($LASTEXITCODE) { throw 'Native conversion failed' }
    & "$out/ps5-native-tool.exe" self --sign --in "$out/eboot-$copy.elf" --out "$out/eboot-$copy.bin" --magic 0x1D3D154F > "$out/self-$copy.log" 2>&1
    if($LASTEXITCODE) { throw 'SELF packaging failed' }
    & "$out/ps5-native-tool.exe" self --inspect --file "$out/eboot-$copy.bin" > "$out/inspect-$copy.log" 2>&1
    if($LASTEXITCODE -or [IO.File]::ReadAllText("$out/inspect-$copy.log") -notmatch 'integrity: valid') { throw 'Container integrity check failed' }
}
foreach($extension in @('elf','bin')) {
    if((Get-FileHash "$out/eboot-a.$extension").Hash -ne (Get-FileHash "$out/eboot-b.$extension").Hash) { throw 'Converter/container outputs are not reproducible' }
}
Copy-Item -LiteralPath "$out/eboot-a.bin" -Destination "$app/eboot.bin" -Force
Copy-Item -LiteralPath "$foundation/sce_sys/param.json","$foundation/sce_sys/icon0.png" -Destination "$app/sce_sys" -Force
Copy-Item -LiteralPath "$runtime/libc-a.prx" -Destination "$app/sce_module/libc.prx" -Force
Copy-Item -LiteralPath "$foundation/assets" -Destination $app -Recurse -Force
@{Built=$true; UpstreamSourcesModified=$false; Inputs=$inputs; CompilerSha256=(Get-FileHash "$bin/clang++.exe").Hash;
    OriginalConverterFailureVerified=$true; OriginalConverterSourceSha256=(Get-FileHash "$native/sce_module_writer.cpp").Hash;
    OverlaySourceSha256=(Get-FileHash "$out/sce_module_writer.cpp").Hash; ConverterSha256=(Get-FileHash "$out/ps5-native-tool.exe").Hash;
    ElfSha256=(Get-FileHash "$out/eboot-a.elf").Hash; SelfSha256=(Get-FileHash "$out/eboot-a.bin").Hash;
    App=$app; Executed=$false; GearsBuilt=$false; Scope='original upstream foundation demo builds on Windows using a local RELRO-start converter repair; no emulator/game execution'
} | ConvertTo-Json -Depth 5 | Set-Content "$out/report.json" -Encoding utf8
Write-Output 'PASS: original foundation demo converted and packaged twice; original converter failure retained'
