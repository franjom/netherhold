// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The computer keeper's readiness grant as a command (ADR-0298): one ordinary level-up
/// for an own living creature, and nothing for anyone else's.</summary>
public class GrantCreatureLevelTests
{
    private const byte Keeper = 3, Rival = 4;

    private static (GameDatabase Db, WorldState World, CreatureState Goblin) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 8; y <= 12; y++)
            for (int x = 8; x <= 12; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        return (db, world, goblin);
    }

    [Fact]
    public void AnOwnCreatureGainsOneLevelWithItsHealthRescaledAsTheLevelUpDoes()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = Setup();
        goblin.Health /= 2;
        int before = goblin.Health;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem()]);
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(new GrantCreatureLevelCommand(Keeper, goblin.Id)));

        // 0x0048B120: the level rises by one and the health keeps its share of the maximum.
        Assert.Equal(2, goblin.Level);
        Assert.True(goblin.Health > before, $"health {goblin.Health} did not scale from {before}");
        Assert.Contains($"creature={goblin.Id} level=2", Assert.Single(sink.Events, e => e.Kind == "level_granted").Detail);
    }

    [Theory]
    [InlineData(0)] // another keeper's command
    [InlineData(1)] // a creature that is not there
    [InlineData(2)] // level ten
    public void AForeignMissingOrTopLevelCreatureIsRefusedUnchanged(int which)
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = Setup();
        byte player = which == 0 ? Rival : Keeper;
        int id = which == 1 ? goblin.Id + 100 : goblin.Id;
        if (which == 2) while (goblin.Level < CreatureLevelRules.MaxLevel) CreatureLevelRules.LevelUp(new StateHookContext(world, db), db.CreatureById(goblin.CreatureTypeId), goblin);
        int level = goblin.Level, health = goblin.Health;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem()]);
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(new GrantCreatureLevelCommand(player, id)));

        Assert.Equal((level, health), (goblin.Level, goblin.Health));
        Assert.Contains(sink.Events, e => e.Kind == "level_grant_refused");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "level_granted");
    }
}
