// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.Json;

internal static class LightingReference
{
    public static void Compare(string computeDirectory, string imageFile)
    {
        var words = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(computeDirectory, "actual.json")))!;
        var indices = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(computeDirectory, "remapped-indices.json")))!;
        var parts = Path.GetFileName(imageFile).Split('-')[2].Split('x');
        var width = int.Parse(parts[0]); var height = int.Parse(parts[1]);
        var actual = File.ReadAllBytes(imageFile);
        if (actual.Length != width * height * 4) throw new InvalidOperationException("Wrong image extent");
        var reference = new byte[actual.Length];
        var depth = Enumerable.Repeat(double.PositiveInfinity, width * height).ToArray();
        var confidence = new bool[depth.Length];
        var scale = Math.Min(width / 640.0, height / 360.0);
        var offsetX = (width - 640 * scale) / 2;
        var offsetY = (height - 360 * scale) / 2;
        double F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
        double[] Vertex(uint id) => Enumerable.Range(0, 10).Select(i => F(words[(int)id * 12 + i])).ToArray();
        for (var tri = 0; tri < indices.Length; tri += 3)
        {
            var a = Vertex(indices[tri]); var b = Vertex(indices[tri + 1]); var c = Vertex(indices[tri + 2]);
            if (a[3] <= 0 || b[3] <= 0 || c[3] <= 0) throw new InvalidOperationException("Reference requires positive W");
            double X(double[] v) => offsetX + (v[0] / v[3] + 1) * 320 * scale;
            double Y(double[] v) => offsetY + (v[1] / v[3] + 1) * 180 * scale;
            var ax = X(a); var ay = Y(a); var bx = X(b); var by = Y(b); var cx = X(c); var cy = Y(c);
            var area = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(area) < 1e-10) continue;
            var x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            var x1 = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            var y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            var y1 = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++)
            {
                var u = ((by - cy) * (x + .5 - cx) + (cx - bx) * (y + .5 - cy)) / area;
                var v = ((cy - ay) * (x + .5 - cx) + (ax - cx) * (y + .5 - cy)) / area;
                var w = 1 - u - v;
                if (u < 0 || v < 0 || w < 0) continue;
                var z = u * a[2] / a[3] + v * b[2] / b[3] + w * c[2] / c[3];
                var pixel = y * width + x;
                if (z < 0 || z > 1 || z >= depth[pixel]) continue;
                depth[pixel] = z;
                // The screenshot scales a 640x360 target. Stay three source
                // pixels away from triangle edges to exclude sampling/coverage
                // differences; this is not a top-left-rule rasterizer oracle.
                var distanceA = u * Math.Abs(area) / Math.Sqrt((bx-cx)*(bx-cx)+(by-cy)*(by-cy));
                var distanceB = v * Math.Abs(area) / Math.Sqrt((ax-cx)*(ax-cx)+(ay-cy)*(ay-cy));
                var distanceC = w * Math.Abs(area) / Math.Sqrt((ax-bx)*(ax-bx)+(ay-by)*(ay-by));
                confidence[pixel] = Math.Min(distanceA, Math.Min(distanceB, distanceC)) > 3 * scale;
                var inverseW = u / a[3] + v / b[3] + w / c[3];
                double Varying(int component) => (u * a[component] / a[3] + v * b[component] / b[3] + w * c[component] / c[3]) / inverseW;
                var nx = Varying(4); var ny = Varying(5); var nz = Varying(6);
                var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                var diffuse = Math.Max(0, (nx * .4 + ny * .7 + nz) / (length * Math.Sqrt(1.65)));
                var light = .18 + .82 * diffuse;
                for (var channel = 0; channel < 3; channel++)
                    reference[pixel * 4 + (2 - channel)] = (byte)Math.Clamp((int)Math.Round(Varying(7 + channel) * light * 255), 0, 255);
                reference[pixel * 4 + 3] = 255;
            }
        }
        var checkedPixels = 0; var mismatchedPixels = 0; var worstError = 0;
        for (var pixel = 0; pixel < depth.Length; pixel++)
        {
            if (!confidence[pixel]) continue;
            checkedPixels++;
            var error = Enumerable.Range(0, 3).Max(c => Math.Abs(actual[pixel * 4 + c] - reference[pixel * 4 + c]));
            if (error > 3) mismatchedPixels++;
            worstError = Math.Max(worstError, error);
        }
        var directory = Path.GetDirectoryName(imageFile)!;
        File.WriteAllBytes(Path.Combine(directory, $"reference-{width}x{height}.bgra"), reference);
        File.WriteAllText(Path.Combine(directory, "lighting-reference-report.json"), JsonSerializer.Serialize(new {
            Scope = "Independent GLSL lighting/depth reference with inferred aspect-fit presentation; diagnostic only, not rasterization conformance",
            CheckedInteriorPixels = checkedPixels, MismatchedPixels = mismatchedPixels, WorstChannelError = worstError,
            ChannelTolerance = 3, EdgeMarginSourcePixels = 3,
            InteriorLightingMatches = checkedPixels >= 10000 && mismatchedPixels == 0, LightingVerified = false
        }));
        Console.WriteLine($"Lighting reference: {mismatchedPixels}/{checkedPixels} interior pixels differ; maximum channel error {worstError}");
    }
}
