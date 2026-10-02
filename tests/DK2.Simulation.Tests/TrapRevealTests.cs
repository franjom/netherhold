// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The reveal (traps.md §11): a creature that reveals adjacent traps, on entering a new tile,
/// unhides every enemy trap on the nine tiles around it and sets its fight counter to 10.</summary>
public class TrapRevealTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte RevealerId = 60;

    private static CreatureDef Revealer() => Dk2TerrainFixture.Goblin() with
    {
        Name = "Revealer",
        CreatureId = RevealerId,
        Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.RevealsAdjacentTraps,
    };

    private static (GameDatabase db, WorldState world, TrapState spike) Open(byte trapOwner)
    {
        GameDatabase db = Dk2TerrainFixture.Database(Revealer());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        ref TileState tile = ref world.Tile(8, 5);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = trapOwner;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = 8, Y = 5, TrapId = Dk2TerrainFixture.SpikeTrapId, PlayerId = trapOwner, NumberOfShots = 1 }]);
        return (db, world, world.Traps[^1]);
    }

    /// <summary>Puts the creature on the next tile, as a mover would between two updates.</summary>
    private static void Step(CreatureState creature, int x, int y)
    {
        creature.PosX = CreatureState.CentreOf(x);
        creature.PosY = CreatureState.CentreOf(y);
    }

    [Fact]
    public void ARevealerEnteringATileBesideAHiddenEnemyTrapRevealsIt_AndItsFightCounterIsSetToTen()
    {
        (GameDatabase db, WorldState world, TrapState spike) = Open(Keeper);
        Assert.True(spike.Hidden);
        CreatureState revealer = world.SpawnCreature(db, RevealerId, Rival, new TilePos(5, 5));
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new CombatSystem()]);
        sim.ObserveWith(sink);

        sim.Run(1);
        Step(revealer, 6, 5);
        sim.Run(1);
        Assert.True(spike.Hidden); // two tiles away: outside the nine
        Step(revealer, 7, 4);
        sim.Run(1);
        Assert.False(spike.Hidden);
        Assert.Equal(TrapRules.RevealFightCounter, revealer.FightProcessCounter);
        Assert.Single(sink.Events, e => e.Kind == "trap_revealed");
    }

    [Fact]
    public void AnOrdinaryCreature_OrARevealerBesideItsOwnKeepersTrap_LeavesTheTrapHidden()
    {
        (GameDatabase db, WorldState world, TrapState spike) = Open(Keeper);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        CreatureState own = world.SpawnCreature(db, RevealerId, Keeper, new TilePos(6, 6));
        var sim = new GameSimulation(db, world, 1, [new CombatSystem()]);

        sim.Run(1);
        Step(goblin, 7, 5);
        Step(own, 7, 6);
        sim.Run(1);
        Assert.True(spike.Hidden);
    }
}
