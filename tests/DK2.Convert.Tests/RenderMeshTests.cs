// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

/// <summary>The renderer frame of ADR-0408's classic look: X east, Y up, Z north, left-handed.</summary>
public sealed class RenderMeshTests
{
    /// <summary>Pins the formula; the owner's Level 1 comparison found the layout it gives
    /// unmirrored (ADR-0411).</summary>
    [Fact]
    public void TheMappingIsXMinusZMinusY()
    {
        Assert.Equal(new Vector3(1, -3, -2), RenderMesh.ToRenderFrame(new Vector3(1, 2, 3)));
    }

    /// <summary>A gold pile stands on the floor, and the straight Lair wall (DK2 Y = -0.5) lands
    /// one to two tiles up on the tile's north edge (DK2's Y runs south, ADR-0411).</summary>
    [InstallFact]
    public void PiecesLandOnTheNorthEdge()
    {
        using InstallArtProvider provider = OpenInstall(out GameDatabase _);

        RenderMesh gold = Load(provider, AssetRef.ForObject("Gold", AssetKind.Mesh), out _);
        Assert.All(gold.Positions, position => Assert.True(position.Y >= 0f, $"gold below the floor at {position}"));

        RenderMesh wall = Load(provider, AssetCatalogue.WallPiece("Lair", 7), out _);
        Assert.All(wall.Positions, position =>
        {
            Assert.Equal(0.5f, position.Z, 3);
            Assert.InRange(position.Y, 0.999f, 2.001f);
        });
    }

    /// <summary>
    /// The winding pin: for nearly every triangle, the face normal from its vertex order points
    /// the way the KMF's own vertex normals do. Unity draws the side that normal points to, so a
    /// mapping that kept the frame but got the order wrong would show every piece inside out.
    /// </summary>
    [InstallFact]
    public void TriangleOrderAgreesWithTheStoredNormals()
    {
        using InstallArtProvider provider = OpenInstall(out GameDatabase _);
        AssetRef[] keys =
        [
            AssetRef.ForObject("Gold", AssetKind.Mesh),
            AssetRef.ForTerrain("Rock", AssetKind.Mesh),
            .. Enumerable.Range(AssetCatalogue.FirstWallPiece,
                AssetCatalogue.LastWallPiece - AssetCatalogue.FirstWallPiece + 1)
                .Select(piece => AssetCatalogue.WallPiece("Lair", piece)),
        ];

        int agree = 0, total = 0;
        foreach (AssetRef key in keys)
        {
            RenderMesh mesh = Load(provider, key, out _);
            foreach (int[] list in mesh.Triangles)
                for (int i = 0; i < list.Length; i += 3)
                {
                    Vector3 a = mesh.Positions[list[i]], b = mesh.Positions[list[i + 1]], c = mesh.Positions[list[i + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    if (face.LengthSquared() < 1e-12f) continue;
                    Vector3 stored = mesh.Normals[list[i]] + mesh.Normals[list[i + 1]] + mesh.Normals[list[i + 2]];
                    total++;
                    if (Vector3.Dot(face, stored) > 0f) agree++;
                }
        }
        Assert.True(total > 50, $"only {total} triangles");
        Assert.True(agree >= total * 0.95, $"{agree} of {total} triangles face their stored normals");
    }

    private static InstallArtProvider OpenInstall(out GameDatabase database)
    {
        string install = Dk2InstallGate.Path;
        database = GameDatabaseLoader.Load(install);
        return InstallArtProvider.Open(install, database);
    }

    private static RenderMesh Load(InstallArtProvider provider, AssetRef key, out KmfModel model)
    {
        Assert.True(provider.TryResolve(key, out ResolvedAsset asset), key.ToString());
        Assert.True(provider.TryLoadModel(asset, out model), asset.InstallEntry);
        return RenderMesh.FromModel(model);
    }
}
