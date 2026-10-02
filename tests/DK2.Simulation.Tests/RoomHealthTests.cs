// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>A room's one health word, DK2's room <c>+0x35</c>, and what moves it: the applier
/// <c>0x004E5300</c>, a tile added (<c>0x004EC650</c>), a merge (<c>0x004ECB70</c>), a tile
/// taken out (<c>0x004ED440</c>) and the room tick's <c>HealthGain</c> (<c>0x004E4B00</c>).
/// tile-work.md "Room health", Q500.</summary>
public class RoomHealthTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;

    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static int PerTile => RoomHealthRules.StartPerTile(Db, Db.Room(RoomTypeId.Treasury));

    private static WorldState World() => WorldState.FromMap(
        Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    /// <summary>Lays a treasury tile down the way a build does: terrain first, then the chain.</summary>
    private static void Build(WorldState world, int x, int y, byte owner)
    {
        ref TileState tile = ref world.Tile(x, y);
        byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
        RoomRules.Place(Db, ref tile, Db.Room(RoomTypeId.Treasury), owner);
        SlabChain.Changed(world, Db, (y * world.Width) + x, terrainBefore, ownerBefore);
    }

    /// <summary>Takes a tile out of its room the way a sale or a destroyed tile does.</summary>
    private static void Unmake(WorldState world, int x, int y)
    {
        ref TileState tile = ref world.Tile(x, y);
        byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.Health = Db.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        SlabChain.Changed(world, Db, (y * world.Width) + x, terrainBefore, ownerBefore);
    }

    private static RoomInstance RoomAt(WorldState world, int x, int y) =>
        RoomIndex.RoomAt(world, Db, (y * world.Width) + x)
        ?? throw new InvalidOperationException($"no room at ({x},{y})");

    private static int Health(WorldState world, int x, int y) =>
        RoomHealthRules.Health(world, Db, RoomAt(world, x, y));

    [Fact]
    public void ARoomStartsAtStartingHealthTimesItsSize_HoweverItWasLaidDown()
    {
        WorldState world = World();
        for (int x = 5; x <= 7; x++) Build(world, x, 5, Keeper);

        // Painted straight onto the tiles, as a map load or a test does, with no chain call.
        for (int x = 5; x <= 6; x++) RoomRules.Place(Db, ref world.Tile(x, 9), Db.Room(RoomTypeId.Treasury), Rival);

        Assert.Equal(3 * PerTile, Health(world, 6, 5));
        Assert.Equal(2 * PerTile, Health(world, 5, 9));
    }

    [Fact]
    public void APositiveDeltaClampsAtMaxHealthTimesSize()
    {
        WorldState world = World();
        for (int x = 5; x <= 6; x++) Build(world, x, 5, Keeper);
        RoomInstance room = RoomAt(world, 5, 5);
        RoomHealthRules.Set(world, Db, room, 100);

        RoomHealthRules.Apply(world, Db, room, 1000, Keeper);
        Assert.Equal(1100, Health(world, 5, 5));

        RoomHealthRules.Apply(world, Db, room, 1_000_000, Keeper);
        Assert.Equal(RoomHealthRules.Cap(Db, room), Health(world, 5, 5));
    }

    [Fact]
    public void ANegativeDeltaThatLeavesHealthTakesTheRoomWholeAndResetsIt()
    {
        WorldState world = World();
        for (int x = 5; x <= 7; x++) Build(world, x, 5, Rival);
        RoomInstance room = RoomAt(world, 5, 5);

        Assert.Equal(RoomHealthOutcome.Progressed, RoomHealthRules.Apply(world, Db, room, -(3 * PerTile) + 1, Keeper));
        Assert.Equal(1, Health(world, 5, 5));
        Assert.Equal(Rival, world.Tile(7, 5).OwnerPlayerId);

        // Below 1, not at 0: the applier's test is "greater than zero".
        Assert.Equal(RoomHealthOutcome.TookOver, RoomHealthRules.Apply(world, Db, room, -1, Keeper));
        for (int x = 5; x <= 7; x++) Assert.Equal(Keeper, world.Tile(x, 5).OwnerPlayerId);
        Assert.Equal(3 * PerTile, Health(world, 5, 5));
    }

    [Fact]
    public void AnAddedTileBringsItsStartingHealth_AndADamagedRoomKeepsItsDamage()
    {
        WorldState world = World();
        for (int x = 5; x <= 6; x++) Build(world, x, 5, Keeper);
        RoomHealthRules.Set(world, Db, RoomAt(world, 5, 5), (2 * PerTile) - 5000);

        Build(world, 7, 5, Keeper);

        Assert.Equal((3 * PerTile) - 5000, Health(world, 7, 5));
    }

    [Fact]
    public void AMergeAddsTheOtherRoomsHealth()
    {
        WorldState world = World();
        Build(world, 5, 5, Keeper);
        Build(world, 7, 5, Keeper);
        RoomHealthRules.Set(world, Db, RoomAt(world, 5, 5), 1000);
        RoomHealthRules.Set(world, Db, RoomAt(world, 7, 5), 2000);

        // The tile between them joins the west room and the east one is absorbed (0x004ECB70).
        Build(world, 6, 5, Keeper);

        Assert.Equal(3, RoomAt(world, 5, 5).TileCount);
        Assert.Equal(1000 + PerTile + 2000, Health(world, 6, 5));
    }

    /// <summary>A tile taken out leaves every piece with the old room's share of its own cap,
    /// in whole percent, and at least 1 (<c>0x004ED440</c>).</summary>
    [Fact]
    public void ATileTakenOutLeavesEachPieceTheOldRoomsShare()
    {
        WorldState world = World();
        for (int x = 5; x <= 9; x++) Build(world, x, 5, Keeper);

        // 5 tiles at 37.4% of their cap: 37% after the 12.12 divide and × 100 >> 12.
        RoomHealthRules.Set(world, Db, RoomAt(world, 5, 5), (5 * PerTile * 374) / 1000);
        Unmake(world, 7, 5);

        Assert.Equal(2 * PerTile * 37 / 100, Health(world, 5, 5));
        Assert.Equal(2 * PerTile * 37 / 100, Health(world, 9, 5));
        Assert.Equal(0, world.Tile(7, 5).RoomHealthLost);
    }

    [Fact]
    public void AnUntouchedRoomCutInTwoLosesNothing()
    {
        WorldState world = World();
        for (int x = 5; x <= 7; x++) Build(world, x, 5, Keeper);

        Unmake(world, 6, 5);

        Assert.Equal(PerTile, Health(world, 5, 5));
        Assert.Equal(PerTile, Health(world, 7, 5));
        for (int x = 5; x <= 7; x++) Assert.Equal(0, world.Tile(x, 5).RoomHealthLost);
    }

    [Fact]
    public void APieceKeepsAtLeastOneHealth()
    {
        WorldState world = World();
        for (int x = 5; x <= 7; x++) Build(world, x, 5, Keeper);
        RoomHealthRules.Set(world, Db, RoomAt(world, 5, 5), 1);

        Unmake(world, 7, 5);

        Assert.Equal(1, Health(world, 5, 5));
    }

    [Fact]
    public void TheShareIsWholePercentThroughTheFixedPointDivide()
    {
        Assert.Equal(100, RoomHealthRules.SharePercent(30000, 30000));
        Assert.Equal(99, RoomHealthRules.SharePercent(29999, 30000));
        Assert.Equal(0, RoomHealthRules.SharePercent(1, 30000));
        Assert.Equal(1, RoomHealthRules.PieceHealth(10000, 2, 0));
        Assert.Equal(7400, RoomHealthRules.PieceHealth(10000, 2, 37));
    }

    /// <summary>The room tick's <c>HealthGain</c>: 0 in every shipped room, so a variant database
    /// gives the Treasury one to show the step runs and stops at the cap.</summary>
    [Fact]
    public void ARoomWithAHealthGainRegeneratesToItsCap()
    {
        var db = new GameDatabase(
            Db.Terrain.Values,
            Db.Rooms.Values.Select(r => r.RoomId == (byte)RoomTypeId.Treasury ? r with { HealthGain = 3000 } : r),
            Db.Creatures.Values,
            Db.Variables);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        RoomRules.Place(db, ref world.Tile(5, 5), db.Room(RoomTypeId.Treasury), Keeper);
        RoomInstance room = RoomIndex.RoomAt(world, db, (5 * world.Width) + 5)!;
        RoomHealthRules.Set(world, db, room, 2000);

        var sim = new GameSimulation(db, world, seed: 1, [new Systems.RoomHealthSystem()]);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(5000, RoomHealthRules.Health(world, db, room));

        for (int i = 0; i < 5; i++) sim.Tick(InputFrame.Empty);
        Assert.Equal(RoomHealthRules.Cap(db, room), RoomHealthRules.Health(world, db, room));
    }

    [Fact]
    public void ADamagedRoomSurvivesASnapshotRoundTrip()
    {
        WorldState world = World();
        for (int x = 5; x <= 7; x++) Build(world, x, 5, Keeper);
        RoomHealthRules.Set(world, Db, RoomAt(world, 5, 5), 12345);

        var sim = new GameSimulation(Db, world, seed: 1, []);
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), Db, []);

        Assert.Equal(12345, RoomHealthRules.Health(copy.World, Db, RoomAt(copy.World, 6, 5)));
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }
}
