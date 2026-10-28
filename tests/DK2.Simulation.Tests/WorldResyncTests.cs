// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary><see cref="WorldResync"/>: a capture's tick copied over our world between ticks. The
/// state here is written by hand in <c>capture_state.py</c>'s shape; what the fields mean is that
/// script's and <c>ResyncState</c>'s to say, not this test's.</summary>
public class WorldResyncTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static GameSimulation Start(int imps)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int i = 0; i < imps; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        return new GameSimulation(database, world, new SimRandom(new Dk2Lcg(7)), Dk2TerrainFixture.DefaultSystems());
    }

    /// <summary>Our world's tiles in the capture's three-ints-a-tile shape.</summary>
    private static int[] TilesOf(WorldState world)
    {
        var tiles = new int[3 * world.Width * world.Height];
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            int i = 3 * ((y * world.Width) + x);
            tiles[i] = tile.TerrainId;
            tiles[i + 1] = tile.OwnerPlayerId;
        }
        return tiles;
    }

    private static ResyncCreature Imp(int slot, TilePos at, int state, ResyncJob? job = null) =>
        new(slot, Keeper, CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y), Dk2TerrainFixture.ImpCreatureId, state, 1000, job ?? Job(0));

    private static ResyncJob Job(int resume, int taskX = 0, int taskY = 0, int side = 0, int slot = 0, int lastKind = 0, int gold = 0) =>
        new(resume, taskX, taskY, 1, side, slot, lastKind, 0, 0, gold);

    private static ResyncState State(WorldState world, ResyncCreature[] creatures, ResyncObject[]? objects = null, uint seed = 99) =>
        new(10, world.Width, world.Height, [new ResyncPlayer(Keeper, 1234, 56)], TilesOf(world), null, creatures, objects, seed);

    [Fact]
    public void TilesTheSeedAndAWorkersPoseComeFromTheCapture()
    {
        GameSimulation sim = Start(1);
        WorldState world = sim.World;
        CreatureState imp = world.Creatures[0];
        int[] tiles = TilesOf(world);
        int i = 3 * ((4 * world.Width) + 2);
        tiles[i] = Dk2TerrainFixture.DirtPath;   // (2,4), rock in the level, dug in the capture
        tiles[i + 1] = 0;
        var state = new ResyncState(10, world.Width, world.Height, [new ResyncPlayer(Keeper, 1234, 56)], tiles, null,
            [Imp(401, new TilePos(3, 3), 10, Job(0, gold: 545, lastKind: 2, taskX: 5, taskY: 5))], null, 4242);

        ResyncReport report = WorldResync.Apply(sim, state, slot => slot == 401 ? imp.Id : null);

        Assert.Equal(1, report.TilesChanged);
        Assert.Equal(Dk2TerrainFixture.DirtPath, world.Tile(2, 4).TerrainId);
        Assert.Equal(new TilePos(3, 3), imp.Tile);
        Assert.Equal(545, imp.GoldHeld);
        Assert.Equal(ImpTaskKind.Mine, imp.LastTaskKind);
        Assert.Equal((5 * world.Width) + 5, imp.LastTaskTileIndex);
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
        Assert.Equal(4242u, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal(56, world.Player(Keeper).Mana);
        Assert.Equal(0, report.Unmapped);
    }

    [Fact]
    public void AWorkerWalkingToAMineInTheCaptureWalksToTheSameFaceAndSlot()
    {
        GameSimulation sim = Start(1);
        WorldState world = sim.World;
        CreatureState imp = world.Creatures[0];
        // The level's seam at (5,5); (4,5) dug and claimed so the west face is a stance.
        int[] tiles = TilesOf(world);
        int west = 3 * ((5 * world.Width) + 4);
        tiles[west] = Dk2TerrainFixture.ClaimedPath;
        tiles[west + 1] = Keeper;
        for (int x = 3; x <= 4; x++) { int j = 3 * ((4 * world.Width) + x); tiles[j] = Dk2TerrainFixture.ClaimedPath; tiles[j + 1] = Keeper; }
        world.Tile(5, 5).TaggedBy = world.Tile(5, 5).TaggedBy.With(Keeper);
        var state = new ResyncState(10, world.Width, world.Height, [new ResyncPlayer(Keeper, 0, 0)], tiles, null,
            [Imp(401, new TilePos(2, 2), 6, Job(25, taskX: 5, taskY: 5, side: 3, slot: 1, lastKind: 2))], null, 1);

        ResyncReport report = WorldResync.Apply(sim, state, slot => slot == 401 ? imp.Id : null);

        Assert.Equal(1, report.JobsReissued);
        Assert.Equal(TileWorkKind.MineGold, imp.TargetWork);
        Assert.Equal((5 * world.Width) + 5, imp.TargetTileIndex);
        Assert.Equal(1, imp.WorkSlot);
        Assert.Equal(6, imp.Dk2StateId);   // DK2's walk to a point, resuming in 25
        Assert.Equal(25, imp.UtilityNewState);
        Assert.Equal(ImpTaskKind.Mine, imp.LastTaskKind);
    }

    [Fact]
    public void ADeliveryIsSetChoosingAndAnUnmappedSlotIsReported()
    {
        GameSimulation sim = Start(1);
        WorldState world = sim.World;
        CreatureState imp = world.Creatures[0];
        ResyncState state = State(world, [
            Imp(401, new TilePos(1, 1), 6, Job(28, gold: 1092, lastKind: 2, taskX: 5, taskY: 5)),
            Imp(402, new TilePos(1, 2), 10),
        ]);

        ResyncReport report = WorldResync.Apply(sim, state, slot => slot == 401 ? imp.Id : null);

        Assert.Equal(1, report.WorkersSetChoosing);
        Assert.Equal(0, report.JobsReissued);
        Assert.Equal(1, report.Unmapped);
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
        Assert.Equal(1092, imp.GoldHeld);
        Assert.Contains(report.Notes, n => n.Contains("28x1"));
        Assert.Contains(report.Notes, n => n.Contains("slot 402"));
    }

    [Fact]
    public void MarksAreTheCapturesInItsOrderNewestLast()
    {
        GameSimulation sim = Start(1);
        WorldState world = sim.World;
        world.Tile(5, 5).TaggedBy = world.Tile(5, 5).TaggedBy.With(Keeper);   // ours, gone in DK2
        var state = new ResyncState(10, world.Width, world.Height, [new ResyncPlayer(Keeper, 0, 0)], TilesOf(world), null,
            [Imp(401, new TilePos(2, 2), 10)], null, 1,
            [new ResyncMark(6, 5, Keeper, 9), new ResyncMark(5, 6, Keeper, 4)]);

        ResyncReport report = WorldResync.Apply(sim, state, slot => slot == 401 ? world.Creatures[0].Id : null);

        Assert.Equal(2, report.MarksSet);
        Assert.False(world.Tile(5, 5).IsTaggedBy(Keeper));
        Assert.True(world.Tile(6, 5).IsTaggedBy(Keeper));
        Assert.True(world.Tile(5, 6).IsTaggedBy(Keeper));
        Assert.True(world.Tile(6, 5).TagOrder > world.Tile(5, 6).TagOrder);   // marked later, so newer
    }

    [Fact]
    public void FloorGoldIsTheCapturesNotOurs()
    {
        GameSimulation sim = Start(1);
        WorldState world = sim.World;
        GameDatabase database = sim.Database;
        FloorGold.Drop(world, database, (2 * world.Width) + 1, 300);
        int at = (3 * world.Width) + 3;
        ResyncState state = State(world, [Imp(401, new TilePos(2, 2), 10)],
            [new ResyncObject(900, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, CreatureState.CentreOf(3), CreatureState.CentreOf(3), 700, 0)]);

        ResyncReport report = WorldResync.Apply(sim, state, slot => slot == 401 ? world.Creatures[0].Id : null);

        Assert.Equal(1, report.PilesReplaced);
        Assert.Equal(0, FloorGold.OnTile(world, database, (2 * world.Width) + 1));
        Assert.Equal(700, FloorGold.OnTile(world, database, at));
    }
}
