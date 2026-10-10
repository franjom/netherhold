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
