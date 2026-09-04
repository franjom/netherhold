// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Health and hunger — the first things a creature has that are not about where
/// it is standing.
///
/// <para>The fixture's two creatures are chosen opposites and that is what makes
/// them useful here: the <b>Imp</b> never eats, never sleeps, and is one of only
/// two creatures in the game that heal on their own land; the <b>Goblin</b> gets
/// hungry every 180 seconds, needs one chicken, and heals on nobody's land. So
/// every rule can be tested with a creature that has it and one that does
/// not.</para>
///
/// <para>Several of these are negative tests about things that are deliberately
/// absent — hunger costs no health, an unfed creature does not starve — because
/// those are the assumptions most likely to be "fixed" by someone later without
/// noticing that DK2 charges anger for it instead.</para>
/// </summary>
public class CreatureNeedsTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;
    private const byte GoblinId = 7;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static GameSimulation Simulation(GameDatabase db, WorldState world) =>
        new(db, world, seed: 42, [new CreatureHealthSystem()]);

    private static void Paint(
        GameDatabase db, WorldState world, RoomTypeId type, TilePos at, byte owner)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(type), owner);
    }

    private const int Second = SimulationTiming.TicksPerSecond;

    // ---- health exists at all ---------------------------------------------

    [Fact]
    public void ACreatureArrivesAtFullHealth()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        Assert.Equal(500, imp.Health);
        Assert.Equal(500, CreatureHealthRules.MaxHealth(
            db.CreatureById(Dk2TerrainFixture.ImpCreatureId)));
    }

    [Fact]
    public void HealthCannotGoAboveTheRecordsMaximumOrBelowZero()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        // Healing at full health lands nothing at all, which is what lets the
        // per-second system apply regeneration unconditionally.
        Assert.Equal(0, CreatureHealthRules.Apply(goblin, state, 500));
        Assert.Equal(1500, state.Health);

        // Overkill lands only what was left.
        Assert.Equal(-1500, CreatureHealthRules.Apply(goblin, state, -99999));
        Assert.Equal(0, state.Health);

        // And healing from zero works, because zero is not a trapdoor in the
        // rules — the system, not the clamp, decides what death is.
        Assert.Equal(200, CreatureHealthRules.Apply(goblin, state, 200));
    }

    [Fact]
    public void TheTwoThresholdsAreTheDatasOwnPercentages()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        // Critical is 20% of max, sleep-from-wounds is below 50%.
        Assert.False(CreatureHealthRules.IsCriticallyHurt(db, goblin, state));
        Assert.False(CreatureHealthRules.ShouldSleepFromWounds(db, goblin, state));

        state.Health = 749;                 // just under half of 1500
        Assert.True(CreatureHealthRules.ShouldSleepFromWounds(db, goblin, state));
        Assert.False(CreatureHealthRules.IsCriticallyHurt(db, goblin, state));

        state.Health = 300;                 // exactly 20%
        Assert.True(CreatureHealthRules.IsCriticallyHurt(db, goblin, state));
    }

    // ---- regeneration on your own land ------------------------------------

    [Fact]
    public void AnImpHealsOnItsOwnLandAtTheRateItsRecordGives()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(2, 2);
        world.Tile(at.X, at.Y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(at.X, at.Y).OwnerPlayerId = Keeper;

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        imp.Health = 100;

        Simulation(db, world).Run(Second);

        // 67 a second, once.
        Assert.Equal(167, imp.Health);
    }

    [Fact]
    public void AnImpDoesNotHealOnLandItsKeeperDoesNotOwn()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(2, 2);
        world.Tile(at.X, at.Y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(at.X, at.Y).OwnerPlayerId = Rival;

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        imp.Health = 100;

        Simulation(db, world).Run(Second * 5);

        Assert.Equal(100, imp.Health);
    }

    [Fact]
    public void AGoblinNeverHealsOnItsOwnLandBecauseItsRecordSaysZero()
    {
        // 44 of the 46 creatures are like this. Own-land regeneration is a real
        // rule that almost nothing uses, and a suite that only tested the Imp
        // would read as though it were general.
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(2, 2);
        world.Tile(at.X, at.Y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(at.X, at.Y).OwnerPlayerId = Keeper;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.Health = 100;

        Simulation(db, world).Run(Second * 10);

        Assert.Equal(100, goblin.Health);
    }

    // ---- the lair ---------------------------------------------------------

    [Fact]
    public void AGoblinInItsKeepersLairHealsAtAHundredASecond()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.Health = 500;

        Simulation(db, world).Run(Second * 3);

        Assert.Equal(800, goblin.Health);
    }

    [Fact]
    public void ALairBelongingToSomebodyElseDoesNotHeal()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Rival);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.Health = 500;

        Simulation(db, world).Run(Second * 3);

        Assert.Equal(500, goblin.Health);
    }

    [Fact]
    public void AnImpInItsKeepersLairGetsBothRates()
    {
        // The only creature in the fixture that can, and the reason the two
        // sources are documented as additive rather than exclusive (Q100).
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        imp.Health = 100;

        Simulation(db, world).Run(Second);

        Assert.Equal(100 + 100 + 67, imp.Health);
    }

    // ---- hunger -----------------------------------------------------------

    [Fact]
    public void AGoblinGetsHungryAfterExactlyItsHungerRate()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        GameSimulation sim = Simulation(db, world);

        sim.Run(Second * 179);
        Assert.Equal(0, goblin.ChickensNeeded);

        sim.Run(Second);
        Assert.Equal(1, goblin.ChickensNeeded);
    }

    [Fact]
    public void AnImpNeverGetsHungryNoMatterHowLongItWaits()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        Simulation(db, world).Run(Second * 600);

        Assert.Equal(0, imp.ChickensNeeded);
        Assert.Equal(0, imp.HungerTicks);   // the clock does not even run
    }

    [Fact]
    public void BeingHungryCostsNoHealthAtAll()
    {
        // Deliberate, and the reason is in the data rather than in mercy: DK2
        // charges *anger* for going unfed (the record's angerNoFood, 100 for a
        // Goblin) and there is no variable anywhere in the balance table that
        // turns hunger into damage. If anger arrives and this test is changed,
        // it should be changed because the game says so.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Simulation(db, world).Run(Second * 600);

        Assert.Equal(1, goblin.ChickensNeeded);     // hungry, and stays hungry
        Assert.Equal(1500, goblin.Health);          // and entirely unharmed
    }

    [Fact]
    public void EatingCountsDownAndOnlyTheLastChickenResetsTheClock()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        state.ChickensNeeded = 2;           // as a Warlock would be
        state.HungerTicks = 999;
        state.Health = 500;

        CreatureHealthRules.Eat(goblin, state);
        Assert.Equal(1, state.ChickensNeeded);
        Assert.Equal(999, state.HungerTicks);       // still hungry, clock runs on
        Assert.Equal(875, state.Health);            // 375 from the chicken

        CreatureHealthRules.Eat(goblin, state);
        Assert.Equal(0, state.ChickensNeeded);
        Assert.Equal(0, state.HungerTicks);         // full: the clock restarts
    }

    // ---- death ------------------------------------------------------------

    [Fact]
    public void ACreatureAtZeroHealthLeavesTheWorld()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        goblin.Health = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second);

        Assert.Empty(world.Creatures);
        Assert.Contains(events.Events, e => e.Kind == "creature_died");
    }

    [Fact]
    public void DeathReportsWhetherTheCreatureLeavesABody()
    {
        // The branch that cannot be taken yet: bodies are objects (M6). The
        // flag is read and published so the day it matters, the rule is already
        // sourced from the record rather than invented.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        goblin.Health = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second);

        SimEvent died = events.Events.First(e => e.Kind == "creature_died");
        Assert.Contains("leaves_body=", died.Detail);
    }

    [Fact]
    public void AHealthyCreatureIsNotQuietlyKilledByTheSystemThatCouldKillIt()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Simulation(db, world).Run(Second * 60);

        Assert.Single(world.Creatures);
    }

    // ---- the sentinel -----------------------------------------------------

    [Fact]
    public void TheNeverSentinelIsShortMaxValue()
    {
        // 32767 in a field wide enough to hold more, so it is a saturated
        // 16-bit value rather than a designed constant — but it means never
        // either way, since 32767 seconds is nine hours.
        Assert.Equal(short.MaxValue, CreatureHealthRules.Never);

        CreatureDef imp = Db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        Assert.Equal(CreatureHealthRules.Never, imp.HungerRate.Raw / Fp32.One);
        Assert.False(CreatureHealthRules.CanGetHungry(imp));
    }
}
