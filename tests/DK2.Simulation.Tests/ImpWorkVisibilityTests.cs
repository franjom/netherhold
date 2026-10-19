// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class ImpWorkVisibilityTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Wall = new(4, 2);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADiggerWhoseActionEndsSeesAnEarlierWorkersStrike(bool strikerIsNewer)
    {
        (GameSimulation sim, _, CreatureState older, CreatureState newer) = Start();
        CreatureState striker = strikerIsNewer ? newer : older;
        CreatureState finisher = strikerIsNewer ? older : newer;
        Work(sim, striker, actionAgeTicks: 1);
        Work(sim, finisher, actionAgeTicks: WorkCadence.ActionTicks(TileWorkKind.Dig));

        sim.Tick();

        Assert.Equal(Dk2TerrainFixture.DirtPath, sim.World.Tile(Wall).TerrainId);
        Assert.Equal(strikerIsNewer ? ImpStates.TaskSelection : ImpStates.For(TileWorkKind.Dig).Work,
            finisher.Dk2StateId);
        Assert.Equal(strikerIsNewer, sim.World.Tile(Wall).FreshlyDug);
        Assert.Empty(sim.World.PendingWork);
    }

    [Fact]
    public void AWorkerChoosingAfterTheWallFallsRemovesItsObsoleteTaskInTheSameTurn()
    {
        (GameSimulation sim, ListEventSink events, CreatureState chooser, CreatureState striker) = Start();
        Work(sim, striker, actionAgeTicks: 1);
        chooser.Dk2StateId = ImpStates.TaskSelection;
        sim.World.Tile(1, 4).SetTaggedBy(Keeper, true);

        sim.Tick();

        Assert.Contains(events.Events, e => e.Kind == "dig_task_dropped"
            && e.Detail.Contains($"imp={chooser.Id} (4,2)"));
        Assert.True(sim.World.Tile(Wall).DigTaskOutlivesWallFor.IsEmpty);
        Assert.False(sim.World.Tile(Wall).FreshlyDug);
    }

    private static (GameSimulation Sim, ListEventSink Events, CreatureState Older, CreatureState Newer) Start()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Neutral, Keeper]);
        CreatureState older = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        CreatureState newer = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        world.Tile(Wall).SetTaggedBy(Keeper, true);
        var sim = new GameSimulation(database, world, 99, [new ImpTaskSystem(), new TileWorkSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events, older, newer);
    }

    private static void Work(GameSimulation sim, CreatureState imp, int actionAgeTicks)
    {
        imp.Dk2StateId = ImpStates.For(TileWorkKind.Dig).Work;
        imp.TargetTileIndex = Wall.Y * sim.World.Width + Wall.X;
        imp.TargetWork = TileWorkKind.Dig;
        imp.TimerTicks = 1;
        imp.StateEnteredTick = -actionAgeTicks;
    }
}
