// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>Which tiles the possession cameras draw (possession-warp.md §8).</summary>
public sealed class PossessionVisibilityTests
{
    private const int Size = 40, EyeX = 20, EyeY = 20;

    private static bool[] Map(params (int X, int Y)[] solid)
    {
        var map = new bool[Size * Size];
        foreach ((int x, int y) in solid) map[(y * Size) + x] = true;
        return map;
    }

    /// <summary>Every tile within <paramref name="reach"/> of the camera's: a polygon all round.</summary>
    private static TilePos[] Around(int reach) =>
        [.. from y in Enumerable.Range(EyeY - reach, (2 * reach) + 1)
            from x in Enumerable.Range(EyeX - reach, (2 * reach) + 1)
            select new TilePos(x, y)];

    private static List<TilePos> Walk(bool[] map, TilePos[] polygon, double eyeX = EyeX + 0.5, double eyeY = EyeY + 0.5)
    {
        var tiles = new List<TilePos>();
        new PossessionVisibility().Walk(eyeX, eyeY, Size, Size, map, polygon, tiles);
        return tiles;
    }

    [Fact]
    public void OnOpenGroundEveryPolygonTileIsListedOnceInRingOrder_StronglyInferredFromBinary()
    {
        List<TilePos> tiles = Walk(Map(), Around(3));

        Assert.Equal(49, tiles.Count);
        Assert.Equal(49, tiles.Distinct().Count());
        // The camera's tile, then ring 1: the north row from its middle out, the south row, west, east.
        Assert.Equal(
            new TilePos[]
            {
                new(20, 20),
                new(20, 19), new(19, 19), new(21, 19),
                new(20, 21), new(19, 21), new(21, 21),
                new(19, 20), new(21, 20),
                new(20, 18),
            },
            tiles.Take(10));
    }

    [Fact]
    public void TheWalkEndsWithThePolygonAndListsNothingOutsideIt()
    {
        // A polygon to the north only: three tiles wide, six deep.
        TilePos[] polygon = [.. from y in Enumerable.Range(14, 7) from x in Enumerable.Range(19, 3) select new TilePos(x, y)];

        List<TilePos> tiles = Walk(Map(), polygon);

        Assert.Equal(polygon.OrderBy(t => t.Y).ThenBy(t => t.X), tiles.OrderBy(t => t.Y).ThenBy(t => t.X));
    }

    /// <summary>A wall right across, three tiles north: the wall is drawn, the ground behind its
    /// middle is not.</summary>
    [Fact]
    public void AWallIsDrawnAndHidesWhatLiesBehindIt_StronglyInferredFromBinary()
    {
        bool[] map = Map([.. Enumerable.Range(0, Size).Select(x => (x, 17))]);

        List<TilePos> tiles = Walk(map, Around(8));

        Assert.Contains(new TilePos(20, 17), tiles);
        Assert.Contains(new TilePos(15, 17), tiles);
        Assert.Contains(new TilePos(20, 18), tiles);
        Assert.DoesNotContain(new TilePos(20, 16), tiles);
        Assert.DoesNotContain(new TilePos(20, 13), tiles);
        Assert.DoesNotContain(new TilePos(17, 14), tiles);
        // The other way stays open.
        Assert.Contains(new TilePos(20, 27), tiles);
    }

    [Fact]
    public void ALoneSolidTileHidesNothing_StronglyInferredFromBinary()
    {
        List<TilePos> tiles = Walk(Map((20, 18)), Around(6));

        Assert.Equal(169, tiles.Count);
        Assert.Contains(new TilePos(20, 17), tiles);
        Assert.Contains(new TilePos(20, 14), tiles);
    }

    /// <summary>The same wall with a one-tile doorway straight ahead: the ground seen through the
    /// doorway is drawn, the ground behind the wall to either side is not.</summary>
    [Fact]
    public void ThroughADoorwayOnlyItsWedgeIsDrawn_StronglyInferredFromBinary()
    {
        bool[] map = Map([.. Enumerable.Range(0, Size).Where(x => x != 20).Select(x => (x, 17))]);

        List<TilePos> tiles = Walk(map, Around(8));

        Assert.Contains(new TilePos(20, 17), tiles);
        Assert.Contains(new TilePos(20, 16), tiles);
        Assert.Contains(new TilePos(20, 12), tiles);
        Assert.DoesNotContain(new TilePos(14, 15), tiles);
        Assert.DoesNotContain(new TilePos(26, 15), tiles);
        Assert.DoesNotContain(new TilePos(13, 13), tiles);
    }

    [Fact]
    public void OffTheMapBlocksAsSolidDoes()
    {
        // The eye two tiles from the map's west edge: the edge column is drawn, nothing is listed twice.
        List<TilePos> tiles = Walk(Map(), [.. from y in Enumerable.Range(15, 11) from x in Enumerable.Range(-3, 9) select new TilePos(x, y)],
            eyeX: 2.5, eyeY: 20.5);

        Assert.Contains(new TilePos(0, 20), tiles);
        Assert.Contains(new TilePos(-1, 20), tiles);
        Assert.DoesNotContain(new TilePos(-3, 20), tiles);
        Assert.Equal(tiles.Count, tiles.Distinct().Count());
    }
}
