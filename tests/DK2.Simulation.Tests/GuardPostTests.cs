// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Guard Post (guard-room.md §4, traps.md §3 step 5): the trap's room <c>+0x113</c>,
/// a patrol's visit at its deadline (<c>0x00463190</c>, state 100) and the post's report to its room.</summary>
public sealed class GuardPostTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;
    private const int TravelToPoint = 6;

    // A 3x3 Guard Room at (5..7, 3..5) on the keeper's claimed path (1..12, 1..8), as GuardTests.
    private static (GameDatabase, WorldState) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 8; y++)
            for (int x = 1; x <= 12; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        for (int y = 3; y <= 5; y++)
            for (int x = 5; x <= 7; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.GuardRoom), Keeper);
        return (db, world);
    }

    private static TrapState Post(GameDatabase db, WorldState world, int x, int y)
    {
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = x, Y = y, TrapId = Dk2TerrainFixture.GuardPostTrapId, PlayerId = Keeper, NumberOfShots = 1 }]);
        return world.Traps[^1];
    }

    /// <summary>A guard of the room, holding its place there as the category's enter hook would.</summary>
    private static CreatureState Guard(GameDatabase db, WorldState world, TilePos at)
    {
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.CurrentJob = JobType.Guard;
        StatePlacement.As(goblin, CreatureActivity.AtWork);
        goblin.HeldPlace = new RoomPlace((4 * world.Width) + 6, 1);
        return goblin;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink, params ISimulationSystem[] more)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureMovementSystem(), new CreatureJobSystem(), .. more]);
        sim.ObserveWith(sink);
        return sim;
    }

    [Fact]
    public void APostsRoomIsTheOwnersGuardRoomWhoseFirstSlabIsNearest_NotTheRoomItStandsIn()
    {
        // A second Guard Room at (10..11, 6..7): the post at (9,6) stands in neither; its first
        // slab (10,6) is one tile away, the first room's (5,3) five.
        (GameDatabase db, WorldState world) = World();
        for (int y = 6; y <= 7; y++)
            for (int x = 10; x <= 11; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.GuardRoom), Keeper);
        TrapState near = Post(db, world, 9, 6);
        TrapState inside = Post(db, world, 7, 3);

        Assert.Equal((6 * world.Width) + 10, near.GuardRoomTile);
        Assert.Equal((3 * world.Width) + 5, inside.GuardRoomTile);
    }

    [Fact]
    public void APostWithNoGuardRoomOfItsOwnerHasNone_AndMatchesOnlyAGuardHoldingNoPlace()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        world.Tile(4, 4).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(4, 4).OwnerPlayerId = Keeper;
        TrapState post = Post(db, world, 4, 4);

        Assert.Equal(-1, post.GuardRoomTile);
        Assert.True(TrapRules.SameRoom(world, db, post, RoomPlace.None));
        Assert.False(TrapRules.SameRoom(world, db, post, new RoomPlace(0, 1)));
    }

    [Fact]
    public void PastItsDeadlineAGuardWalksToItsRoomsPostResuming100_ThenBackToARandomRoomTileResuming98_KeepingItsPlace()
    {
        // The post at (9,4) stands outside the room; 0x00463190 still sends the guard there.
        (GameDatabase db, WorldState world) = World();
        TrapState post = Post(db, world, 9, 4);
        CreatureState goblin = Guard(db, world, new TilePos(6, 4));
        int place = goblin.HeldPlace.TileIndex;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        var seen = StateTrace.Of(sim, goblin, 400, () => sink.Events.Any(e => e.Kind == "guard_from_guard_post"));

        Assert.Contains(sink.Events, e => e.Kind == "guard_to_guard_post" && e.Detail.Contains($"trap={post.Id} at=(9,4)"));
        Assert.Contains((TravelToPoint, GuardRules.PatrolState, GuardRules.AtThePostState), seen);
        Assert.Contains(sink.Events, e => e.Kind == "guard_from_guard_post");
        Assert.Equal((TravelToPoint, GuardRules.PatrolState), (goblin.Dk2StateId, goblin.UtilityNewState));
        // State 100's deadline: the turn + 50 + random(v152).
        long arrived = sink.Events.Last(e => e.Kind == "guard_from_guard_post").Tick.Value;
        Assert.InRange(goblin.GuardDeadlineTick, arrived + 50, arrived + 50 + 59);
        // 98, 6 and 100 are all category 11 or a walk between them: no leave hook runs, the place stays.
        Assert.Equal(place, goblin.HeldPlace.TileIndex);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void WithNoGuardPostThePatrolsDeadlineResetsByTheImpIdleDelay()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = Guard(db, world, new TilePos(6, 4));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        StateTrace.Of(sim, goblin, 400, () => sink.Events.Count(e => e.Kind == "guard_leg") >= 1);
        goblin.GuardDeadlineTick = 0;

        StateTrace.Of(sim, goblin, 400, () => goblin.GuardDeadlineTick > 0);

        Assert.InRange(goblin.GuardDeadlineTick - sim.CurrentTick.Value, 74, 75 + 5);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "guard_to_guard_post");
    }

    [Fact]
    public void APostReportsAnEnemyInReachToItsRoom_AGuardHoldingAPlaceThereRunsToItInStateSixResuming254()
    {
        // The enemy at (12,6) is in reach (7 tiles) of the post at (9,4) and farther than five
        // tiles from the guard, so the keeper's own strength there is 0 and the deficit is the
        // enemy's Threat. The post reports on the turns whose sum with its id has bit 3 set.
        (GameDatabase db, WorldState world) = World();
        TrapState post = Post(db, world, 9, 4);
        CreatureState goblin = Guard(db, world, new TilePos(6, 4));
        CreatureState enemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(12, 6));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink, new TrapSystem());

        sim.Run(17);

        var report = Assert.Single(sink.Events, e => e.Kind == "guard_post_reported");
        Assert.Contains($"trap={post.Id} player={Keeper} enemy={enemy.Id} sent=1", report.Detail);
        Assert.NotEqual(0, (report.Tick.Value + post.Id) & 8);
        Assert.True(goblin.Running);
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Equal(CreatureActivity.Reporting, goblin.Activity);
    }

    [Fact]
    public void APostDoesNotReportAnEnemyItsOwnersStrengthThereOutweighs()
    {
        // Two keeper goblins beside the enemy: the deficit, Threat - 3/4 of twice Threat, is negative.
        (GameDatabase db, WorldState world) = World();
        Post(db, world, 9, 4);
        CreatureState goblin = Guard(db, world, new TilePos(6, 4));
        world.SpawnCreature(db, GoblinId, Rival, new TilePos(12, 6));
        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(12, 7));
        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 7));
        var sink = new ListEventSink();

        Simulation(db, world, sink, new TrapSystem()).Run(17);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "guard_post_reported");
        Assert.Equal(JobType.Guard, goblin.CurrentJob);
    }

    [Fact]
    public void ThePostsRoomSurvivesASnapshotRoundTrip()
    {
        (GameDatabase db, WorldState world) = World();
        TrapState post = Post(db, world, 9, 4);
        Guard(db, world, new TilePos(6, 4));
        var sim = Simulation(db, world, new ListEventSink(), new TrapSystem());
        sim.Run(3);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new CreatureMovementSystem(), new CreatureJobSystem(), new TrapSystem()]);

        Assert.Equal(post.GuardRoomTile, copy.World.TrapById(post.Id)!.GuardRoomTile);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        for (int i = 0; i < 20; i++) { sim.Tick(); copy.Tick(); }
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }
}
