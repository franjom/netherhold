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
        int last = imp.Dk2StateId;
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick(t == 0 && first.Length > 0 ? InputFrame.Of(first) : InputFrame.Empty);
            if (imp.Dk2StateId == last) continue;
            seen.Add((imp.Dk2StateId, imp.StateBeforeUtilityState, imp.UtilityNewState));
            last = imp.Dk2StateId;
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

    [Fact]
    public void AnImpMinesInTwentySix()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        foreach (TilePos p in new TilePos[] { new(4, 3), new(4, 4), new(4, 5) })
        {
            ref TileState tile = ref sim.World.Tile(p);
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.Health = database.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }

        List<(int State, int Saved, int Resume)> seen = StatesSeen(
            sim, SimulationTiming.TicksFromSeconds(4), TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));

        Assert.Equal([(Walk, Select, 25), (26, Select, Idle)], seen.Take(2));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }
}
