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
    public void AGoblinAsleepInItsOwnBedHealsAtAHundredASecond()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.Health = 500;
        goblin.LairTileIndex = (at.Y * world.Width) + at.X;
        goblin.Activity = CreatureActivity.Sleeping;

        Simulation(db, world).Run(Second * 3);

        Assert.Equal(800, goblin.Health);
    }

    [Fact]
    public void AGoblinStandingAwakeInItsOwnBedHealsNothing()
    {
        // CONFIRMED by the owner: a creature heals *while it sleeps* in its own
        // bed. Presence is not enough, and this is the half of the rule that the
        // first implementation had wrong.
        GameDatabase db = Db;
        WorldState world = World(db);

        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.Health = 500;
        goblin.LairTileIndex = (at.Y * world.Width) + at.X;
        goblin.Activity = CreatureActivity.Idle;

        // The sleep system would eventually put it to bed, so run only the
        // health system: this is about the healing gate, not the cycle.
        new GameSimulation(db, world, 42, [new CreatureHealthSystem()]).Run(Second * 3);

        Assert.Equal(500, goblin.Health);
    }

    [Fact]
    public void AGoblinAsleepOnSomebodyElsesBedHealsNothing()
    {
        // The other half: its *own* bed, not any lair tile. One bed per tile,
        // one tile per creature.
        GameDatabase db = Db;
        WorldState world = World(db);

        Paint(db, world, RoomTypeId.Lair, new TilePos(4, 4), Keeper);
        Paint(db, world, RoomTypeId.Lair, new TilePos(5, 4), Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(4, 4));
        goblin.Health = 500;
        goblin.Activity = CreatureActivity.Sleeping;

        // Its bed is the tile next door, and it is lying on the wrong one.
        goblin.LairTileIndex = (4 * world.Width) + 5;

        new GameSimulation(db, world, 42, [new CreatureHealthSystem()]).Run(Second * 3);

        Assert.Equal(500, goblin.Health);
    }

    [Fact]
    public void AGoblinWithNoBedNeverHealsHoweverLongItRuns()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // Lair terrain, its keeper's, and the creature is standing on it — but
        // no bed has been claimed, so there is nothing to sleep in.
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.Health = 500;

        new GameSimulation(db, world, 42, [new CreatureHealthSystem()]).Run(Second * 30);

        Assert.Equal(500, goblin.Health);
    }

    [Fact]
    public void AnImpAsleepInABedWouldGetBothRatesButAnImpHasNoBed()
    {
        // The Imp's LairObjectId is 0, which the owner confirms for the
        // Skeleton too: some creatures simply have no lair. So the additive
        // question (Q100) is unreachable for the only fixture creature that
        // regenerates, and own-land healing is all an imp ever gets.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        Assert.False(LairRules.NeedsBed(imp));

        WorldState world = World(db);
        var at = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        state.Health = 100;

        Assert.False(LairRules.TryClaimBed(world, db, state));
        Assert.Equal(-1, state.LairTileIndex);

        Simulation(db, world).Run(Second);

        // 67 from its own land — a lair tile its keeper owns is its own land —
        // and nothing from the lair, because it cannot sleep in one.
        Assert.Equal(167, state.Health);
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

    // ---- terrain health deltas --------------------------------------------

    [Fact]
    public void StandingInWaterCostsAHundredHealthASecond()
    {
        // The owner marked a gem tile on an island cut off by water, with no
        // bridge, and imps waded across to mine it — which CONFIRMED that
        // hazards are crossable and then exposed that crossing one was free
        // here. Water's TerrainDef.Damage is 100 and was read by nothing but
        // the asset catalogue.
        GameDatabase db = Db;
        WorldState world = World(db);
        var pond = new TilePos(3, 6);
        world.Tile(pond.X, pond.Y).TerrainId = Dk2TerrainFixture.Water;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, pond);
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second * 3);

        Assert.Equal(1500 - 300, goblin.Health);
        Assert.Contains(events.Events, e => e.Kind == "creature_terrain_health");
    }

    [Fact]
    public void ACreatureLeftInWaterLongEnoughDrowns()
    {
        // Fifteen seconds for a Goblin, five for an Imp. Which is why the
        // cadence matters and is flagged as ours (Q117): per tick instead of
        // per second would make hazards twenty times deadlier.
        GameDatabase db = Db;
        WorldState world = World(db);
        var pond = new TilePos(3, 6);
        world.Tile(pond.X, pond.Y).TerrainId = Dk2TerrainFixture.Lava;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, pond);

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second * 15);

        // Out of health, and now in the dying window rather than dead.
        Assert.Equal(0, goblin.Health);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
    }

    [Fact]
    public void TerrainIsStillAppliedToACollapsedCreatureButCannotTakeItFurther()
    {
        // Water is applied to the down as well as the standing, because a
        // creature that drops in water is in water and lying in it is no safer
        // than walking through it.
        //
        // **But it has no observable effect**, and this test exists because the
        // first version of it asserted the opposite — that terrain was "the one
        // thing that can still kill something during its rescue window". It is
        // not: a creature at zero health cannot be taken lower, so the clamp
        // makes every subsequent second a no-op and only ONE terrain event
        // fires, the partial hit that took it to zero.
        //
        // The code is left as it is rather than adding a skip, because the rule
        // "terrain acts on whatever is standing in it" is the true one and will
        // start mattering the moment anything can heal a down creature in
        // place. What is corrected is the claim, not the behaviour.
        GameDatabase db = Db;
        WorldState world = World(db);
        var pond = new TilePos(3, 6);
        world.Tile(pond.X, pond.Y).TerrainId = Dk2TerrainFixture.Water;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, pond);
        goblin.Health = 50;
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second * 5);

        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
        Assert.Equal(0, goblin.Health);

        // Exactly one: the 50 that was left, and nothing after.
        Assert.Single(events.Events, e => e.Kind == "creature_terrain_health");
        Assert.Contains("delta=-50", events.Events
            .First(e => e.Kind == "creature_terrain_health").Detail);
    }

    [Fact]
    public void DryLandCostsNothing()
    {
        // The control, and it is not idle: 38 of 41 terrains carry no delta at
        // all, so a rule that charged everybody would be wrong nearly always.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(1500, goblin.Health);
    }

    // ---- death ------------------------------------------------------------

    [Fact]
    public void ACreatureAtZeroHealthCollapsesAndDoesNotDieYet()
    {
        // The correction Q99 forced. This used to assert the creature was gone
        // after one second, which was the whole mechanic missing: DK2 gives it
        // CreatureDyingStateDurationSeconds on the floor first.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        goblin.Health = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second);

        Assert.Single(world.Creatures);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
        Assert.Contains(events.Events, e => e.Kind == "creature_collapsed");
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_died");
    }

    [Fact]
    public void ACollapsedCreatureLiesThereForSixtySecondsAndThenDies()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        goblin.Health = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);

        // One second short of the window: still lying there, still rescuable.
        sim.Run(Second * 60);
        Assert.Single(world.Creatures);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);

        // And the second the window closes.
        sim.Run(Second);
        Assert.Empty(world.Creatures);
        Assert.Contains(events.Events, e => e.Kind == "creature_died");
    }

    [Fact]
    public void TheDyingWindowIsTheDatabasesAndNotAConstant()
    {
        GameDatabase db = Db;

        Assert.Equal(60, db[VariableTypeId.CreatureDyingStateDurationSeconds]);
        Assert.Equal(Second * 60, CreatureDeathRules.DyingWindowTicks(db));

        // The second timer, which nothing implements: a corpse is an object
        // (M6). Asserted anyway so the number is pinned for the day it is used,
        // and so that two variables sharing a value stays a fact rather than a
        // coincidence someone collapses into one.
        Assert.Equal(60, db[VariableTypeId.DeadBodyDiesAfterSeconds]);
        Assert.Equal(Second * 60, CreatureDeathRules.BodyWindowTicks(db));
    }

    [Fact]
    public void AnImpIsDeadRightAwayAndHasNoLyingState()
    {
        // CONFIRMED by the owner: "imps are dead right away, they don't have a
        // lying state." The discriminator is the GenerateDeadBody flag, which
        // an Imp lacks — and OpenKeeper tests exactly the same flag before any
        // unconscious handling, so the rule generalises to all eight bodyless
        // creatures rather than being an Imp special case.
        //
        // Placed on its own lair tile on purpose: an Imp regenerates 67 health
        // a second at home, so if the collapse were deferred by even one second
        // it would heal instead of dying.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(2, 2);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        imp.Health = 0;
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second);

        Assert.Empty(world.Creatures);
        Assert.Contains(events.Events, e => e.Kind == "creature_died");
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_collapsed");
    }

    [Fact]
    public void OwnLandRegenerationIsSuppressedForACreatureThatIsDown()
    {
        // The guard that keeps a collapsed creature on the floor. It no longer
        // has an Imp to protect against — an imp dies outright — but it still
        // has KING REGINALD, who regenerates 400 a second on his own land and
        // *does* leave a body, so he is the creature who would be unkillable at
        // home without this. Asserted on the rule rather than through a system
        // because the fixture has no creature with both properties.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(2, 2);
        Paint(db, world, RoomTypeId.Lair, at, Keeper);

        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);

        // Alive on its own land: the record's rate.
        Assert.Equal(67, CreatureHealthRules.OwnLandRegenPerSecond(world, imp, state));

        // Out of health: nothing, whether or not the collapse has been booked.
        state.Health = 0;
        Assert.Equal(0, CreatureHealthRules.OwnLandRegenPerSecond(world, imp, state));

        CreatureDeathRules.Collapse(state);
        Assert.Equal(0, CreatureHealthRules.OwnLandRegenPerSecond(world, imp, state));
    }

    [Fact]
    public void ADyingCreatureDoesNotGetHungrier()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Health = 0;
        Simulation(db, world).Run(Second * 30);

        // One second's worth, from the second it dropped — the collapse is
        // booked at the end of that second, after the hunger clock has already
        // ticked. From then on the clock is frozen: a creature lying on the
        // floor is not working towards its next meal.
        Assert.Equal(Second, goblin.HungerTicks);
        Assert.Equal(0, goblin.ChickensNeeded);
    }

    [Fact]
    public void ARescuedCreatureWakesInItsBedAndIsHealedRatherThanCollapsingAgain()
    {
        // The ordering test. Rescue puts the creature in its bed at zero health;
        // the lair heals it before anything asks whether it has collapsed. Swap
        // those two steps in CreatureHealthSystem and this goes red.
        GameDatabase db = Db;
        WorldState world = World(db);
        var bed = new TilePos(5, 5);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, bed);
        Assert.True(LairRules.TryClaimBed(world, db, goblin));

        goblin.Health = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);

        Assert.True(CreatureDeathRules.Rescue(world, goblin));
        Assert.Equal(CreatureActivity.Sleeping, goblin.Activity);
        Assert.Equal(0, goblin.Health);              // rescue heals nothing itself

        sim.Run(Second);
        Assert.Equal(100, goblin.Health);            // the lair does, at 100 a second
        Assert.Single(world.Creatures);
    }

    [Fact]
    public void ACreatureWithNoBedCannotBeRescued()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(goblin));

        // Nowhere to carry it to. Its keeper never built a lair.
        Assert.Equal(-1, goblin.LairTileIndex);
        Assert.False(CreatureDeathRules.Rescue(world, goblin));
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
    }

    [Fact]
    public void ACollapsedCreatureKeepsItsBedUntilItActuallyDies()
    {
        // Releasing the bed on collapse would be the obvious tidy-up and would
        // break the rescue: another creature would move into the bed of the one
        // lying on the floor waiting to be carried back to it.
        GameDatabase db = Db;
        WorldState world = World(db);
        var bed = new TilePos(5, 5);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, bed);
        Assert.True(LairRules.TryClaimBed(world, db, first));
        int held = first.LairTileIndex;

        first.Health = 0;
        var sim = new GameSimulation(
            db, world, 42, [new CreatureSleepSystem(), new CreatureHealthSystem()]);
        sim.Run(Second * 30);

        Assert.Equal(CreatureActivity.Dying, first.Activity);
        Assert.Equal(held, first.LairTileIndex);         // still holds it

        // And the only other tile in a one-tile lair is taken, so a newcomer
        // gets nothing rather than the dying creature's bed.
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Assert.False(LairRules.TryClaimBed(world, db, second));

        // Death is what frees it.
        sim.Run(Second * 31);
        Assert.DoesNotContain(world.Creatures, c => c.Id == first.Id);
        Assert.True(LairRules.TryClaimBed(world, db, second));
        Assert.Equal(held, second.LairTileIndex);
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
        sim.Run(Second * 61);

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

    // ---- beds, and the whole cycle end to end -----------------------------

    [Fact]
    public void AGoblinClaimsABedTheTickItCanAndTheLairHoldsOnePerTile()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // Two tiles of lair, so two beds and no more.
        Paint(db, world, RoomTypeId.Lair, new TilePos(4, 4), Keeper);
        Paint(db, world, RoomTypeId.Lair, new TilePos(5, 4), Keeper);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 3));
        CreatureState third = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 4));

        new GameSimulation(db, world, 42, [new CreatureSleepSystem()]).Run(1);

        // One bed each for the first two, in creature order, and nothing for
        // the third — a lair's capacity is its tile count.
        Assert.True(first.LairTileIndex >= 0);
        Assert.True(second.LairTileIndex >= 0);
        Assert.NotEqual(first.LairTileIndex, second.LairTileIndex);
        Assert.Equal(-1, third.LairTileIndex);
    }

    [Fact]
    public void AGoblinWalksToItsOwnBedFallsAsleepHealsAndWakesUp()
    {
        // The whole cycle, through the real systems, with the creature starting
        // wounded so that CreatureSleepsWhenBelowPercentHealth sends it to bed
        // at once rather than after its 240 awake seconds.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        var bed = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);

        // A walkable route from the creature to the bed.
        for (int x = 2; x <= 4; x++)
        {
            ref TileState tile = ref world.Tile(x, 4);
            if (tile.TerrainId == Dk2TerrainFixture.LairTerrain) continue;
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 4));
        goblin.Health = 300;    // a fifth of 1500, well under the 50% threshold

        var sim = new GameSimulation(db, world, 42, [
            new CreatureMovementSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
        ]);
        sim.ObserveWith(events);
        sim.Run(Second * 30);

        Assert.Contains(events.Events, e => e.Kind == "bed_claimed");
        Assert.Contains(events.Events, e => e.Kind == "going_to_sleep");
        Assert.Contains(events.Events, e => e.Kind == "fell_asleep");
        Assert.Contains(events.Events, e => e.Kind == "woke_up");

        // It slept for its record's nine seconds at 100 a second, so it woke up
        // considerably better off than it lay down.
        Assert.True(goblin.Health > 300, $"health is {goblin.Health}, expected a heal");
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(0, goblin.SleepTicks);
    }

    [Fact]
    public void AWoundedGoblinWithNoLairStaysAwakeAndStaysWounded()
    {
        // No lair at all, so no bed, so nowhere to go. In DK2 this is what
        // angerNoLair is for — 100 for a Goblin — and anger does not exist yet
        // (Q105), so the visible consequence is simply that it never recovers.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Health = 300;

        new GameSimulation(db, world, 42, [
            new CreatureMovementSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
        ]).Run(Second * 60);

        Assert.Equal(-1, goblin.LairTileIndex);
        Assert.Equal(300, goblin.Health);
        Assert.NotEqual(CreatureActivity.Sleeping, goblin.Activity);
    }

    [Fact]
    public void ABedSoldOutFromUnderACreatureIsGivenUp()
    {
        // A bed is not a promise: the lair can be sold, dug out or taken while
        // its owner is asleep in it. Without this the creature would go on
        // healing on a patch of claimed floor.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        var bed = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, bed);
        goblin.Health = 500;

        var sim = new GameSimulation(db, world, 42, [
            new CreatureSleepSystem(), new CreatureHealthSystem(),
        ]);
        sim.ObserveWith(events);
        sim.Run(1);

        Assert.Equal((4 * world.Width) + 4, goblin.LairTileIndex);

        // The lair goes back to plain floor under it.
        RoomRules.Unmake(db, ref world.Tile(bed.X, bed.Y));
        sim.Run(1);

        Assert.Equal(-1, goblin.LairTileIndex);
        Assert.Contains(events.Events, e => e.Kind == "bed_lost");
    }

    [Fact]
    public void ADeadCreaturesBedIsFreedForSomebodyElse()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        var bed = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var sim = new GameSimulation(db, world, 42, [
            new CreatureSleepSystem(), new CreatureHealthSystem(),
        ]);
        sim.Run(1);
        Assert.True(first.LairTileIndex >= 0);

        // It dies, and the one bed in the lair has to become available again —
        // otherwise a lair silently loses a tile every time somebody dies.
        //
        // Two seconds used to be enough, because zero health used to be death.
        // It now takes CreatureDyingStateDurationSeconds first (Q99), and the
        // bed stays held for the whole of that window on purpose — the rescue
        // path carries the creature back to it.
        first.Health = 0;
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 3));
        sim.Run(Second * 61);

        Assert.DoesNotContain(world.Creatures, c => c.Id == first.Id);
        Assert.True(second.LairTileIndex >= 0, "the dead creature's bed was never freed");
    }

    [Fact]
    public void AnImpIsNeverDrivenBySleepEvenIfItSomehowGotABed()
    {
        // The worker guard is explicit rather than incidental. An imp is
        // excluded because it is a worker, not merely because its record
        // happens to say LairObjectId 0.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, new TilePos(4, 4), Keeper);

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        imp.LairTileIndex = (4 * world.Width) + 4;

        new GameSimulation(db, world, 42, [new CreatureSleepSystem()]).Run(Second * 300);

        Assert.Equal(CreatureActivity.Idle, imp.Activity);
        Assert.Equal(0, imp.AwakeTicks);
    }
}
