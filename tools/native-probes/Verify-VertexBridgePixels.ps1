# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
param([Parameter(Mandatory)][string]$Directory,[switch]$ColorParameters)
$ErrorActionPreference='Stop'
$frame=Get-ChildItem -LiteralPath $Directory -Filter 'present-*.bgra' | Sort-Object Name | Select-Object -Last 1
if(-not $frame -or $frame.Name -notmatch '-(\d+)x(\d+)-B8G8R8A8Unorm.bgra$') { throw 'Expected actual BGRA swapchain capture' }
$width=[int]$Matches[1];$height=[int]$Matches[2]
if(-not ('VertexBridgePixelOracle' -as [type])) {
 Add-Type -TypeDefinition @'
public static class VertexBridgePixelOracle {
 public static int[] Verify(byte[] data,int w,int h,bool gradient) {
  if(data.Length != checked(w*h*4)) throw new System.Exception("Capture extent mismatch");
  int inside=0,outside=0;
  for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
   double u=(x+0.5)/w,v=(y+0.5)/h;
   bool red=u>0.155 && v>0.155 && u+v<0.97;
   bool black=u<0.095 || v<0.095 || u+v>1.03 || u>0.905 || v>0.905;
   if(!red && !black) continue; // exclude raster/filter boundary band
   int p=(y*w+x)*4;
   if(red && gradient) {
    double a=(u-0.125)/0.75,b=(v-0.125)/0.75;
    double[] rgb={b*255,a*255,(1-a-b)*255}; // capture is BGRA
    for(int c=0;c<3;c++) if(System.Math.Abs(data[p+c]-rgb[c])>5)
     throw new System.Exception("Parameter interpolation mismatch at "+x+","+y+" channel "+c);
   } else if(data[p]!=0 || data[p+1]!=0 || data[p+2]!=(red?255:0))
     throw new System.Exception("Triangle pixel mismatch at "+x+","+y);
   if(red) inside++; else outside++;
  }
  if(inside<1000 || outside<1000) throw new System.Exception("Insufficient independent pixel coverage");
  return new int[]{inside,outside};
 }
}
'@
}
$counts=[VertexBridgePixelOracle]::Verify([IO.File]::ReadAllBytes($frame.FullName),$width,$height,[bool]$ColorParameters)
@{Scope='Independent GPU-produced three-vertex triangle, not Gears/NGG';Frame=$frame.Name;Sha256=(Get-FileHash $frame.FullName).Hash;
 InsidePixels=$counts[0];OutsidePixels=$counts[1];BoundaryBandExcluded=$true;ColorParameters=[bool]$ColorParameters;IndependentTriangleVerified=$true} |
 ConvertTo-Json | Set-Content (Join-Path $Directory 'pixel-report.json') -Encoding utf8
Write-Output "Verified independent triangle pixels: $($counts[0]) inside, $($counts[1]) outside"
