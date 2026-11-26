// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>Which torches bend again after a tile push (migration plan step 2j): the lattice forgets within one of the
/// pushed tile and a torch reads within one of its wall, so a torch within two refreshes.</summary>
public sealed class LatticeDirtyTrackerTests
{
    private const int Width = 12, Height = 10;

    private static int Index(int x, int y) => (y * Width) + x;

    [Fact]
    public void NothingIsTouchedAtFirst()
    {
        var tracker = new LatticeDirtyTracker(Width, Height);
        Assert.False(tracker.AnyTouched);
        Assert.False(tracker.TouchedNear(new TilePos(5, 5)));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, -1, true)]
    [InlineData(2, 2, true)]
    [InlineData(-2, 1, true)]
    [InlineData(0, -2, true)]
    [InlineData(3, 0, false)]
    [InlineData(0, 3, false)]
    [InlineData(-3, -3, false)]
    [InlineData(2, -3, false)]
    public void ATorchWithinTwoOfAPushedTileRefreshes(int dx, int dy, bool expected)
    {
        var tracker = new LatticeDirtyTracker(Width, Height);
        tracker.Touch(Index(5 + dx, 5 + dy));
        Assert.True(tracker.AnyTouched);
        Assert.Equal(expected, tracker.TouchedNear(new TilePos(5, 5)));
    }

    [Fact]
    public void AWallAtTheEdgeLooksOnlyInsideTheMap()
    {
        var tracker = new LatticeDirtyTracker(Width, Height);
        tracker.Touch(Index(Width - 1, Height - 1));
        Assert.True(tracker.TouchedNear(new TilePos(Width - 1, Height - 1)));
        Assert.True(tracker.TouchedNear(new TilePos(Width + 1, Height + 1)));
        Assert.False(tracker.TouchedNear(new TilePos(0, 0)));
        // An edge row above the map wraps nowhere: the tile at the far end of the row before is not near.
        tracker.Clear();
        tracker.Touch(Index(Width - 1, 3));
        Assert.False(tracker.TouchedNear(new TilePos(0, 4)));
    }

    [Fact]
    public void ClearForgetsEveryPushedTile()
    {
        var tracker = new LatticeDirtyTracker(Width, Height);
        tracker.Touch(Index(5, 5));
        tracker.Clear();
        Assert.False(tracker.AnyTouched);
        Assert.False(tracker.TouchedNear(new TilePos(5, 5)));
    }

    [Fact]
    public void TheReachesAreOneAndTwo()
    {
        Assert.Equal(1, LatticeDirtyTracker.ForgetReach);
        Assert.Equal(2, LatticeDirtyTracker.TorchReach);
    }

    /// <summary>The lattice side: a change forgets its own cell and its eight neighbours', and no cell beyond.</summary>
    [Fact]
    public void TheLatticeForgetsTheCellsWithinOneOfAChange()
    {
        TerrainDef rock = new()
        {
            Name = "rock", TerrainId = 2, Flags = TerrainFlags.Solid, StartingHealth = 1, MaxHealth = 1, GoldValue = 0,
            ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var database = new GameDatabase([rock], [], [], new GlobalVariables());
        var map = new LevelMap(Width, Height, Enumerable.Repeat(new MapTile(2, PlayerIds.Unowned, 0, 0), Width * Height).ToArray());
        var world = WorldState.FromMap(database, map, []);
        var lattice = new TerrainLattice(database, world, new PlayerId(PlayerIds.FirstKeeper));
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                lattice.Prepare(x, y);

        lattice.Invalidate(5, 5);

        for (int y = 2; y <= 8; y++)
            for (int x = 2; x <= 8; x++)
                Assert.Equal(Math.Abs(x - 5) > 1 || Math.Abs(y - 5) > 1, lattice.IsBuilt(x, y));
    }
}
