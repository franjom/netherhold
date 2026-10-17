// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Data.Wad;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public sealed class TriangleDepthOrderTests
{
    [Fact]
    public void ReusedSortMatchesStableOrderingAcrossDirectionsAndGeometryChanges()
    {
        var random = new Random(42);
        Vector3[] positions = Enumerable.Range(0, 90)
            .Select(_ => new Vector3(random.Next(-2, 3), random.Next(-2, 3), random.Next(-2, 3))).ToArray();
        RenderTriangle[] triangles = Enumerable.Range(0, 180)
            .Select(i => new RenderTriangle(random.Next(90), random.Next(90), random.Next(90), i % 3)).ToArray();
        var buffer = new TriangleDepthOrderBuffer();
        foreach (Vector3 forward in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.Zero, Vector3.UnitX })
        {
            var expected = triangles.Select((t, i) => (t, i, depth: Vector3.Dot(positions[t.A] + positions[t.B] + positions[t.C], forward)))
                .OrderByDescending(e => e.depth).ThenBy(e => e.i).Select(e => e.t).ToArray();
            Assert.Equal(expected, buffer.Sort(positions, triangles, forward).SelectMany(r => r.Triangles));
            AssertRuns(triangles, forward);
        }
        Assert.Empty(buffer.Sort(positions, [], Vector3.UnitZ));
        Assert.Single(buffer.Sort(positions, triangles.Take(1).ToArray(), Vector3.UnitZ));
        Assert.Equal(triangles.Length, buffer.Sort(positions, triangles, Vector3.UnitZ).Sum(r => r.Triangles.Count));
        AssertRuns(triangles.Concat(triangles.Take(37)).ToArray(), -Vector3.UnitX);

        void AssertRuns(RenderTriangle[] source, Vector3 forward)
        {
            var expected = TriangleDepthOrder.Sort(positions, source, forward);
            var actual = buffer.Sort(positions, source, forward);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].MaterialIndex, actual[i].MaterialIndex);
                Assert.Equal(expected[i].Triangles, actual[i].Triangles);
            }
        }
    }

    [Fact]
    public void ReusedSortAllocatesNoManagedMemoryAfterCapacityWarmup()
    {
        Vector3[] positions = [Vector3.Zero, Vector3.UnitZ, -Vector3.UnitZ];
        RenderTriangle[] triangles = [new(0, 0, 0, 0), new(1, 1, 1, 1), new(2, 2, 2, 0)];
        var buffer = new TriangleDepthOrderBuffer();
        for (int i = 0; i < 100; i++) buffer.Sort(positions, triangles, i % 2 == 0 ? Vector3.UnitZ : -Vector3.UnitZ);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) buffer.Sort(positions, triangles, i % 2 == 0 ? Vector3.UnitZ : -Vector3.UnitZ);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AlphaFacesInterleaveAcrossMaterialsAndReverseWithTheCamera()
    {
        Vector3[] positions = [new(0, 0, 1), new(0, 0, 3), new(0, 0, 2)];
        RenderTriangle[] triangles = [new(0, 0, 0, 0), new(1, 1, 1, 0), new(2, 2, 2, 1)];
        var front = TriangleDepthOrder.Sort(positions, triangles, Vector3.UnitZ);
        Assert.Equal([1, 2, 0], front.SelectMany(r => r.Triangles).Select(t => t.A));
        Assert.Equal([0, 1, 0], front.Select(r => r.MaterialIndex));
        var back = TriangleDepthOrder.Sort(positions, triangles, -Vector3.UnitZ);
        Assert.Equal([0, 2, 1], back.SelectMany(r => r.Triangles).Select(t => t.A));
    }

    [Fact]
    public void DepthUsesTheThreeVerticesTogetherAndEqualDepthKeepsSourceOrder()
    {
        Vector3[] positions = [new(0, 0, 9), Vector3.Zero, new(0, 0, 4), new(1, 0, 4)];
        RenderTriangle[] triangles = [new(0, 1, 1, 0), new(2, 2, 2, 1), new(3, 3, 3, 0)];
        var sorted = TriangleDepthOrder.Sort(positions, triangles, Vector3.UnitZ);
        Assert.Equal([2, 3, 0], sorted.SelectMany(r => r.Triangles).Select(t => t.A));
    }

    [InstallFact]
    public void PortalCrystalsAndChasmShareOneDepthOrderedStream()
    {
        using var wad = WadArchive.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Meshes.WAD"));
        var positions = new List<Vector3>();
        var triangles = new List<RenderTriangle>();
        var materials = new Dictionary<string, int>();
        for (int tile = 0; tile < 9; tile++)
        {
            KmfModel model = KmfReader.Read(wad.Extract($"PORTAL{tile}.kmf"));
            Assert.Equal(KmfKind.Mesh, model.Kind);
            foreach (KmfPrimitive primitive in model.Mesh!.Primitives)
            {
                KmfMaterial material = model.Materials[(int)primitive.MaterialIndex];
                if (material.Blend == KmfBlend.Opaque) continue;
                if (!materials.TryGetValue(material.TextureNames[0], out int slot))
                    materials.Add(material.TextureNames[0], slot = materials.Count);
                int start = positions.Count;
                positions.AddRange(primitive.Vertices.Select(v => RenderMesh.ToRenderFrame(
                    v.Position + new Vector3(tile % 3 + 0.5f, tile / 3 + 0.5f, 0))));
                var indices = primitive.IndicesByDetailLevel[0];
                for (int i = 0; i < indices.Count; i += 3)
                    triangles.Add(new(start + indices[i], start + indices[i + 1], start + indices[i + 2], slot));
            }
        }
        Assert.Equal(2, materials.Count);
        Vector3 forward = Vector3.Normalize(new Vector3(1, -1, 1));
        var runs = TriangleDepthOrder.Sort(positions, triangles, forward);
        RenderTriangle[] sorted = runs.SelectMany(r => r.Triangles).ToArray();
        Assert.Equal(triangles.Count, sorted.Length);
        Assert.True(runs.Count > 2, "The crystal and chasm materials must interleave.");
        float Depth(RenderTriangle t) => Vector3.Dot(positions[t.A] + positions[t.B] + positions[t.C], forward);
        for (int i = 1; i < sorted.Length; i++) Assert.True(Depth(sorted[i - 1]) >= Depth(sorted[i]));
        Assert.Equal(triangles.OrderBy(t => t.A).ThenBy(t => t.B).ThenBy(t => t.C),
            sorted.OrderBy(t => t.A).ThenBy(t => t.B).ThenBy(t => t.C));
    }
}
