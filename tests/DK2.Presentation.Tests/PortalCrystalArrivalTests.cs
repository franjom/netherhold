// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class PortalCrystalArrivalTests
{
    [InstallTheory]
    [InlineData(5, 3)]
    [InlineData(7, 5)]
    [InlineData(5, 7)]
    [InlineData(3, 5)]
    public void AnArrivingGoblinCanLeaveAfterItsAuthoredEntranceOffset(int x, int y)
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db,
            new LevelMap(12, 12, Enumerable.Repeat(new MapTile(8, PlayerIds.FirstKeeper, 0, 0), 144).ToArray()),
            [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int py = 4; py <= 6; py++) for (int px = 4; px <= 6; px++)
            RoomRules.Place(db, ref world.Tile(px, py), db.Room(RoomTypeId.Portal), PlayerIds.FirstKeeper);
        var definition = db.CreatureByName("Goblin")!;
        var creature = world.SpawnCreature(db, definition.CreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        new GameSimulation(db, world, 42, []).Run(PortalRules.JoiningTicks(definition) + 1);
        Assert.False(PortalRules.StillJoining(world, creature, definition));
        var navigation = new WorldSteering(db, world);
        Assert.True(navigation.TryRoute(creature, new TilePos(x, y), MoveOptions.ExactSpot));
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem()]);
        for (int tick = 0; tick < 600 && !creature.Body.PathFinished; tick++) sim.Tick();
        Assert.Equal(new TilePos(x, y), creature.Tile);
        Assert.True(creature.Body.PathFinished);
    }
}
