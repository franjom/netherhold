// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Guard duty, states 97, 98 and 99 (ADR-0212, <c>guard-room.md</c>).</summary>
public sealed class GuardTests
{
    private const byte Keeper = 4;
    private const byte GoblinId = 7;

    // A 3x3 Guard Room at (5..7, 3..5): corners NW (5,3), NE (7,3), SE (7,5), SW (5,5).
    private static readonly TilePos Centre = new(6, 4);

    private static (GameDatabase, WorldState) World()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
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

    private static CreatureState Guard(GameDatabase db, WorldState world, TilePos at)
    {
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.CurrentJob = JobType.Guard;
        StatePlacement.As(goblin, CreatureActivity.AtWork);
        Assert.Equal(GuardRules.ToThePostState, goblin.Dk2StateId);
        return goblin;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    [Fact]
    public void EveryCornerOfARectangularRoomIsAPost()
    {
        // 0x00462BB0 returns the pair's facing on a hit (0x00462CD3) and 8 only for a miss, so the
        // ninth pair (mask 10, facing 0) is a post like the other corners (ADR-0780).
        (GameDatabase db, WorldState world) = World();
        int SlotOf(int x, int y, out int f) => GuardRules.Slot(world, db, x, y, Keeper, out f);

        Assert.Equal(5, SlotOf(7, 5, out int se));   // west and north in the room
        Assert.Equal(2, se);
        Assert.Equal(6, SlotOf(7, 3, out int ne));   // west and south
        Assert.Equal(1, ne);
        Assert.Equal(7, SlotOf(5, 5, out int sw));   // east and north
        Assert.Equal(3, sw);
        Assert.Equal(8, SlotOf(5, 3, out int nw));   // east and south: the ninth pair
        Assert.Equal(0, nw);
        Assert.Equal(new TilePos(5, 3), GuardRules.NearestPost(world, db, 5, 3, Keeper));
        Assert.Equal(GuardRules.NoSlot, SlotOf(6, 3, out _));   // an edge
        Assert.Equal(GuardRules.NoSlot, SlotOf(6, 4, out _));   // the middle
    }

    [Fact]
    public void TheSpiralFromTheMiddleFindsTheSouthEastCornerFirst()
    {
        // (6,4), (7,4) an edge, then (7,5): the second ring's first corner.
        (GameDatabase db, WorldState world) = World();
        Assert.Equal(new TilePos(7, 5), GuardRules.NearestPost(world, db, Centre.X, Centre.Y, Keeper));
    }

    [Fact]
    public void TheWallFollowGoesRoundTheEdgeOneStraightLegAtATime()
    {
        // From the south-east corner facing 2: west along the south wall, north, east, south.
        (GameDatabase db, WorldState world) = World();
        (TilePos at, int facing) = (new TilePos(7, 5), 2);
        var legs = new List<(TilePos, int)>();
        for (int i = 0; i < 4; i++)
        {
            (at, facing) = GuardRules.Leg(world, db, at.X, at.Y, facing, Keeper);
            legs.Add((at, facing));
        }

        Assert.Equal(
            [(new TilePos(5, 5), 3), (new TilePos(5, 3), 0), (new TilePos(7, 3), 1), (new TilePos(7, 5), 2)],
            legs);
    }

    [Fact]
    public void AGuardWalksToItsPostThenPatrolsTheEdgeInStateSixResumingNinetyEight()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = Guard(db, world, Centre);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        var seen = StateTrace.Of(sim, goblin, 40 * SimulationTiming.TicksPerSecond,
            () => sink.Events.Count(e => e.Kind == "guard_leg") >= 4);

        Assert.Contains(sink.Events, e => e.Kind == "guard_to_post" && e.Detail.Contains("post=(7,5) slot=5 facing=2"));
        Assert.Equal((6, GuardRules.ToThePostState, GuardRules.PatrolState), seen[0]);
        Assert.Equal(
            ["to=(5,5) facing=3", "to=(5,3) facing=0", "to=(7,3) facing=1", "to=(7,5) facing=2"],
            sink.Events.Where(e => e.Kind == "guard_leg").Take(4).Select(e => e.Detail[(e.Detail.IndexOf("to=", StringComparison.Ordinal))..]));
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AGuardWhoseNearestPostIsTheSouthWestCornerWalksToIt()
    {
        // Slot 7 faces 3, and the target is read by the facing (0x00462CD3, 0x00462AFB): (0xA00, 0x600)
        // on the post tile. Level 5 seed 1, turn 1922: native creature 432 at a slot-7 post (19,21)
        // routes to (0x13A00, 0x15600) and walks in state 6 resuming 98 (ADR-0780).
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = Guard(db, world, new TilePos(5, 5));
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(4);

        Assert.Contains(sink.Events, e => e.Kind == "guard_to_post" && e.Detail.Contains("slot=7 facing=3"));
        Assert.Equal(((5 << 12) | 0xA00, (5 << 12) | 0x600), GuardRules.PostPoint(new TilePos(5, 5), 3));
        Assert.NotEqual(CreatureActivity.Idle, goblin.Activity);
    }

    [Fact]
    public void AGuardOffItsGuardRoomIsIdle()
    {
        (GameDatabase db, WorldState world) = World();
        // East of the room, where the nearest post is a working corner, so only the gate idles it.
        CreatureState goblin = Guard(db, world, new TilePos(9, 4));
        var sink = new ListEventSink();

        Simulation(db, world, sink).Run(4);

        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "guard_to_post");
    }

    [Fact]
    public void TheSpiralTurnsDownThenWestThenUpThenEast()
    {
        // Two lone lair tiles, posts of mask 0: (5,5) is the spiral's fifth tile from (6,4),
        // (7,3) its ninth.
        GameDatabase db = Dk2TerrainFixture.Database();
        var empty = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        RoomRules.Place(db, ref empty.Tile(5, 5), db.Room(RoomTypeId.Lair), Keeper);
        RoomRules.Place(db, ref empty.Tile(7, 3), db.Room(RoomTypeId.Lair), Keeper);

        Assert.Equal(new TilePos(5, 5), GuardRules.NearestPost(empty, db, 6, 4, Keeper));
    }

    [Fact]
    public void ALegEndsOnTheQuarterAgainstItsWall()
    {
        Assert.Equal(((5 << 12) | 0x600, (5 << 12) | 0xA00), GuardRules.LegPoint(new TilePos(5, 5), 3));
        Assert.Equal(((7 << 12) | 0xA00, (3 << 12) | 0x600), GuardRules.LegPoint(new TilePos(7, 3), 1));
        // A post's target is read by the slot's facing (0x00462CD3): slot 5 faces 2.
        Assert.Equal(((7 << 12) | 0xA00, (5 << 12) | 0x600), GuardRules.PostPoint(new TilePos(7, 5), 2));
    }

    [Fact]
    public void AtAnInnerCornerTheWallOpensAndTheGuardTurnsIntoIt()
    {
        // An L: the 3x3 room plus (8,5) and (8,6) beside its south-east corner. South from the
        // north-east corner, (7,4)'s east neighbour is outside and (7,5)'s is inside: the wall
        // opens there, facing 5. East from (7,5), (8,5) is the last tile before the wall.
        (GameDatabase db, WorldState world) = World();
        RoomRules.Place(db, ref world.Tile(8, 5), db.Room(RoomTypeId.GuardRoom), Keeper);
        RoomRules.Place(db, ref world.Tile(8, 6), db.Room(RoomTypeId.GuardRoom), Keeper);

        Assert.Equal((new TilePos(7, 5), 5), GuardRules.Leg(world, db, 7, 3, 1, Keeper));
        Assert.Equal((new TilePos(8, 5), 1), GuardRules.Leg(world, db, 7, 5, 5, Keeper));

        // And east along the north wall, with (7,2) added above the north-east corner: facing 4.
        RoomRules.Place(db, ref world.Tile(7, 2), db.Room(RoomTypeId.GuardRoom), Keeper);
        Assert.Equal((new TilePos(7, 3), 4), GuardRules.Leg(world, db, 5, 3, 0, Keeper));
    }
}
