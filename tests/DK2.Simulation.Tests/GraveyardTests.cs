// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class GraveyardTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos First = new(1, 1);

    [Fact]
    public void Confirmed_FiveGoblinCorpsesRaiseTheRoomsCreatedCreature()
    {
        (GameDatabase db, WorldState world, RoomInstance room) = Setup(First);
        int burial = Index(world, new TilePos(2, 1));

        GraveyardBurialResult result = default;
        for (int i = 0; i < 5; i++)
            result = GraveyardRules.Bury(world, db, room, 200, burial);

        CreatureState vampire = Assert.Single(
            world.Creatures, c => c.CreatureTypeId == room.Definition.CreatedCreatureId);
        Assert.Equal(vampire.Id, result.SpawnedCreatureId);
        Assert.Equal(0, Assert.Single(world.Graveyards).DecomposeValue);
    }

    [Fact]
    public void Confirmed_SeparateRoomsKeepSeparateDecomposeTallies()
    {
        TilePos second = new(3, 1);
        (GameDatabase db, WorldState world, RoomInstance first) = Setup(First, second);
        RoomInstance other = RoomIndex.RoomAt(world, db, Index(world, second))!;

        GraveyardRules.Bury(world, db, first, 600, Index(world, First));
        GraveyardRules.Bury(world, db, other, 400, Index(world, second));

        Assert.Equal([400, 600], world.Graveyards.Select(g => g.DecomposeValue).Order().ToArray());
        Assert.Empty(world.Creatures);
    }

    [Fact]
    public void OurDefaults_SubtractThresholdAndRaiseAtMostOneAtTheLastBurialTile()
    {
        (GameDatabase db, WorldState world, RoomInstance room) = Setup(First);
        int burial = Index(world, new TilePos(2, 1));

        GraveyardBurialResult result = GraveyardRules.Bury(world, db, room, 2200, burial);

        Assert.True(result.SpawnedCreatureId > 0);
        Assert.Equal(1200, result.DecomposeValue);
        Assert.Single(world.Creatures);
        Assert.Equal(new TilePos(2, 1), world.Creatures[0].Tile);
    }

    [Fact]
    public void OurAnchorPolicy_SaleClearsTheTallyAndRebuildStartsEmpty()
    {
        (GameDatabase db, WorldState world, RoomInstance room) = Setup(First);
        GraveyardRules.Bury(world, db, room, 600, Index(world, First));

        ref TileState tile = ref world.Tile(First);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.Health = db.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        GraveyardRules.Reconcile(world, db);
        Assert.Empty(world.Graveyards);

        Paint(db, world, First);
        RoomInstance rebuilt = RoomIndex.RoomAt(world, db, Index(world, First))!;
        GraveyardRules.Bury(world, db, rebuilt, 200, Index(world, First));
        Assert.Equal(200, Assert.Single(world.Graveyards).DecomposeValue);
    }

    [Fact]
    public void PersistentTally_SurvivesSnapshotAndChangesTheStateHash()
    {
        (GameDatabase db, WorldState world, RoomInstance room) = Setup(First);
        var sim = new GameSimulation(db, world, 42, [new GraveyardSystem()]);
        ulong before = sim.StateHash();
        GraveyardRules.Bury(world, db, room, 200, Index(world, First));

        Assert.NotEqual(before, sim.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), db, [new GraveyardSystem()]);

        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(200, Assert.Single(restored.World.Graveyards).DecomposeValue);
    }

    private static (GameDatabase Db, WorldState World, RoomInstance Room) Setup(
        params TilePos[] graveyards)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        foreach (TilePos tile in graveyards) Paint(db, world, tile);
        return (db, world, RoomIndex.RoomAt(world, db, Index(world, graveyards[0]))!);
    }

    private static void Paint(GameDatabase db, WorldState world, TilePos tile)
    {
        ref TileState state = ref world.Tile(tile);
        RoomRules.Place(db, ref state, db.Room(RoomTypeId.Graveyard), Keeper);
    }

    private static int Index(WorldState world, TilePos tile) => (tile.Y * world.Width) + tile.X;
}
