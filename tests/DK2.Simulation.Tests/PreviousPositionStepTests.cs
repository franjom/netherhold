// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The creature update's previous-position copy (<c>0x00489FE6</c>): taken before the state
/// handler, so a later step that moves the creature moves it away from the copy, and one that resets
/// the copy with the position (a landing, a fall) keeps its reset (Q794).</summary>
public class PreviousPositionStepTests
{
    private const byte Keeper = 3;

    [Fact]
    public void TheCopyIsThePositionBeforeTheHandlerMovesTheCreature()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = Open();
        int x = goblin.PosX, y = goblin.PosY;
        var sim = new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(new PreviousPositionStep(), new Probe(c => c.PosX += 300))]);

        sim.Tick();

        Assert.Equal((x + 300, y), (goblin.PosX, goblin.PosY));
        Assert.Equal((x, y), (goblin.LastPosX, goblin.LastPosY));
    }

    [Fact]
    public void AResetLaterInTheSameUpdateIsKept()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = Open();
        int x = goblin.PosX + 500, y = goblin.PosY;
        var sim = new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(new PreviousPositionStep(), new Probe(c =>
            {
                c.PosX = c.LastPosX = x;
                c.PosY = c.LastPosY = y;
            }))]);

        sim.Tick();

        Assert.Equal((x, y), (goblin.LastPosX, goblin.LastPosY));
    }

    private static (GameDatabase, WorldState, CreatureState) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(12, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        goblin.LastPosX = goblin.LastPosY = 0;
        return (db, world, goblin);
    }

    private sealed class Probe(Action<CreatureState> act) : ICreatureUpdateStep
    {
        public string Name => "probe";

        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

        public void Update(SimulationContext context, CreatureState creature) => act(creature);
    }
}
