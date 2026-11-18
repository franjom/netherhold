// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation.Tests;

/// <summary>The roof's chunks as both adapters draw them (<see cref="TerrainCeilingScheduler"/>).</summary>
public sealed class TerrainCeilingSchedulerTests
{
    private const int Size = 40;

    private static bool[] Map(params (int X, int Y)[] closed)
    {
        var map = new bool[Size * Size];
        foreach ((int x, int y) in closed) map[(y * Size) + x] = true;
        return map;
    }

    private static List<int> Built(TerrainCeilingScheduler roof, TerrainCeilingGeometry geometry)
    {
        var built = new List<int>();
        for (int chunk = 0; chunk < roof.ChunkCount; chunk++)
            if (roof.TryBuild(chunk, geometry)) built.Add(chunk);
        return built;
    }

    [Fact]
    public void TheFirstReadBuildsEveryChunkAndThenNoneWaits()
    {
        var roof = new TerrainCeilingScheduler(Size, Size);
        var geometry = new TerrainCeilingGeometry();

        Assert.Equal(9, roof.ChunkCount);
        Assert.True(roof.Refresh(Map()));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], Built(roof, geometry));
        Assert.False(roof.AnyDirty);
        Assert.False(roof.Refresh(Map()));
        Assert.Empty(Built(roof, geometry));
    }

    [Fact]
    public void AChunkIsItsOpenTilesPiecesInTheRenderersFrameWithTheRoofAnchor()
    {
        bool[] map = Map((1, 0));
        var roof = new TerrainCeilingScheduler(Size, Size);
        var geometry = new TerrainCeilingGeometry();
        roof.Refresh(map);
        Assert.True(roof.TryBuild(0, geometry));

        // 16 by 16 tiles less the closed one, 9 vertices and 8 triangles each.
        Assert.Equal(255 * 9, geometry.Positions.Count);
        Assert.Equal(255 * 24, geometry.Triangles.Count);

        var field = new TerrainCeilingField(Size, Size);
        field.Refresh(map, []);
        var positions = new Vector3[TerrainCeiling.Vertices];
        var normals = new Vector3[TerrainCeiling.Vertices];
        // The second piece is tile (2, 0): (1, 0) is closed.
        field.Piece(2, 0, null, positions, normals);
        for (int i = 0; i < TerrainCeiling.Vertices; i++)
        {
            Vector3 p = positions[i], n = normals[i];
            Assert.Equal(new Vector3(p.X, -p.Z, -p.Y), geometry.Positions[9 + i]);
            Assert.Equal(new Vector3(n.X, -n.Z, -n.Y), geometry.Normals[9 + i]);
            Assert.Equal(new Vector2(i % 3 * 0.5f, i / 3 * 0.5f), geometry.Uvs[9 + i]);
            Assert.Equal(new Vector2(2, TerrainCeilingScheduler.RoofAnchor), geometry.LightAnchors[9 + i]);
        }
        byte[] order = TerrainCeiling.Triangles.ToArray();
        for (int i = 0; i < order.Length; i++) Assert.Equal(9 + order[i], geometry.Triangles[24 + i]);
        // The roof faces down, and is over the floor (up is +Y here).
        Assert.All(geometry.Normals, n => Assert.True(n.Y < 0));
        Assert.All(geometry.Positions, p => Assert.True(p.Y >= 2f));
    }

    [Fact]
    public void AChunkWithNoOpenTileIsBuiltEmpty()
    {
        bool[] map = Map();
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                map[(y * Size) + x] = true;
        var roof = new TerrainCeilingScheduler(Size, Size);
        var geometry = new TerrainCeilingGeometry();
        roof.Refresh(map);

        Assert.True(roof.TryBuild(0, geometry));
        Assert.Empty(geometry.Positions);
        Assert.Empty(geometry.Triangles);
    }

    [Fact]
    public void ATileThatClosesReachesTheChunksWithinTheChangeReach()
    {
        var roof = new TerrainCeilingScheduler(Size, Size);
        var geometry = new TerrainCeilingGeometry();
        roof.Refresh(Map());
        Built(roof, geometry);

        // (20, 20) is in the middle chunk; 5 tiles reach 15 to 25, which crosses into the chunks before it only.
        Assert.True(roof.Refresh(Map((20, 20))));
        Assert.Equal([0, 1, 3, 4], Built(roof, geometry));

        // Nothing changed since.
        Assert.False(roof.Refresh(Map((20, 20))));
    }

    [Fact]
    public void APresentedTileReachesTheChunksWithinTheBendReach()
    {
        var roof = new TerrainCeilingScheduler(Size, Size);
        var geometry = new TerrainCeilingGeometry();
        roof.Refresh(Map());
        Built(roof, geometry);

        roof.TileChanged(36, 8);
        Assert.True(roof.AnyDirty);
        Assert.Equal([2], Built(roof, geometry));

        roof.TileChanged(17, 31);
        Assert.Equal([3, 4, 6, 7], Built(roof, geometry));
    }

    [Fact]
    public void AChunkMarkedTwiceIsBuiltOnceAndThenNoneWaits()
    {
        var roof = new TerrainCeilingScheduler(Size, Size);
        var geometry = new TerrainCeilingGeometry();
        roof.Refresh(Map());
        Built(roof, geometry);

        roof.TileChanged(36, 8);
        roof.TileChanged(37, 8);
        Assert.True(roof.Refresh(Map((36, 8))));

        Assert.Equal([1, 2], Built(roof, geometry));
        Assert.False(roof.AnyDirty);
    }

    [Fact]
    public void ClearLeavesEveryChunkWaiting()
    {
        var roof = new TerrainCeilingScheduler(Size, Size);
        var geometry = new TerrainCeilingGeometry();
        roof.Refresh(Map());
        Built(roof, geometry);

        roof.Clear();

        Assert.True(roof.AnyDirty);
        Assert.Equal(9, Built(roof, geometry).Count);
    }
}
