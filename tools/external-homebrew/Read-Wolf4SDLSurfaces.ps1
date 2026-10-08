# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
# Read-only diagnostic for the caller-owned source-profile process. Samples are
# concurrent observations, not an atomic snapshot or evidence of pixel correctness.
# Altered C# tile-address equation from SDL_ps5tilemap.c.
# Copyright (C) 2026 John Tornblom <john.tornblom@gmail.com>.
# This software is provided 'as-is', without any express or implied warranty.
# In no event will the authors be held liable for any damages arising from
# the use of this software.
# Permission is granted to anyone to use this software for any purpose,
# including commercial applications, and to alter it and redistribute it
# freely, subject to the following restrictions:
# 1. The origin of this software must not be misrepresented; you must not
# claim that you wrote the original software. If you use this software in a
# product, an acknowledgment in the product documentation would be appreciated
# but is not required.
# 2. Altered source versions must be plainly marked as such, and must not be
# misrepresented as being the original software.
# 3. This notice may not be removed or altered from any source distribution.
function Save-WolfSurfaceSample {
    param([System.Diagnostics.Process]$Process,[string]$Output,[string]$Elf,[int]$Sample)
    # Windows CLI runs guest code in a mitigated child, not the launcher process.
    $parentId=$Process.Id
    $children=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$parentId" |
        Where-Object { $_.Name -ieq 'TouchePx5.exe' -and $_.CommandLine -like '*--sharpemu-mitigated-child*' })
    if($children.Count -ne 1) { throw 'Expected exactly one caller-owned mitigated child' }
    $guestProcess=[Diagnostics.Process]::GetProcessById([int]$children[0].ProcessId)
    try {
    if(-not ('WolfSurfaceReader' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WolfSurfaceReader {
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
    public static byte[] Read(IntPtr handle, ulong address, int length) {
        if(length < 1 || length > 16*1024*1024) throw new ArgumentOutOfRangeException(nameof(length));
        byte[] bytes=new byte[length]; UIntPtr read;
        if(!ReadProcessMemory(handle, new IntPtr(unchecked((long)address)), bytes, (UIntPtr)length, out read) || read.ToUInt64() != (ulong)length)
            throw new InvalidOperationException("ReadProcessMemory address=0x"+address.ToString("X")+" length="+length+" read="+read.ToUInt64()+" error="+Marshal.GetLastWin32Error());
        return bytes;
    }
    private static uint Offset(uint x,uint y) {
        return ((x&1)<<0)|(((x>>1)&1)<<1)|((y&1)<<2)|(((y>>1)&1)<<3)|(((y>>2)&1)<<4)|(((x>>2)&1)<<5)|
            ((((x>>3)^(y>>3))&1)<<6)|((((x>>4)^(y>>4))&1)<<7)|((((x>>6)^(y>>5))&1)<<8)|
            ((((x>>5)^(y>>6))&1)<<9)|(((y>>3)&1)<<10)|(((x>>4)&1)<<11)|(((y>>6)&1)<<12)|
            (((x>>6)&1)<<13)|(((x>>7)&1)<<14)|(((x>>8)&1)<<15);
    }
    public static byte[] ReadTiledHud(IntPtr handle, ulong address,int width,int height) {
        if(width<1 || width>3840 || height<200 || height>2160) throw new ArgumentOutOfRangeException();
        int first=height-200;
        int start=(first/128)*(128*width)*4;
        int end=((height-1)/128)*(128*width)*4+((width+511)/512)*65536*4;
        byte[] source=Read(handle,address+(ulong)start,end-start);
        byte[] result=new byte[width*200*4];
        for(int y=first;y<height;y++) for(int x=0;x<width;x++) {
            int offset=((y/128)*(128*width)+(x/512)*65536+(int)Offset((uint)(x%512),(uint)(y%128)))*4-start;
            Buffer.BlockCopy(source,offset,result,((y-first)*width+x)*4,4);
        }
        return result;
    }
}
'@
    }
    $symbols=& 'C:\BuildTools\VC\Tools\Llvm\x64\bin\llvm-nm.exe' $Elf
    if($LASTEXITCODE -ne 0) { throw 'Cannot resolve source-profile symbols' }
    $report=[ordered]@{Sample=$Sample;ParentProcessId=$parentId;ProcessId=$guestProcess.Id;Utc=[DateTime]::UtcNow.ToString('o');Atomic=$false;Surfaces=@()}
    foreach($name in @('screenBuffer','screen','textureSurface','windowSurface')) {
        $symbolName=if($name -eq 'windowSurface') {'window'} elseif($name -eq 'textureSurface') {'_ZL7texture'} else {$name}
        $symbol=@($symbols | Where-Object { $_ -match ('^[0-9a-fA-F]+\s+[bBdD]\s+'+$symbolName+'$') })
        if($symbol.Count -ne 1) { throw "Missing unique symbol: $name" }
        # The pinned source ELF is loaded at 0x400000 by this explicit profile.
        $address=0x400000UL+[Convert]::ToUInt64(($symbol[0] -split '\s+')[0],16)
        $pointer=[BitConverter]::ToUInt64([WolfSurfaceReader]::Read($guestProcess.Handle,$address,8),0)
        if($pointer -eq 0) { continue }
        if($name -eq 'textureSurface') {
            # SW_LockTexture in this pinned ELF loads SDL_Texture.driverdata
            # at 0x70; the software backend owns an SDL_Surface there.
            $pointer=[BitConverter]::ToUInt64([WolfSurfaceReader]::Read($guestProcess.Handle,$pointer+0x70UL,8),0)
            if($pointer -eq 0) { continue }
        }
        if($name -eq 'windowSurface') {
            # SDL_Window.surface offset from the pinned public SDL_sysvideo.h.
            $windowHeader=[WolfSurfaceReader]::Read($guestProcess.Handle,$pointer,152)
            $pointer=[BitConverter]::ToUInt64($windowHeader,144)
            if($pointer -eq 0) { continue }
        }
        $header=[WolfSurfaceReader]::Read($guestProcess.Handle,$pointer,40)
        $width=[BitConverter]::ToInt32($header,16)
        $height=[BitConverter]::ToInt32($header,20)
        $pitch=[BitConverter]::ToInt32($header,24)
        $pixels=[BitConverter]::ToUInt64($header,32)
        if($width -lt 1 -or $width -gt 3840 -or $height -lt 200 -or $height -gt 2160 -or $pitch -lt $width -or $pitch -gt 16384 -or $pixels -eq 0) {
            throw "Invalid SDL_Surface geometry for $name"
        }
        $rows=[Math]::Min(200,$height)
        $start=$pixels+[uint64](($height-$rows)*$pitch)
        $bytes=[WolfSurfaceReader]::Read($guestProcess.Handle,$start,$rows*$pitch)
        $again=[WolfSurfaceReader]::Read($guestProcess.Handle,$start,$rows*$pitch)
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        $second=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($again))
        $file=('surface-{0:D2}-{1}.bin' -f $Sample,$name)
        [IO.File]::WriteAllBytes((Join-Path $Output $file),$bytes)
        $report.Surfaces+= [ordered]@{Name=$name;Pointer=('0x{0:X}' -f $pointer);Pixels=('0x{0:X}' -f $pixels);Width=$width;Height=$height;Pitch=$pitch;FirstRow=$height-$rows;Rows=$rows;Sha256=$hash;TwoReadsMatch=$hash -eq $second;File=$file}
    }
    $videoSymbol=@($symbols | Where-Object { $_ -match '^[0-9a-fA-F]+\s+[bBdD]\s+_this$' })
    if($videoSymbol.Count -ne 1) { throw 'Missing unique SDL video-device symbol' }
    $videoSlot=0x400000UL+[Convert]::ToUInt64(($videoSymbol[0] -split '\s+')[0],16)
    $video=[BitConverter]::ToUInt64([WolfSurfaceReader]::Read($guestProcess.Handle,$videoSlot,8),0)
    # Confirmed in the bundled ELF's PS5_UpdateWindowFramebuffer disassembly:
    # SDL_VideoDevice.driverdata @ 0x600, PS5_DeviceData.tmap @ 0x68.
    $driver=[BitConverter]::ToUInt64([WolfSurfaceReader]::Read($guestProcess.Handle,$video+0x600UL,8),0)
    $driverHeader=[WolfSurfaceReader]::Read($guestProcess.Handle,$driver,112)
    $tilemap=[BitConverter]::ToUInt64($driverHeader,104)
    $tileHeader=[WolfSurfaceReader]::Read($guestProcess.Handle,$tilemap,568)
    $tileWidth=[BitConverter]::ToInt32($tileHeader,0)
    $tileHeight=[BitConverter]::ToInt32($tileHeader,4)
    $report.Tilemap=[ordered]@{Pointer=('0x{0:X}' -f $tilemap);Width=$tileWidth;Height=$tileHeight;Pending0=@(0..3 | ForEach-Object {[BitConverter]::ToInt32($tileHeader,528+$_*4)});Pending1=@(0..3 | ForEach-Object {[BitConverter]::ToInt32($tileHeader,544+$_*4)});Full0=[BitConverter]::ToInt32($tileHeader,560);Full1=[BitConverter]::ToInt32($tileHeader,564)}
    $report.TiledHud=@(foreach($buffer in 0..1) {
        $pointer=[BitConverter]::ToUInt64($driverHeader,8+$buffer*32)
        $bytes=[WolfSurfaceReader]::ReadTiledHud($guestProcess.Handle,$pointer,$tileWidth,$tileHeight)
        $file=('surface-{0:D2}-tiled{1}.bin' -f $Sample,$buffer)
        [IO.File]::WriteAllBytes((Join-Path $Output $file),$bytes)
        [ordered]@{Buffer=$buffer;Pointer=('0x{0:X}' -f $pointer);File=$file;Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))}
    })
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Output ('surface-{0:D2}.json' -f $Sample)) -Encoding utf8
    } finally { $guestProcess.Dispose() }
}
