// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>An imp's tile work on DK2's own state ids (ADR-0181, <c>imp-state-exits.md</c>).
/// State 10 and the entry states last less than a tick, so each trace records the saved caller
/// and the resume slot beside the state: they are the evidence the short states ran.</summary>
public class ImpStateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int NoState = CreatureStateMachine.NoState;
    private const int Idle = CreatureStateMachine.Idle;
    private const int Walk = 6;
    private const int Select = ImpStates.TaskSelection;

    private static (GameSimulation Sim, ListEventSink Events) Start(TilePos impAt)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);

        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static List<(int State, int Saved, int Resume)> StatesSeen(
        GameSimulation sim, int ticks, params PlayerCommand[] first)
    {
        CreatureState imp = sim.World.Creatures[0];
        var seen = new List<(int, int, int)>();
        (int, int, int) last = (imp.Dk2StateId, imp.StateBeforeUtilityState, imp.UtilityNewState);
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick(t == 0 && first.Length > 0 ? InputFrame.Of(first) : InputFrame.Empty);
            (int, int, int) now = (imp.Dk2StateId, imp.StateBeforeUtilityState, imp.UtilityNewState);
            if (now == last) continue;
            seen.Add(now);
            last = now;
        }

        return seen;
    }

    [Fact]
    public void AnImpDigsInFourteenClaimsInSixteenAndReinforcesInTwentyTwo()
    {
        // 10 walks in 6 resuming in 13; 13 moves to 14, which swings until the rock is dug
        // and sets 10. The claim of the tile it opened is 15 into 16 the same way. The wall
        // beside it needs no walk, so 6 and 21 fall inside one tick and only 22 is seen.
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));

        Assert.Equal(
            [(Walk, Select, 13), (14, Select, Idle), (NoState, Select, Idle),
             (Walk, Select, 15), (16, Select, Idle), (NoState, Select, Idle),
             (22, Select, Idle)],
            StatesSeen(sim, SimulationTiming.TicksFromSeconds(6),
                TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AnImpKeepsFourteenAcrossEverySwingUntilTheRockIsGone()
    {
        // The even state repeats its action in place (Q420): no 10 between the swings.
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        CreatureState imp = sim.World.Creatures[0];
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));

        var states = new List<int>();
        while (!events.Events.Any(e => e.Kind == "destroyed"))
        {
            sim.Tick(InputFrame.Empty);
            states.Add(imp.Dk2StateId);
        }

        int first = states.IndexOf(14);
        Assert.True(first >= 0);
        Assert.All(states.Skip(first), s => Assert.Equal(14, s));
        Assert.True(states.Count - first > SimulationTiming.WorkApplicationPeriodTicks);
    }

    [Fact]
    public void UnmarkingTheRockMidWalkEndsTheWalkThroughThirteenAndTen()
    {
        // The walk ends in 13, which finds the task gone and sets 10.
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        CreatureState imp = sim.World.Creatures[0];

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));
        Assert.Equal((Walk, 13), (imp.Dk2StateId, imp.UtilityNewState));

        sim.Tick(InputFrame.Of(TagTilesCommand.Untag(Keeper, new TilePos(4, 2))));

        Assert.Single(events.Events, e => e.Kind == "job_abandoned");
        Assert.Equal(NoState, imp.Dk2StateId);
        Assert.Equal((Select, Idle), (imp.StateBeforeUtilityState, imp.UtilityNewState));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    private static void Store(GameSimulation sim, TilePos at, RoomTypeId type)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        ref TileState tile = ref sim.World.Tile(at);
        tile.TerrainId = db.Room(type).TerrainId;
        tile.Health = db.TerrainById(tile.TerrainId).StartingHealth;
        tile.OwnerPlayerId = Keeper;
    }

    private static void OpenTheSeam(GameSimulation sim)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        foreach (TilePos p in new TilePos[] { new(4, 3), new(4, 4), new(4, 5) })
        {
            ref TileState tile = ref sim.World.Tile(p);
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.Health = db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }
    }

    [Fact]
    public void FloorGoldIsPickedUpInTwentySevenAndPutDownThroughTwentyEightAndTwentyNine()
    {
        // 10 walks in 6 to 27; 27 takes the pile and walks to the store in 6 resuming in 28,
        // so the saved caller turns from 10 to 27. 28 moves to 29, which deposits and sets 10.
        GameDatabase db = Dk2TerrainFixture.Database();
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(1, 1));
        Store(sim, new TilePos(3, 3), RoomTypeId.Treasury);
        FloorGold.Drop(sim.World, db, (2 * sim.World.Width) + 2, 900);

        Assert.Equal(
            [(Walk, Select, ImpStates.PickingUpGold),
             (Walk, ImpStates.PickingUpGold, ImpStates.AtTheStore),
             (NoState, ImpStates.PickingUpGold, Idle)],
            StatesSeen(sim, 100).Take(3));
        Assert.Contains(events.Events, e => e.Kind == "gold_delivered" && e.Detail.Contains("amount=900"));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AFullMinerWalksFromTwentySixAndSpillsTheRestThroughTen()
    {
        // 26 calls the store chooser before the swing that would not fit. The first heart
        // tile takes 1000 of 1363; 29 sets 10 and 10 walks on with the rest, so the second
        // walk's saved caller is 10.
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(4, 4));
        OpenTheSeam(sim);
        Store(sim, new TilePos(3, 3), RoomTypeId.DungeonHeart);
        Store(sim, new TilePos(3, 2), RoomTypeId.DungeonHeart);

        Assert.Equal(
            [(Walk, Select, 25), (26, Select, Idle),
             (Walk, 26, ImpStates.AtTheStore), (Walk, Select, ImpStates.AtTheStore),
             (NoState, Select, Idle)],
            StatesSeen(sim, 30, TagTilesCommand.Tag(Keeper, new TilePos(5, 5))).Take(5));
        Assert.Equal(1000, sim.World.Tile(3, 3).GoldHeld);
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AnImpMinesInTwentySix()
    {
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        OpenTheSeam(sim);

        List<(int State, int Saved, int Resume)> seen = StatesSeen(
            sim, SimulationTiming.TicksFromSeconds(4), TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));

        Assert.Equal([(Walk, Select, 25), (26, Select, Idle)], seen.Take(2));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }
}
