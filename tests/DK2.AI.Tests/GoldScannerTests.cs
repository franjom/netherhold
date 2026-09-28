// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI.Tests;

public class GoldScannerTests
{
    [Fact]
    public void HistoricalCutoffLetsALaterFartherSeamReplaceANearerFourthSeam()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        TilePos[] seams = [new(13, 13), new(15, 13), new(17, 13), new(2, 16), new(12, 17), new(19, 19)];
        foreach (TilePos seam in seams) KeeperWorld.Set(world, db, seam, KeeperWorld.Gold);

        List<GoldDeposit> found = GoldScanner.Scan(new KeeperView(db, world, KeeperWorld.Keeper));

        // Squared distances in encounter order: 8, 4, 8, 170, 13, 32.
        Assert.Equal(new[] { seams[1], seams[0], seams[2], seams[5] }, found.Select(d => d.Centre));
        Assert.DoesNotContain(found, d => d.Centre == seams[4]);
    }

    [Fact]
    public void EqualHistoricalCutoffIsRejectedAndEarlierEqualDistancesKeepOrder()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        TilePos[] seams = [new(13, 13), new(15, 13), new(17, 13), new(2, 16), new(28, 16)];
        foreach (TilePos seam in seams) KeeperWorld.Set(world, db, seam, KeeperWorld.Gold);

        List<GoldDeposit> found = GoldScanner.Scan(new KeeperView(db, world, KeeperWorld.Keeper));

        Assert.Equal(new[] { seams[1], seams[0], seams[2], seams[3] }, found.Select(d => d.Centre));
    }

    [Theory]
    [InlineData(false, 11, 10)]
    [InlineData(true, 11, 11)]
    public void ImpenetrableCentroidSearchesEastThenSouthAndCanChooseRockWithoutGold(
        bool blockEast, int expectedX, int expectedY)
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        for (int y = 8; y <= 12; y++)
            for (int x = 8; x <= 12; x++)
                if (x is 8 or 12 || y is 8 or 12)
                    KeeperWorld.Set(world, db, new TilePos(x, y), KeeperWorld.Gold);
        KeeperWorld.Set(world, db, new TilePos(10, 10), KeeperWorld.Impenetrable);
        if (blockEast) KeeperWorld.Set(world, db, new TilePos(11, 10), KeeperWorld.Impenetrable);

        GoldDeposit found = Assert.Single(GoldScanner.Scan(new KeeperView(db, world, KeeperWorld.Keeper)));

        Assert.Equal(new TilePos(expectedX, expectedY), found.Centre);
        Assert.Equal(KeeperWorld.Rock, world.Tile(found.Centre).TerrainId);
        Assert.Equal(16 * 1024, found.GoldAmount);
    }

    [Fact]
    public void GoldValueIncludesNonsolidTerrainAndCentroidIsNotWeightedByAmount()
    {
        GameDatabase basis = KeeperWorld.Database();
        var db = new GameDatabase(basis.Terrain.Values.Select(t => t.TerrainId switch
        {
            KeeperWorld.Gold => t with { Flags = t.Flags & ~TerrainFlags.Solid },
            KeeperWorld.DirtPath => t with { GoldValue = 10 },
            _ => t,
        }), basis.Rooms.Values, [], basis.Variables);
        WorldState world = KeeperWorld.World(db);
        KeeperWorld.Set(world, db, new TilePos(4, 4), KeeperWorld.Gold);
        KeeperWorld.Set(world, db, new TilePos(5, 4), KeeperWorld.DirtPath);
        KeeperWorld.Set(world, db, new TilePos(6, 4), KeeperWorld.DirtPath);

        GoldDeposit found = Assert.Single(GoldScanner.Scan(new KeeperView(db, world, KeeperWorld.Keeper)));

        Assert.Equal(new TilePos(5, 4), found.Centre);
        Assert.Equal(1044, found.GoldAmount);
    }
}
