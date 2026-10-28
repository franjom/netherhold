// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Health and hunger.</summary>
/// <remarks>
/// <para>The fixture's creatures are chosen opposites: the Imp never eats, never
/// sleeps, and is one of only two creatures that heal on their own land; the
/// Goblin gets hungry every 180 seconds, needs one chicken, and heals on nobody's
/// land. Hazard terrain needed a third — water is crossable by 44 of the 46, and
/// the Vampire is one of the two it is not, so it is what every water test here
/// drowns.</para>
/// <para>Several tests here are negative — hunger costs no health, an unfed
/// creature does not starve — because those are the assumptions most likely to be
/// "fixed" later without noticing that DK2 charges anger instead.</para>
/// </remarks>
public class CreatureNeedsTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;
    private const byte GoblinId = 7;
    private const byte VampireId = Dk2TerrainFixture.VampireCreatureId;

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
            db, db.CreatureById(Dk2TerrainFixture.ImpCreatureId), imp));
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
        Assert.Equal(0, CreatureHealthRules.Apply(db, goblin, state, 500, 0));
        Assert.Equal(1500, state.Health);

        // Overkill lands only what was left.
        Assert.Equal(-1500, CreatureHealthRules.Apply(db, goblin, state, -99999, 0));
        Assert.Equal(0, state.Health);

        // And healing from zero works, because zero is not a trapdoor in the
        // rules — the system, not the clamp, decides what death is.
        Assert.Equal(200, CreatureHealthRules.Apply(db, goblin, state, 200, 0));
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
        LairRules.PlaceBed(world, db, goblin, (at.Y * world.Width) + at.X);
        StatePlacement.As(goblin, CreatureActivity.Sleeping);

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
        LairRules.PlaceBed(world, db, goblin, (at.Y * world.Width) + at.X);
        StatePlacement.As(goblin, CreatureActivity.Idle);

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
        StatePlacement.As(goblin, CreatureActivity.Sleeping);

        // Its bed is the tile next door, and it is lying on the wrong one.
        LairRules.PlaceBed(world, db, goblin, (4 * world.Width) + 5);

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

        Assert.False(LairRules.TryPlaceBedInAnyLair(world, db, state));
        Assert.Equal(-1, LairRules.BedTileIndex(world, db, state));

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
        Assert.Equal(0, goblin.HungerNeed);

        sim.Run(Second);
        Assert.Equal(1, goblin.HungerNeed);
    }

    [Fact]
    public void AnImpNeverGetsHungryNoMatterHowLongItWaits()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        Simulation(db, world).Run(Second * 600);

        Assert.Equal(0, imp.HungerNeed);
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

        // 600 seconds is three helpings of a Goblin's 180-second rate, and the
        // need ACCUMULATES: DK2 adds HungerFill each time and caps nothing, so
        // a creature with nowhere to eat owes every helping it missed
        // (docs/research/eating.md). It was 1 here until 2026-09-06, when the
        // clock stopped being a flag.
        Assert.Equal(3, goblin.HungerNeed);
        Assert.Equal(1500, goblin.Health);          // and entirely unharmed
    }

    [Fact]
    public void TheHungerClockAccumulatesRatherThanSaturating()
    {
        // The whole of what changed on 2026-09-06, stated as a difference
        // rather than as a number: two periods owe twice as much as one.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        GameSimulation sim = Simulation(db, world);

        sim.Run(Second * 180);
        Assert.Equal(1, goblin.HungerNeed);

        sim.Run(Second * 180);
        Assert.Equal(2, goblin.HungerNeed);
    }

    [Fact]
    public void EachChickenPaysOffOneAndRestartsTheClock()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        state.HungerNeed = 2;               // two helpings owed
        state.HungerTicks = 999;
        state.Health = 500;

        CreatureHealthRules.Eat(db, goblin, state);
        Assert.Equal(1, state.HungerNeed);
        Assert.Equal(0, state.HungerTicks);         // EVERY chicken restarts it
        Assert.Equal(875, state.Health);            // 375 from the chicken

        state.HungerTicks = 999;
        CreatureHealthRules.Eat(db, goblin, state);
        Assert.Equal(0, state.HungerNeed);
        Assert.Equal(0, state.HungerTicks);
    }

    // ---- terrain health deltas --------------------------------------------

    [Fact]
    public void AVampireInWaterLosesAHundredHealthASecond()
    {
        // Water's TerrainDef.Damage is 100, read by nothing but the asset
        // catalogue until the owner's observation. The creature is a Vampire
        // because water charges only what cannot cross it, and in the shipped
        // roster that is the Vampire and the Unique Vampire and nothing else.
        GameDatabase db = Db;
        WorldState world = World(db);
        var pond = new TilePos(3, 6);
        world.Tile(pond.X, pond.Y).TerrainId = Dk2TerrainFixture.Water;

        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, pond);
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second * 3);

        Assert.Equal(2500 - 300, vampire.Health);
        Assert.Contains(events.Events, e => e.Kind == "creature_terrain_health");
    }

    [Fact]
    public void AGoblinWadesTheSameWaterForNothing()
    {
        // CONFIRMED (owner, Q118): "no creature that can traverse through water
        // looses health" — charging everything standing in water would drown an
        // imp in five seconds doing the job the owner watched it do. Same tile and
        // terrain as the test above, run ten times as long; one flag differs.
        GameDatabase db = Db;
        WorldState world = World(db);
        var pond = new TilePos(3, 6);
        world.Tile(pond.X, pond.Y).TerrainId = Dk2TerrainFixture.Water;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, pond);
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);
        sim.Run(Second * 30);

        Assert.Equal(1500, goblin.Health);
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_terrain_health");
    }

    [Fact]
    public void AnImpCrossesWaterForFreeAndBurnsInLava()
    {
        // The pairing that makes the rule per-hazard rather than per-creature:
        // the Imp carries CanWalkOnWater and not CanWalkOnLava, so one creature
        // is immune to one hazard and killed by the other. Both halves are the
        // shipped record's.
        GameDatabase db = Db;
        WorldState world = World(db);
        var pond = new TilePos(3, 6);
        var fire = new TilePos(4, 6);
        world.Tile(pond.X, pond.Y).TerrainId = Dk2TerrainFixture.Water;
        world.Tile(fire.X, fire.Y).TerrainId = Dk2TerrainFixture.Lava;

        CreatureState wader = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, pond);
        CreatureState burner = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, fire);

        // No movement system, so neither imp goes anywhere. Own-land healing is
        // not in play either — this fixture map's water and lava belong to
        // nobody — which matters because an Imp regenerates 67 a second at home
        // and would otherwise outheal the lava.
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second * 3);

        Assert.Equal(500, wader.Health);
        Assert.Equal(500 - 300, burner.Health);
    }

    [Fact]
    public void ACreatureLeftInLavaLongEnoughBurnsToDeath()
    {
        // Fifteen seconds for a Goblin. Which is why the cadence matters and is
        // flagged as ours (Q118): per tick instead of per second would make
        // hazards twenty times deadlier, and the owner's answer removed the
        // easy way to measure it — nothing they can walk into water is charged
        // at all.
        GameDatabase db = Db;
        WorldState world = World(db);
        var fire = new TilePos(3, 6);
        world.Tile(fire.X, fire.Y).TerrainId = Dk2TerrainFixture.Lava;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, fire);

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second * 15);

        // Out of health, and now in the dying window rather than dead.
        Assert.Equal(0, goblin.Health);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
    }

    [Fact]
    public void TerrainIsStillAppliedToACollapsedCreatureButCannotTakeItFurther()
    {
        // Terrain acts on whatever is in it, down creatures included — lying in
        // lava is no safer than walking through it — but has no observable effect
        // during the rescue window: a creature at zero health cannot be taken
        // lower, so the clamp makes every later second a no-op and exactly one
        // terrain event fires, the partial hit that took it to zero. Kept unskipped
        // because it matters the moment anything can heal a down creature in place.
        GameDatabase db = Db;
        WorldState world = World(db);
        var fire = new TilePos(3, 6);
        world.Tile(fire.X, fire.Y).TerrainId = Dk2TerrainFixture.Lava;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, fire);
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

    // ---- the other three gates on the dying window (Q303) -----------------

    /// <summary>A creature carrying <c>DieInstantly</c> that runs out of health <b>out of
    /// doors</b> is gone on the spot: no collapse, no sixty seconds, nothing to rescue.
    /// </summary>
    [Fact]
    public void ADieInstantlyCreatureOutOfDoorsNeverReachesTheFloor()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 6));
        var events = new ListEventSink();

        goblin.DieInstantly = true;
        goblin.Health = 0;
        GameSimulation sim = Simulation(db, world);
        sim.ObserveWith(events);
        sim.Run(Second);

        // Still a corpse: the gate skips the rescuable window, not the body.
        // Gate one already required GenerateDeadBody to get this far.
        Assert.Equal(CreatureActivity.Dead, goblin.Activity);
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_collapsed");
        Assert.Contains(events.Events, e => e.Kind == "creature_died");
    }

    /// <summary>And <b>standing in a room changes nothing</b>. DK2 asks the room first, but the
    /// room it asks is the one the creature is <i>attached</i> to (<c>RoomId</c> at
    /// <c>+0x3B9</c>, read at <c>0x004B2F61</c>), written when a room takes a creature in as a
    /// user, not the one under its feet. A creature merely standing in a Treasury is attached
    /// to nothing, so <c>DieInstantly</c> is asked: the Level 2 capture shows a Lord Of The Land
    /// dying outright on the keeper's Lair at tick 3241.</summary>
    [Fact]
    public void ACreatureStandingInARoomItIsNotAttachedToIsStillAskedAboutDieInstantly()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(6, 6);
        Paint(db, world, RoomTypeId.Treasury, at, Keeper);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);

        goblin.DieInstantly = true;
        goblin.Health = 0;
        Simulation(db, world).Run(Second);

        Assert.Equal(CreatureActivity.Dead, goblin.Activity);
    }

    /// <summary>A Prison refuses the window to the creatures <b>attached</b> to it, its
    /// prisoners, not to anyone who happens to die on its tiles: the gate reads the attached
    /// room (<c>+0x3B9</c>), and a creature is attached to a prison when the prison takes it
    /// in (<c>0x004E5A70</c>). That a non-prisoner is never attached is STRONGLY INFERRED from
    /// those writers, not watched, which is why the name says so.</summary>
    [Fact]
    public void APrisonTileGivesTheWindowToACreatureThatIsNotAPrisoner_Inferred()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(6, 6);
        Paint(db, world, RoomTypeId.Prison, at, Rival);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);

        Assert.False(PrisonRules.IsImprisoned(goblin));

        goblin.Health = 0;
        Simulation(db, world).Run(Second);

        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
    }

    /// <summary>Possession is the third gate. Nothing in this simulation possesses a
    /// creature, so the rule answers from state that is always zero today — but it is state
    /// we hold, and asserting it now is what keeps the gate honest when possession arrives.
    /// </summary>
    [Fact]
    public void APossessedCreatureDiesWhereItStands()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 6));

        world.Player(Keeper).PossessedCreatureId = goblin.Id;
        goblin.Health = 0;
        Simulation(db, world).Run(Second);

        Assert.Equal(CreatureActivity.Dead, goblin.Activity);
    }

    /// <summary>Three creature types answer <c>DieInstantly</c> true whatever a script asked:
    /// <c>0x0048E110</c> compares the type id against 21, 28 and 29 and returns 1 before it
    /// ever reads the flag. Asserted on the id rather than the name because that is what the
    /// binary compares.</summary>
    [Theory]
    [InlineData(21, true)]    // King Reginald
    [InlineData(28, true)]    // Stone Knight
    [InlineData(29, true)]    // Lord Of The Land
    [InlineData(GoblinId, false)]
    public void ThreeTypesCarryDieInstantlyWithNoFlagSet(byte creatureId, bool expected)
    {
        CreatureDef def = Dk2TerrainFixture.Goblin() with { CreatureId = creatureId };
        var state = new CreatureState { DieInstantly = false };

        Assert.Equal(expected, CreatureDeathRules.DiesInstantly(def, state));
    }

    [Fact]
    public void ACollapsedCreatureLiesThereForSixtySecondsAndThenBecomesACorpseForSixtyMore()
    {
        // Both halves of the owner's pipeline, end to end (Q99, ADR-0063).
        // Until carrying existed the second half could not be built and this
        // test stopped at "and then it is gone".
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        goblin.Health = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.ObserveWith(events);

        // One second short of the rescue window: still lying there, still
        // rescuable.
        sim.Run(Second * 60);
        Assert.Single(world.Creatures);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);

        // The second the window closes it dies - and a Goblin leaves a body, so
        // it is still on the floor, now as a corpse an imp could bury.
        sim.Run(Second);
        Assert.Contains(events.Events, e => e.Kind == "creature_died");
        Assert.Single(world.Creatures);
        Assert.Equal(CreatureActivity.Dead, goblin.Activity);

        // A fresh sixty seconds, not the remains of the first: the two windows
        // share one field and it is put back to zero at the seam.
        sim.Run(Second * 59);
        Assert.Single(world.Creatures);

        sim.Run(Second);
        Assert.Empty(world.Creatures);
        Assert.Contains(events.Events, e => e.Kind == "corpse_rotted");
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
        // CONFIRMED (owner): "imps are dead right away, they don't have a lying
        // state." The discriminator is the GenerateDeadBody flag, which an Imp
        // lacks; OpenKeeper tests the same flag before any unconscious handling,
        // so the rule covers all eight bodyless creatures. Placed on its own lair
        // tile on purpose — an Imp regenerates 67 health a second at home, so a
        // collapse deferred by even one second would heal instead of dying.
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

        CreatureDeathRules.Collapse(new StateHookContext(world, db), state);
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
        Assert.Equal(0, goblin.HungerNeed);
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
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        goblin.Health = 0;
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);

        Assert.True(CreatureDeathRules.Rescue(new StateHookContext(world, db), goblin));
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
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), goblin));

        // Nowhere to carry it to. Its keeper never built a lair.
        Assert.Equal(-1, LairRules.BedTileIndex(world, db, goblin));
        Assert.False(CreatureDeathRules.Rescue(new StateHookContext(world, db), goblin));
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
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, first));
        int held = LairRules.BedTileIndex(world, db, first);

        first.Health = 0;
        var sim = new GameSimulation(
            db, world, 42, [new CreatureSleepSystem(), new CreatureHealthSystem()]);
        sim.Run(Second * 30);

        Assert.Equal(CreatureActivity.Dying, first.Activity);
        Assert.Equal(held, LairRules.BedTileIndex(world, db, first));         // still holds it

        // And the only other tile in a one-tile lair is taken, so a newcomer
        // gets nothing rather than the dying creature's bed.
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Assert.False(LairRules.TryPlaceBedInAnyLair(world, db, second));

        // Death is what frees it - and the body stays on the floor as a
        // corpse for a second sixty seconds (ADR-0063), which is why this
        // asserts the bed rather than the creature's absence.
        sim.Run(Second * 31);
        Assert.Equal(CreatureActivity.Dead, first.Activity);
        Assert.False(LairRules.HasBed(world, db, first));
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, second));
        Assert.Equal(held, LairRules.BedTileIndex(world, db, second));
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
    public void ThreeGoblinsWalkToATwoTileLairTwoBedDownAndTheThirdFindsItFull()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        // Two tiles of lair inside the keeper's own room, so two beds and no
        // more — a lair's capacity is its tile count (Q156).
        Paint(db, world, RoomTypeId.Lair, new TilePos(2, 3), Keeper);
        Paint(db, world, RoomTypeId.Lair, new TilePos(3, 3), Keeper);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 1));
        CreatureState third = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 1));

        var sim = new GameSimulation(db, world, 42, [
            new CreatureMovementSystem(), new CreatureSleepSystem(),
        ]);
        sim.ObserveWith(events);

        // Nobody has a bed on the tick they arrive: a bed is walked to, not
        // claimed from across the room (ADR-0060).
        sim.Run(1);
        Assert.False(LairRules.HasBed(world, db, first));
        Assert.Equal(CreatureActivity.SeekingLair, first.Activity);

        sim.Run(Second * 10);

        // One bed each for the two that got there first, on two different
        // tiles, and nothing for the last — which found the room full on
        // arrival and was told so. Which two is geometry: the middle goblin
        // walks straight while the outer two turn first, and the steering
        // mover carries them at DK2's speeds, so the order is not asserted.
        CreatureState[] bedded = [.. new[] { first, second, third }.Where(g => LairRules.HasBed(world, db, g))];
        Assert.Equal(2, bedded.Length);
        Assert.NotEqual(
            LairRules.BedTileIndex(world, db, bedded[0]),
            LairRules.BedTileIndex(world, db, bedded[1]));
        Assert.Equal(2, world.Objects.Count);
        Assert.Contains(events.Events, e =>
            e.Kind == "lair_attempt_failed" && e.Detail.Contains("reason=lair_full"));
    }

    [Fact]
    public void AWoundedGoblinWalksToTheLairBedsDownSleepsAtOnceHealsAndWakesUp()
    {
        // The whole cycle, through the real systems, with the creature starting
        // wounded so that CreatureSleepsWhenBelowPercentHealth puts it to sleep
        // the moment its bed is down rather than after its 240 awake seconds.
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

        Assert.Contains(events.Events, e => e.Kind == "lair_sought");
        Assert.Contains(events.Events, e => e.Kind == "bed_claimed");
        Assert.Contains(events.Events, e => e.Kind == "fell_asleep");
        Assert.Contains(events.Events, e => e.Kind == "woke_up");

        // It never "went to sleep": it was standing on its bed when the bed
        // went down, and being hurt, it lay down there and then.
        Assert.DoesNotContain(events.Events, e => e.Kind == "going_to_sleep");

        // It woke up WHOLE, not merely better. DK2's state 83 leaves only when the
        // rest need has run out AND health has reached the level-scaled maximum, so
        // nine seconds at 100 a second is not enough from 300 of 1500 and the goblin
        // stays in bed for the twelve it takes (Q403, ADR-0173).
        SimEvent woke = Assert.Single(events.Events, e => e.Kind == "woke_up");
        Assert.Contains("health=1500/1500", woke.Detail);
        Assert.Equal(1500, goblin.Health);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(0, goblin.SleepTicks);
    }

    [Fact]
    public void AWoundedGoblinSleepsPastItsTimeSleepUntilItIsWhole()
    {
        // The refutation half of the rule above, and the thing that changed: the
        // record's TimeSleep is nine seconds, so a time-only wake would have this
        // goblin up and about at t36 with 1200 of 1500. It is still asleep.
        GameDatabase db = Db;
        WorldState world = World(db);

        var bed = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);
        for (int x = 2; x <= 4; x++)
        {
            ref TileState tile = ref world.Tile(x, 4);
            if (tile.TerrainId == Dk2TerrainFixture.LairTerrain) continue;
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, bed);
        goblin.Health = 300;

        var sim = new GameSimulation(db, world, 42, [
            new CreatureMovementSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
        ]);

        // Long enough to be asleep, and long enough for TimeSleep to have elapsed
        // twice over, but not long enough to have healed 1200.
        sim.Run(Second * 11);
        Assert.Equal(CreatureActivity.Sleeping, goblin.Activity);
        Assert.True(goblin.SleepTicks > SimulationTiming.TicksFromSeconds(db.CreatureById(GoblinId).TimeSleep),
            $"slept {goblin.SleepTicks} ticks, expected past TimeSleep");
        Assert.True(goblin.Health < 1500, $"health is {goblin.Health}, expected still short");

        sim.Run(Second * 10);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(1500, goblin.Health);
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

        Assert.Equal(-1, LairRules.BedTileIndex(world, db, goblin));
        Assert.Equal(300, goblin.Health);
        Assert.NotEqual(CreatureActivity.Sleeping, goblin.Activity);
    }

    [Fact]
    public void AWoundedGoblinWithABedWaitsUntilItIsTired()
    {
        // The needs routine's wound branch calls the Sleep job (0x00478023), and with a bed
        // that job routes only while the sleep latch +0x58 is set (0x0046F440): a wound
        // alone does not send a bedded creature to it. TimeAwake is 240 s, so not tired yet.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        var bed = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);
        for (int x = 2; x <= 4; x++)
        {
            ref TileState tile = ref world.Tile(x, 4);
            if (tile.TerrainId == Dk2TerrainFixture.LairTerrain) continue;
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 4));
        LairRules.PlaceBed(world, db, goblin, (bed.Y * world.Width) + bed.X);
        goblin.Health = 300;
        Assert.True(CreatureHealthRules.ShouldSleepFromWounds(db, db.CreatureById(GoblinId), goblin));

        var sim = new GameSimulation(db, world, 42, [
            new CreatureMovementSystem(),
            new CreatureSleepSystem(),
        ]);
        sim.ObserveWith(events);
        sim.Run(Second * 10);

        Assert.DoesNotContain(events.Events, e => e.Kind == "going_to_sleep");
        Assert.NotEqual(CreatureActivity.Sleeping, goblin.Activity);

        // Tired, it goes, wounded or not.
        goblin.AwakeTicks = SimulationTiming.TicksFromSeconds(db.CreatureById(GoblinId).TimeAwake);
        sim.Run(Second * 10);
        Assert.Contains(events.Events, e => e.Kind == "going_to_sleep");
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

        // Two ticks: one to decide on the room it is already standing in, one
        // to test the tile underfoot and put the bed down.
        sim.Run(2);

        Assert.Equal(
            (4 * world.Width) + 4, LairRules.BedTileIndex(world, db, goblin));

        // The lair goes back to plain floor under it.
        RoomRules.Unmake(db, ref world.Tile(bed.X, bed.Y));
        sim.Run(1);

        Assert.Equal(-1, LairRules.BedTileIndex(world, db, goblin));
        Assert.Contains(events.Events, e => e.Kind == "bed_lost");
    }

    [Fact]
    public void ADeadCreaturesBedIsFreedForSomebodyElse()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // One lair tile in the keeper's own room, a walk away.
        var bed = new TilePos(3, 3);
        Paint(db, world, RoomTypeId.Lair, bed, Keeper);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var sim = new GameSimulation(db, world, 42, [
            new CreatureMovementSystem(), new CreatureSleepSystem(), new CreatureHealthSystem(),
        ]);
        sim.Run(Second * 5);
        Assert.True(LairRules.HasBed(world, db, first));

        // It dies, and the one bed in the lair has to become available again, or a
        // lair silently loses a tile every time somebody dies. Zero health is not
        // death: CreatureDyingStateDurationSeconds runs first (Q99) and the bed
        // stays held for that window, because the rescue path carries the creature
        // back to it. A bed is walked to, so the newcomer notices the freed tile
        // at its next idle decision — hence a run longer than the dying window.
        first.Health = 0;
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 3));

        // Sixty seconds of dying, an idle re-evaluation, and the walk onto the freed tile: the
        // steering mover's final approach settles onto the centre over a few ticks, so a
        // sixty-five second window ends with the goblin still approaching.
        sim.Run(Second * 70);

        // Dead, and lying there as a corpse - the bed is what death frees, not
        // the tile the body is on (ADR-0063).
        Assert.Equal(CreatureActivity.Dead, first.Activity);
        Assert.False(LairRules.HasBed(world, db, first));
        Assert.True(
            LairRules.HasBed(world, db, second),
            "the dead creature's bed was never freed");
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
        // Handed a Goblin's bed, because an imp has no bed of its own to be
        // handed: its LairObjectId is 0 and there is no object to place. That
        // is the point of the test — the guard is the worker flag, so an imp
        // sitting in a bed it could never have owned still must not sleep.
        ObjectState bed = world.SpawnObject(
            db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, (4 * world.Width) + 4);
        bed.CreatureId = imp.Id;

        new GameSimulation(db, world, 42, [new CreatureSleepSystem()]).Run(Second * 300);

        Assert.Equal(CreatureActivity.Idle, imp.Activity);
        Assert.Equal(0, imp.AwakeTicks);
    }
}
