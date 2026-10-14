// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

// Explicit audit partition, not a model of AMD's hardware subgroup scheduler.
internal static class MeshReplayReference
{
    public static void Consume(string replayFile, string outputDirectory)
    {
        using var replay = JsonDocument.Parse(File.ReadAllText(replayFile));
        var draw = replay.RootElement.GetProperty("DrawContext").Deserialize<Gen5ShaderReplayDrawContext>()!;
        var batch = Partition(draw);
        var actual = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(outputDirectory, "actual.json")))!;
        var primitiveFirst = checked((int)batch.OutputSlotCapacity * 12);
        var expectedLength = checked((int)batch.OutputSlotCapacity * 26 + batch.GroupCount * 4 + 16);
        if (actual.Length != expectedLength) throw new InvalidOperationException("Wrong completed-readback extent");
        var indices = new List<uint>();
        var idleRejected = 0;
        for (uint first = 0; first < draw.DrawCount; first += 126)
        {
            var live = Math.Min(126u, draw.DrawCount - first);
            var vertexBase = first / 126 * 128;
            for (uint primitive = 0; primitive < 128; primitive++)
            {
                var record = actual.AsSpan(primitiveFirst + (int)(vertexBase + primitive) * 5, 5);
                var consumed = Gen5NggTriangleRemap.TryRemap(record, live, vertexBase, batch.OutputSlotCapacity, out var triangle);
                if (primitive >= live / 3)
                {
                    if (consumed) throw new InvalidOperationException("Idle primitive slot was consumed");
                    idleRejected++;
                    continue;
                }
                if (!consumed || triangle != (vertexBase + primitive * 3, vertexBase + primitive * 3 + 1, vertexBase + primitive * 3 + 2))
                    throw new InvalidOperationException("Completed primitive remap differs from source triangle-list order");
                indices.AddRange([triangle.Vertex0, triangle.Vertex1, triangle.Vertex2]);
                var stale = record.ToArray();
                stale[4] = 2;
                if (Gen5NggTriangleRemap.TryRemap(stale, live, vertexBase, batch.OutputSlotCapacity, out _))
                    throw new InvalidOperationException("Stale noncanonical validity value was accepted");
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory, "remapped-indices.json"), JsonSerializer.Serialize(indices));
        File.WriteAllText(Path.Combine(outputDirectory, "remap-report.json"), JsonSerializer.Serialize(new {
            Scope = "Completed compute readback supplied by caller; checked subgroup-to-output-slot triangle remap only; no graphics submission",
            TriangleCount = indices.Count / 3, IndexCount = indices.Count, OutputSlots = batch.OutputSlotCapacity,
            IdlePrimitiveSlotsRejected = idleRejected, NoncanonicalValidityRejected = true, RenderingVerified = false
        }));
        Console.WriteLine($"PASS completed mesh triangle remap: {indices.Count / 3} triangles, {idleRejected} idle slots rejected");
    }

    public static Gen5NggPassthroughBatch Partition(Gen5ShaderReplayDrawContext draw)
    {
        if (draw.DrawCount == 0 || draw.DrawCount > 2394 || draw.DrawCount % 3 != 0 ||
            draw.InstanceCount != 1 || draw.Indexed || draw.PrimitiveType != 4 || draw.BaseVertex != 0)
            throw new InvalidOperationException("Mesh audit requires a bounded nonindexed triangle list with base vertex zero");
        return Gen5NggPassthroughBatch.CreateNonindexedTriangleList(draw.DrawCount);
    }

    public static (uint[] Expected, int[] FloatWords) Evaluate(Gen5ShaderReplaySnapshot snapshot,
        Gen5ShaderReplayDrawContext draw, uint[] initial, int primitiveFirst)
    {
        var position = snapshot.VertexInputs.Single(v => v.Pc == 0x78);
        var normal = snapshot.VertexInputs.Single(v => v.Pc == 0x80);
        if (position.Stride != 24 || position.OffsetBytes != 0 || normal.Stride != 24 || normal.OffsetBytes != 16 ||
            position.DataLength != draw.DrawCount * 24 || normal.DataLength != position.DataLength ||
            !position.Data.AsSpan(0, position.DataLength).SequenceEqual(normal.Data.AsSpan(0, normal.DataLength)) ||
            snapshot.UserData.Length < 25)
            throw new InvalidOperationException("Unexpected matched interleaved Gears vertex layout");
        double F(uint value) => BitConverter.UInt32BitsToSingle(value);
        var constants = snapshot.UserData.Skip(1).Take(24).Select(F).ToArray();
        if (constants.Any(v => !double.IsFinite(v))) throw new InvalidOperationException("Nonfinite Gears constants");
        var expected = initial.ToArray();
        var floatWords = new List<int>();
        // Independent GLSL reference: column-major matrix multiplication and
        // v + 2*cross(q.xyz, cross(q.xyz,v)+q.w*v), evaluated in double precision.
        for (uint vertex = 0; vertex < draw.DrawCount; vertex++)
        {
            var values = new double[6];
            for (var component = 0; component < 6; component++)
                values[component] = F(BinaryPrimitives.ReadUInt32LittleEndian(position.Data.AsSpan((int)vertex * 24 + component * 4)));
            if (values.Any(v => !double.IsFinite(v))) throw new InvalidOperationException("Nonfinite Gears geometry");
            var exports = new double[10];
            for (var row = 0; row < 4; row++)
                exports[row] = constants[row] * values[0] + constants[4 + row] * values[1] +
                    constants[8 + row] * values[2] + constants[12 + row];
            double[] q = [constants[16], constants[17], constants[18]];
            double[] n = [values[3], values[4], values[5]];
            static double[] Cross(double[] a, double[] b) =>
                [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
            var inner = Cross(q, n);
            for (var component = 0; component < 3; component++) inner[component] += constants[19] * n[component];
            var outer = Cross(q, inner);
            for (var component = 0; component < 3; component++) exports[4 + component] = n[component] + 2 * outer[component];
            for (var component = 0; component < 3; component++) exports[7 + component] = constants[20 + component];
            var slot = (int)(vertex / 126 * 128 + vertex % 126);
            for (var component = 0; component < 10; component++)
            {
                expected[slot * 12 + component] = BitConverter.SingleToUInt32Bits((float)exports[component]);
                if (component < 7) floatWords.Add(slot * 12 + component);
            }
        }
        for (uint first = 0; first < draw.DrawCount; first += 126)
        {
            var count = Math.Min(126u, draw.DrawCount - first);
            for (uint triangle = 0; triangle < count / 3; triangle++)
            {
                var v = triangle * 3;
                uint[] words = [v | ((v + 1) << 10) | ((v + 2) << 20), v, v + 1, v + 2, 1];
                Array.Copy(words, 0, expected, primitiveFirst + ((int)(first / 126 * 128 + triangle)) * 5, 5);
            }
        }
        return (expected, floatWords.ToArray());
    }
}
