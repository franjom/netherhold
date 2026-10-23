// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Q621 (owner, 2026-10-23; ADR-0533): tagging a wall an Imp is reinforcing stops the reinforce
/// and the Imp digs the wall out. The tag's pool raise of kind 1 (<c>0x00507BB0</c>) removes the
/// tile's kind-6 entry; state 22 finds it gone once its action ends (<c>0x004663C4</c>) and goes
/// to 10, which takes the dig. The wall keeps the health the reinforce gave it.
/// </summary>
public class DigMarkCancelsReinforceTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static (GameSimulation Sim, ListEventSink Events) StartReinforcing()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        var at = new TilePos(8, 5);
        ref TileState floor = ref world.Tile(at);
        floor.TerrainId = Dk2TerrainFixture.ClaimedPath;
        floor.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        floor.OwnerPlayerId = Keeper;
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, at);

        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    /// <summary>Runs until the Imp is in state 22 on a wall it has already struck once.</summary>
    private static (TilePos Wall, int Health) RunUntilReinforcing(GameSimulation sim)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        int work = ImpStates.For(TileWorkKind.Reinforce).Work;
        for (int i = 0; i < SimulationTiming.TicksFromSeconds(10); i++)
        {
            sim.Tick(InputFrame.Empty);
            CreatureState imp = sim.World.Creatures[0];
            if (imp.Dk2StateId != work || imp.TargetWork != TileWorkKind.Reinforce) continue;
            var wall = new TilePos(imp.TargetTileIndex % sim.World.Width, imp.TargetTileIndex / sim.World.Width);
            ref TileState tile = ref sim.World.Tile(wall);
            if (tile.Health > database.TerrainById(tile.TerrainId).StartingHealth) return (wall, tile.Health);
        }
        throw new InvalidOperationException("the Imp never struck a wall to reinforce it");
    }

    [Fact]
    public void TaggingTheWallBeingReinforced_EndsTheReinforce_AndTheImpDigsIt()
    {
        (GameSimulation sim, ListEventSink events) = StartReinforcing();
        (TilePos wall, int reinforced) = RunUntilReinforcing(sim);
        byte rock = sim.World.Tile(wall).TerrainId;

        sim.Tick(InputFrame.Of(new TagTilesCommand(Keeper, [wall], true)));

        // State 22 holds while the running action plays, then finds its task gone.
        int work = ImpStates.For(TileWorkKind.Reinforce).Work;
        int limit = SimulationTiming.TicksFromSeconds(2);
        int waited = 0;
        while (sim.World.Creatures[0].Dk2StateId == work && waited++ < limit) sim.Tick(InputFrame.Empty);
        Assert.NotEqual(work, sim.World.Creatures[0].Dk2StateId);
        Assert.Contains(events.Events, e => e.Kind == "job_abandoned"
            && e.Detail.Contains("Reinforce") && e.Detail.Contains("reason=gone"));
        // The partial reinforce is not reset: no reinforce strike undoes itself.
        Assert.True(sim.World.Tile(wall).Health >= reinforced);
        Assert.Equal(rock, sim.World.Tile(wall).TerrainId);

        for (int i = 0; i < SimulationTiming.TicksFromSeconds(30) && sim.World.Tile(wall).TerrainId == rock; i++)
            sim.Tick(InputFrame.Empty);

        string at = $"({wall.X},{wall.Y})";
        Assert.Contains(events.Events, e => e.Kind == "job_taken"
            && e.Detail.Contains($"{at} Dig"));
        Assert.NotEqual(rock, sim.World.Tile(wall).TerrainId);
    }

    /// <summary>The control: untagged, the same Imp is still reinforcing the wall after the same wait.</summary>
    [Fact]
    public void AnUntaggedWall_KeepsBeingReinforced()
    {
        (GameSimulation sim, ListEventSink events) = StartReinforcing();
        (TilePos wall, int reinforced) = RunUntilReinforcing(sim);

        for (int i = 0; i < SimulationTiming.TicksFromSeconds(2); i++) sim.Tick(InputFrame.Empty);

        CreatureState imp = sim.World.Creatures[0];
        Assert.Equal(TileWorkKind.Reinforce, imp.TargetWork);
        Assert.Equal(wall.Y * sim.World.Width + wall.X, imp.TargetTileIndex);
        Assert.True(sim.World.Tile(wall).Health > reinforced);
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_abandoned");
    }

    /// <summary>Each keeper has its own task pool (player <c>+0x1105</c>): a rival's tag raises kind 1
    /// in the rival's pool and leaves this Imp's reinforce entry standing.</summary>
    [Fact]
    public void ARivalsTag_LeavesTheReinforceAlone()
    {
        (GameSimulation sim, ListEventSink events) = StartReinforcing();
        (TilePos wall, int reinforced) = RunUntilReinforcing(sim);
        ref TileState tile = ref sim.World.Tile(wall);
        tile.TaggedBy = tile.TaggedBy.With(new PlayerId(Rival));

        for (int i = 0; i < SimulationTiming.TicksFromSeconds(2); i++) sim.Tick(InputFrame.Empty);

        CreatureState imp = sim.World.Creatures[0];
        Assert.Equal(TileWorkKind.Reinforce, imp.TargetWork);
        Assert.True(sim.World.Tile(wall).Health > reinforced);
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_abandoned");
    }

    /// <summary>Untagging removes the kind-1 entry (0x00512A20), so the reinforce handler may raise
    /// kind 6 for the wall again (0x0046B05F pushes 6 into TaskPool_Raise 0x00507BB0, which refused it
    /// only while kind 1 stood): the reinforce resumes. Owner, from DK2 play: it does.</summary>
    [Fact]
    public void UntaggingTheWall_LetsTheReinforceResume()
    {
        (GameSimulation sim, ListEventSink events) = StartReinforcing();
        (TilePos wall, _) = RunUntilReinforcing(sim);
        byte rock = sim.World.Tile(wall).TerrainId;

        sim.Tick(InputFrame.Of(new TagTilesCommand(Keeper, [wall], true)));
        sim.Tick(InputFrame.Of(new TagTilesCommand(Keeper, [wall], false)));
        int index = wall.Y * sim.World.Width + wall.X;
        bool resumed = false;
        for (int i = 0; i < SimulationTiming.TicksFromSeconds(10) && !resumed; i++)
        {
            sim.Tick(InputFrame.Empty);
            CreatureState imp = sim.World.Creatures[0];
            resumed = imp.TargetWork == TileWorkKind.Reinforce && imp.TargetTileIndex == index
                && imp.Dk2StateId == ImpStates.For(TileWorkKind.Reinforce).Work;
        }

        Assert.True(resumed);
        Assert.Equal(rock, sim.World.Tile(wall).TerrainId);
    }
}
