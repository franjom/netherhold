// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Data.Wad;
using DK2.Simulation.Navigation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class PortalCrystalMeshTests
{
    /// <summary>Render height 1.85 is the floor (1.0) plus 0.85 tile of body; KMF z points down.</summary>
    private const float BodyTop = -1.85f;

    [InstallTheory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void EachFootprintIsTheGemHullBelowBodyHeight(int piece)
    {
        using var wad = WadArchive.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Meshes.WAD"));
        KmfModel model = KmfReader.Read(wad.Extract($"PORTAL{piece}.kmf"));
        var points = new List<Vector2>();
        foreach (KmfPrimitive primitive in model.Mesh!.Primitives)
        {
            if (!model.Materials[(int)primitive.MaterialIndex].TextureNames.Any(t => t.StartsWith("Portal_Gem", StringComparison.Ordinal))) continue;
            var indices = primitive.IndicesByDetailLevel[0];
            for (int i = 0; i < indices.Count; i += 3)
            for (int e = 0; e < 3; e++)
            {
                Vector3 p = primitive.Vertices[indices[i + e]].Position, q = primitive.Vertices[indices[i + ((e + 1) % 3)]].Position;
                if (p.Z >= BodyTop) points.Add(new(p.X, p.Y));
                if ((p.Z >= BodyTop) != (q.Z >= BodyTop))
                {
                    Vector3 cut = p + ((q - p) * ((BodyTop - p.Z) / (q.Z - p.Z)));
                    points.Add(new(cut.X, cut.Y));
                }
            }
        }
        Assert.NotEmpty(points);
        var hull = PortalCrystalShape.ForPiece(piece)!.Vertices.Select(v => new Vector2(v.X / 65536f, v.Y / 65536f)).ToArray();
        const float tolerance = 3e-5f;
        for (int i = 0; i < hull.Length; i++)
        {
            Vector2 a = hull[i], b = hull[(i + 1) % hull.Length], centre = hull.Aggregate(Vector2.Zero, (s, v) => s + v) / hull.Length;
            var outward = Vector2.Normalize(new Vector2(b.Y - a.Y, a.X - b.X));
            if (Vector2.Dot(outward, centre - a) > 0) outward = -outward;
            Assert.All(points, p => Assert.True(Vector2.Dot(outward, p - a) <= tolerance, $"{p} outside edge {i}"));
            Assert.Contains(points, p => Vector2.Distance(p, a) <= tolerance);
        }
    }
}
