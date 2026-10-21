// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Security.Cryptography;
using System.Text.Json;

internal static class BspPixelReference
{
    public static void Verify(string computeRoot,string graphicsRoot)
    {
        using var graphics=JsonDocument.Parse(File.ReadAllText(Path.Combine(graphicsRoot,"graphics-report.json")));
        var g=graphics.RootElement;
        var width=g.GetProperty("Width").GetInt32();var height=g.GetProperty("Height").GetInt32();
        if(width!=640 || height!=360 || !g.GetProperty("GpuDrawsCompleted").GetBoolean()) throw new InvalidOperationException("Unexpected BSP raster extent");
        var captures=g.GetProperty("Captures").EnumerateArray().ToArray();
        byte[] ReadCapture(JsonElement entry)
        {
            var path=entry.GetProperty("File").GetString()!;
            if(!Path.GetFullPath(path).StartsWith(Path.GetFullPath(graphicsRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Capture outside audit directory");
            var bytes=File.ReadAllBytes(path);
            if(bytes.Length!=width*height*4 || Convert.ToHexString(SHA256.HashData(bytes))!=entry.GetProperty("Sha256").GetString()) throw new InvalidOperationException("Captured RGBA bytes differ");
            return bytes;
        }
        var background=ReadCapture(captures[0]);var final=ReadCapture(captures[^1]);
        foreach(var draw in g.GetProperty("Draws").EnumerateArray())
        {
            var name=draw.GetProperty("Case").GetString();
            if(name is not ("background" or "identity")) throw new InvalidOperationException("Unexpected completed source case");
            foreach(var (field,file) in new[] {("CompletedGsSha256","actual.json"),("FixtureSha256","fixture-report.json"),("GpuProofSha256","gpu-result.json")})
                if(draw.GetProperty(field).GetString()!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(computeRoot,name,file)))))
                    throw new InvalidOperationException("Pixel reference inputs differ from completed graphics draw");
        }
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(computeRoot,"identity","fixture-report.json")));
        using var backgroundFixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(computeRoot,"background","fixture-report.json")));
        var vertices=fixture.RootElement.GetProperty("Vertices").Deserialize<float[][]>()!;
        var matrix=fixture.RootElement.GetProperty("MatrixColumnMajor").Deserialize<float[]>()!;
        var flat=backgroundFixture.RootElement.GetProperty("Control").Deserialize<float[]>()!;
        var basePixels=g.GetProperty("BasePixels").Deserialize<byte[]>()!;
        var lightPixels=g.GetProperty("LightPixels").Deserialize<byte[]>()!;
        if(vertices.Length!=3 || matrix.Length!=16 || flat.Length!=4 || basePixels.Length!=16 || lightPixels.Length!=16) throw new InvalidOperationException("Unexpected source reference shape");
        // Explicit positive-height viewport from the recorded graphics fixture.
        // Recompute positions from source GLSL, not actual GS exports/readback.
        var screen=new double[3][];
        for(var v=0;v<3;v++)
        {
            var clip=new double[4];
            for(var row=0;row<4;row++) { clip[row]=matrix[12+row];for(var col=0;col<3;col++) clip[row]+=(double)matrix[col*4+row]*vertices[v][col]; }
            if(!double.IsFinite(clip[3]) || clip[3]<=0) throw new InvalidOperationException("Reference requires positive clip W");
            screen[v]=[(clip[0]/clip[3]*0.5+0.5)*width,(clip[1]/clip[3]*0.5+0.5)*height,clip[3]];
        }
        var denominator=(screen[1][1]-screen[2][1])*(screen[0][0]-screen[2][0])+(screen[2][0]-screen[1][0])*(screen[0][1]-screen[2][1]);
        if(Math.Abs(denominator)<1) throw new InvalidOperationException("Degenerate reference triangle");
        var interior=0;var discarded=0;var opaque=0;var exterior=0;var mismatches=0;var worst=0;
        var missingLightmapMismatches=0;var swappedTexturesMismatches=0;var blackDiscardMismatches=0;
        var seenPairs=new HashSet<int>();var controlPixel=-1;
        bool Match(byte[] actual,int p,double[] expected,bool track)
        {
            var match=true;
            for(var c=0;c<4;c++) { var error=Math.Abs(actual[p+c]-(int)Math.Round(expected[c]));if(track) worst=Math.Max(worst,error);if(error>2) match=false; }
            return match;
        }
        for(var y=0;y<height;y++) for(var x=0;x<width;x++)
        {
            var px=x+0.5;var py=y+0.5;
            var a=((screen[1][1]-screen[2][1])*(px-screen[2][0])+(screen[2][0]-screen[1][0])*(py-screen[2][1]))/denominator;
            var b=((screen[2][1]-screen[0][1])*(px-screen[2][0])+(screen[0][0]-screen[2][0])*(py-screen[2][1]))/denominator;
            var c=1-a-b;var minimum=Math.Min(a,Math.Min(b,c));var p=(y*width+x)*4;
            if(minimum< -0.01)
            {
                exterior++;
                if(!Match(background,p,[0,0,0,0],true) || !Match(final,p,[0,0,0,0],true)) mismatches++;
                continue;
            }
            if(minimum<=0.01) continue; // raster edge/coverage precision excluded
            var weights=new[] {a/screen[0][2],b/screen[1][2],c/screen[2][2]};var sum=weights.Sum();
            var uv=new double[4];for(var component=0;component<4;component++) for(var v=0;v<3;v++) uv[component]+=weights[v]*vertices[v][3+component]/sum;
            if(uv.Any(t=>Math.Abs(t-0.5)<0.015)) continue; // nearest-texel boundary precision excluded
            var baseIndex=Math.Clamp((int)Math.Floor(uv[1]*2),0,1)*2+Math.Clamp((int)Math.Floor(uv[0]*2),0,1);
            var lightIndex=Math.Clamp((int)Math.Floor(uv[3]*2),0,1)*2+Math.Clamp((int)Math.Floor(uv[2]*2),0,1);
            double[] flatColor=[flat[0]*255,flat[1]*255,flat[2]*255,255];
            interior++;
            if(!Match(background,p,flatColor,true)) mismatches++;
            if(basePixels[baseIndex*4+3]<128)
            {
                discarded++;
                if(!Match(final,p,flatColor,true)) mismatches++;
                if(!Match(final,p,[0,0,0,0],false)) blackDiscardMismatches++;
            }
            else
            {
                opaque++;seenPairs.Add(baseIndex*4+lightIndex);
                if(controlPixel<0) controlPixel=y*width+x;
                var expected=new double[4];var missing=new double[4];var swapped=new double[4];expected[3]=missing[3]=swapped[3]=255;
                for(var channel=0;channel<3;channel++)
                {
                    expected[channel]=basePixels[baseIndex*4+channel]*lightPixels[lightIndex*4+channel]/255.0;
                    missing[channel]=basePixels[baseIndex*4+channel];
                    swapped[channel]=lightPixels[baseIndex*4+channel]*basePixels[lightIndex*4+channel]/255.0;
                }
                if(!Match(final,p,expected,true)) mismatches++;
                if(!Match(final,p,missing,false)) missingLightmapMismatches++;
                if(!Match(final,p,swapped,false)) swappedTexturesMismatches++;
            }
        }
        var passed=mismatches==0 && interior>20000 && discarded>1000 && opaque>1000 && exterior>100000 && seenPairs.Count>=4 &&
            missingLightmapMismatches>1000 && swappedTexturesMismatches>1000 && blackDiscardMismatches>1000;
        File.WriteAllText(Path.Combine(graphicsRoot,"pixel-reference.json"),JsonSerializer.Serialize(new {
            Scope="Independent source GLSL interior/exterior RGBA reference; raster and nearest-texel boundaries excluded, tolerance 2/255",
            InteriorTexturingAndDiscardVerified=passed,InteriorPixels=interior,OpaquePixels=opaque,DiscardedPixels=discarded,ExteriorPixels=exterior,
            Mismatches=mismatches,MaximumChannelError=worst,DistinctBaseLightmapPairs=seenPairs.Count,
            CorruptionControlPixel=controlPixel,
            MissingLightmapMismatches=missingLightmapMismatches,SwappedTexturesMismatches=swappedTexturesMismatches,BlackDiscardMismatches=blackDiscardMismatches,
            BackgroundSha256=Convert.ToHexString(SHA256.HashData(background)),FinalSha256=Convert.ToHexString(SHA256.HashData(final)),
            NativeTitleExecuted=false,WholeImageConformanceVerified=false
        },new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"BSP pixel reference: {mismatches} mismatches, {interior} interior ({discarded} discard), {exterior} exterior, worst={worst}");
        if(!passed) throw new InvalidOperationException("Original BSP texture/discard pixel reference failed");
    }
}
