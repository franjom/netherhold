// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The trap shots read for ADR-0331 (traps.md §12, shots.md §7, §8): the Alarm's message, the
/// Fear's stub, the Spike's square and the gas cloud.</summary>
public sealed class TrapShotTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static (GameDatabase db, WorldState world) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        world.Player(Keeper).Mana = 1000;
        return (db, world);
    }

    private static TrapState Place(GameDatabase db, WorldState world, byte trapId, int x, int y, byte owner = Keeper)
    {
        ref TileState tile = ref world.Tile(x, y);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = x, Y = y, TrapId = trapId, PlayerId = owner, NumberOfShots = 1 }]);
        return world.Traps[^1];
    }

    private static CreatureState Enemy(GameDatabase db, WorldState world, int x, int y)
    {
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(x, y));
        creature.Health = 100000;
        return creature;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem(), new ShotSystem(), new TrapSystem(), new TrapRemovalSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    [Fact]
    public void AnAlarmTrapsShotPostsMessage47ToItsOwnerWithTheShotsTile()
    {
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.AlarmTrapId, 5, 5);
        Enemy(db, world, 7, 5);
        var sink = new ListEventSink();

        Sim(db, world, sink).Run(4);

        PlayerMessage message = Assert.Single(world.Messages.Entries, m => m.Kind == PlayerMessageKind.AlarmRaised);
        Assert.Equal((Keeper, 5, 5), (message.PlayerId, message.Data1, message.Data2));
        Assert.Equal((uint)SimulationTiming.TicksFromSeconds(50), message.Timeout);
    }

    [Fact]
    public void AFearTrapsShotDoesNothing_ItsTrapsThreatFrightens()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState fear = Place(db, world, Dk2TerrainFixture.FearTrapId, 5, 5);
        CreatureState goblin = Enemy(db, world, 7, 5);
        var sink = new ListEventSink();

        Sim(db, world, sink).Run(4);

        Assert.Contains(sink.Events, e => e.Kind == "trap_fired" && e.Detail.Contains($"trap={fear.Id} "));
        Assert.Equal(600, fear.Threat);
        Assert.True(goblin.TempFear > 0);
        Assert.Equal(100000, goblin.Health);
    }

    [Fact]
    public void ASpikeHurtsTheTwoByTwoSquareNorthWestOfItsTile_TheBoundsAreHalfOpenAsRead()
    {
        // 0x004AC5A0 runs from tile - data2 to below tile + data2: with data2 1, (4..5, 4..5) for the
        // Spike at (5,5). The goblin on the trap is struck; one at (4,4) is too; one at (6,5) is not.
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.SpikeTrapId, 5, 5);
        CreatureState onTrap = Enemy(db, world, 5, 5);
        CreatureState northWest = Enemy(db, world, 4, 4);
        CreatureState east = Enemy(db, world, 6, 5);
        var sink = new ListEventSink();

        Sim(db, world, sink).Run(4);

        Assert.True(onTrap.Health < 100000);
        Assert.True(northWest.Health < 100000);
        Assert.Equal(100000, east.Health);
        Assert.Contains(sink.Events, e => e.Kind == "shot_square_hit" && e.Detail.Contains("damage=750"));
    }

    [Fact]
    public void AGasCloudBurnsAnEnemyCreature_ButSparesARivalsTrap_ForItsCategoryIsGas()
    {
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.GasTrapId, 5, 5);
        TrapState rival = Place(db, world, Dk2TerrainFixture.SentryTrapId, 6, 5, owner: Rival);
        CreatureState goblin = Enemy(db, world, 5, 5);
        var sink = new ListEventSink();

        Sim(db, world, sink).Run(6);

        Assert.Contains(sink.Events, e => e.Kind == "shot_area_hit" && e.Detail.Contains($"target={goblin.Id} "));
        Assert.Equal(db.Traps[Dk2TerrainFixture.SentryTrapId].Health, rival.Health);
    }

    [Fact]
    public void TheAlarmFearSpikeAndGasShotsCountAsBuilt()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        foreach (byte id in new[] { Dk2TerrainFixture.AlarmTrapId, Dk2TerrainFixture.FearTrapId, Dk2TerrainFixture.SpikeTrapId, Dk2TerrainFixture.GasTrapId })
            Assert.True(TrapRules.FiringBuilt(db, db.Traps[id]), db.Traps[id].Name);
    }
}
