// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

public class NavMeshLocateCacheTests
{
    private const int U = NavMesh.UnitsPerTile;

    [Fact]
    public void ARestoredLocateCacheAnswersAsTheBuildLeftIt()
    {
        var grid = new Grid();
        NavMesh mesh = NavMesh.Build(grid), fresh = NavMesh.Build(grid);
        int[] built = mesh.CaptureLocateCache();
        int[] saved = (int[])built.Clone();
        for (int y = 0; y < grid.Height; y += 3)
            for (int x = 0; x < grid.Width; x += 3)
                mesh.Locate(x * U, y * U);
        int[] scrambled = mesh.CaptureLocateCache();
        Assert.True(built.Where((value, i) => value != scrambled[i]).Count() > 1);

        mesh.RestoreLocateCache(built);

        Assert.Equal(built, mesh.CaptureLocateCache());
        foreach ((int x, int y) in new[] { (4, 4), (8, 8), (66, 40), (66, 100), (64, 64), (128, 128) })
            Assert.Equal(fresh.Locate(x * U, y * U), mesh.Locate(x * U, y * U));
        Assert.Equal(saved, built);
    }

    [Fact]
    public void RestoreLocateCacheRefusesAWrongLength() =>
        Assert.Throws<ArgumentException>(() => NavMesh.Build(new Grid()).RestoreLocateCache(new int[3]));

    private sealed class Grid : INavByteGrid
    {
        public int Width => 132;
        public int Height => 132;
        public byte NavByte(int x, int y) =>
            (x >= 4 && x < 8 && y >= 4 && y < 8) || (x == 66 && y >= 40 && y < 100)
                ? NavBytes.Solid : NavBytes.Floor;
    }
}
