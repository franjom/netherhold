// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Levels multiply a creature's stats by percentages the shipped data
/// states outright: <c>GlobalVariables.kwd</c>'s 32-stat by 9-level table, read
/// into <c>GlobalVariables.CreatureStats</c> (Q119).
/// </summary>
/// <remarks>
/// Only an imp levels here. What raises a level is CONFIRMED (owner,
/// 2026-09-04, Q124): imps earn experience by working, and other creatures
/// level "mostly through training and fighting", neither of which this
/// simulation has. The tests below assert that zero as deliberately as they
/// assert the imp's curve.
/// </remarks>
public class CreatureLevelTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    // ---- the table itself -------------------------------------------------

    [Fact]
    public void LevelOneIsAlwaysAHundredPercent()
    {
        // The property that makes this safe to introduce at all: every creature
        // in the project is level 1 today, so every scaled stat must come back
        // exactly as the record's. The table has no level-1 rows — it starts at
        // 2 — and this asserts the implicit 100 rather than trusting it.
        GameDatabase db = Db;

        foreach (CreatureStatId stat in Enum.GetValues<CreatureStatId>())
            Assert.Equal(100, CreatureLevelRules.Percent(db, stat, 1));
    }

    [Fact]
    public void AStatTheTableDoesNotScaleStaysAtAHundredForever()
    {
        // Ten of the 32 stats are 100% at every level, and own-land
        // regeneration is one of them: an Imp heals at 67 a second at level 1
        // and at level 10. A rule that assumed "levels scale everything" would
        // be wrong about a third of the table.
        GameDatabase db = Db;

        for (int level = 1; level <= CreatureLevelRules.MaxLevel; level++)
        {
            Assert.Equal(100, CreatureLevelRules.Percent(
                db, CreatureStatId.OwnLandHealthIncreasePerSecond, level));
        }
    }

    [Fact]
    public void ALevelAboveTheTableIsClampedRatherThanReturningZero()
    {
        // A snapshot from another build, or a scenario asking for level 12,
        // must not silently produce a creature with no health. Level 11 reads
        // as level 10.
        GameDatabase db = Db;

        Assert.Equal(
            CreatureLevelRules.Percent(db, CreatureStatId.Health, CreatureLevelRules.MaxLevel),
            CreatureLevelRules.Percent(db, CreatureStatId.Health, 99));

        Assert.Equal(
            CreatureLevelRules.Percent(db, CreatureStatId.Health, 1),
            CreatureLevelRules.Percent(db, CreatureStatId.Health, 0));
    }

    // ---- what it does to a creature ---------------------------------------

    [Fact]
    public void MaxHealthScalesWithLevelAndTheGoblinCurveIsTheShippedOne()
    {
        // A Goblin's record says 1500. The Health row says 125% at level 2 and
        // 600% at level 10, so the same goblin ranges from 1500 to 9000.
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.Equal(1, state.Level);
        Assert.Equal(1500, CreatureHealthRules.MaxHealth(db, goblin, state));

        state.Level = 2;
        Assert.Equal(1875, CreatureHealthRules.MaxHealth(db, goblin, state));

        state.Level = 10;
        Assert.Equal(9000, CreatureHealthRules.MaxHealth(db, goblin, state));
    }

    [Fact]
    public void HealingCannotExceedTheLevelledMaximumRatherThanTheRecordsOne()
    {
        // The clamp in Apply reads the LEVELLED maximum, so a level-10 goblin
        // can be healed past the record's 1500.
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        state.Level = 10;
        state.Health = 1500;

        Assert.Equal(2000, CreatureHealthRules.Apply(db, goblin, state, 2000));
        Assert.Equal(3500, state.Health);
    }

    [Fact]
    public void ALevelTwoImpWalksAtTheLevelOneSpeed()
    {
        // ADR-0518: 0x004887D0 takes the speed from the definition every creature of a type
        // shares (+0x370, then +0x5B4), and nothing reads the Speed row. The Level 4 capture
        // shows Imps of levels 1 to 8 at 30144 (0.46 tiles a tick) or the timed boost's 45216,
        // never a level's 105%. The owner's 2026-09-04 remark that levelled Imps are faster is
        // refused by the capture. Measured through the mover, so one that scaled by level fails.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        Assert.Equal(105, CreatureLevelRules.Percent(db, CreatureStatId.Speed, 2));
        Assert.Equal(CreatureMovementSystem.MaxSpeedOf(imp), (SimulationTiming.LoaderRatePerTick(7536)) << 4);

        // Two imps, same route, one a level higher. The fixture's walkable room
        // is (1,1)-(3,3), so the walk is short and the faster one must be
        // strictly further along after the same ticks.
        WorldState world = World(db);
        CreatureState slow = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        CreatureState fast = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        fast.Level = 2;

        var steering = new Navigation.WorldSteering(db, world);
        foreach (CreatureState c in new[] { slow, fast })
            Assert.True(steering.TryRoute(c, new TilePos(3, 1), Navigation.MoveOptions.Loose));

        new GameSimulation(db, world, 42, [new CreatureMovementSystem()]).Run(Second / 2);

        Assert.Equal((slow.PosX, slow.PosY), (fast.PosX, fast.PosY));
    }

    [Fact]
    public void TheExperienceThresholdIsTheRecordsOwnNumberAtLevelOne()
    {
        // Our reading of an ambiguity, asserted so the choice stays visible:
        // the table is indexed by the creature's CURRENT level, which makes the
        // first level cost exactly the record's 3000. Indexed by the target
        // level it would cost 3900 and the record's own number would appear
        // nowhere. CreatureExperienceSystem depends on this reading.
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);

        Assert.Equal(3000, goblin.ExpForNextLevel);
        Assert.Equal(3000, CreatureLevelRules.ExperienceForNextLevel(db, goblin, 1));
    }

    // ---- earning a level (Q124) -------------------------------------------

    [Fact]
    public void TheOwnersRule_AnImpEarnsExperienceForEachWorkHit()
    {
        // CONFIRMED (owner, 2026-09-04, Q124): "imps get experience by working". DK2 pays the raw
        // ImpExperienceGainPerSecond, 20, once per work action whose tile test passes (the nine
        // readers are the action callbacks, dig 0x00496E00 first), not once per tick of work
        // (ADR-0386). Ten seconds standing in a work state pay nothing; one dig hit pays 20; a hit
        // on impenetrable rock is refused and pays nothing; an idle imp earns nothing.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState worker = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        CreatureState idler = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        StatePlacement.As(worker, CreatureActivity.Working);
        StatePlacement.As(idler, CreatureActivity.Idle);

        world.QueueWork(new TileWorkOrder((2 * world.Width) + 4, TileWorkKind.Dig, Keeper, worker.Id));
        world.QueueWork(new TileWorkOrder(2 * world.Width, TileWorkKind.Dig, Keeper, worker.Id));
        new GameSimulation(db, world, 42, [new TileWorkSystem(), new CreatureExperienceSystem()]).Run(Second * 10);

        Assert.Equal(20, worker.ExperiencePoints);
        Assert.Equal(0, idler.ExperiencePoints);
        Assert.Equal(1, worker.Level);
    }

    [Fact]
    public void TheHitThatReachesThreeThousandBuysLevelTwo()
    {
        // 3000 buys the second level: 150 hits at 20 (ADR-0386). The balance is zeroed on
        // promotion, not reduced, so the overshoot is gone.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        state.ExperiencePoints = 2980;
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 42, [new TileWorkSystem(), new CreatureExperienceSystem()]);
        sim.ObserveWith(events);

        world.QueueWork(new TileWorkOrder((2 * world.Width) + 4, TileWorkKind.Dig, Keeper, state.Id));
        sim.Run(1);

        Assert.Equal(2, state.Level);
        Assert.Equal(0, state.ExperiencePoints);
        SimEvent up = Assert.Single(events.Events, e => e.Kind == "creature_level_up");
        Assert.Contains("1->2", up.Detail);
        Assert.Contains("next_at=3900", up.Detail);   // 130% of 3000
    }

    [Fact]
    public void ACreatureThatCannotTrainOrFightNeverLevelsAtAll()
    {
        // The honest zero. Creatures "gain levels through experience, mostly
        // training and fighting" (owner, Q124) and this simulation has neither,
        // so a Goblin stays at level 1 for ever. Asserted rather than left
        // implicit: giving everything the imp's rate would invent a curve DK2
        // does not grant for merely existing.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        StatePlacement.As(goblin, CreatureActivity.Working);   // even so

        new GameSimulation(db, world, 42, [new CreatureExperienceSystem()]).Run(Second * 600);

        Assert.Equal(0, goblin.ExperiencePoints);
        Assert.Equal(1, goblin.Level);
    }

    [Fact]
    public void LevellingStopsAtTenBecauseTheTableStopsThere()
    {
        // The cap is ten because the shipped stat table has no rows past level
        // 10. The owner's caps are lower and belong to rooms rather than to the
        // creature -- training to 4, the combat pit to 8, "to get to level 10
        // they need to fight actual fights" -- and both room numbers are
        // shipped variables (Q124). Neither room exists here.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        StatePlacement.As(state, CreatureActivity.Working);
        state.Level = CreatureLevelRules.MaxLevel;

        new GameSimulation(db, world, 42, [new CreatureExperienceSystem()]).Run(Second * 300);

        Assert.Equal(CreatureLevelRules.MaxLevel, state.Level);

        // And it stops *earning*, not just stops promoting -- experience that
        // could never be spent would be a number growing for no reason.
        Assert.Equal(0, state.ExperiencePoints);
    }

    [Fact]
    public void EnoughExperienceInOneGoBuysONELevelAndThrowsTheRestAway()
    {
        // DK2's adder promotes ONCE, sets the balance to zero and returns, so
        // the overshoot is discarded and two levels in one award are not
        // possible (Q205). The tempting reading -- loop and carry the remainder
        // -- is what this test asserted before Q205 settled it.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        // 3000 buys level 2 and 3900 would buy level 3, so 6900 was the number
        // that "bought both exactly" under the old reading.
        Assert.True(CreatureLevelRules.Grant(new StateHookContext(world, db), imp, state, 6900));

        Assert.Equal(2, state.Level);
        Assert.Equal(0, state.ExperiencePoints);

        // And the discarded 3900 is really gone: the next level costs its full
        // price all over again.
        Assert.Equal(3900, CreatureLevelRules.ExperienceForNextLevel(db, imp, 2));
    }

    /// <summary>An earned level goes through DK2's one level increment, <c>0x0048B120</c>
    /// (<c>0x004579D4</c>), so health keeps its share of the new maximum (Q427).</summary>
    [Fact]
    public void AnEarnedLevelCarriesHealthUpWithTheMaximum()
    {
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        int oldMax = CreatureHealthRules.MaxHealth(db, imp, state);
        state.Health = oldMax / 2;

        Assert.True(CreatureLevelRules.Grant(new StateHookContext(world, db), imp, state, 3000));

        int newMax = CreatureHealthRules.MaxHealth(db, imp, state);
        Assert.True(newMax > oldMax);
        Assert.Equal((int)(((((long)(oldMax / 2)) << 12) / oldMax * newMax) >> 12), state.Health);
    }

    [Fact]
    public void LandingExactlyOnTheThresholdPromotes()
    {
        // The original's comparison is "balance < needed -> stop", so equality
        // promotes: one off either way decides whether a creature levels on the
        // tick it earns its last point or a tick later.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        state.ExperiencePoints = 2999;
        Assert.False(CreatureLevelRules.Grant(new StateHookContext(world, db), imp, state, 0));
        Assert.Equal(1, state.Level);

        Assert.True(CreatureLevelRules.Grant(new StateHookContext(world, db), imp, state, 1));
        Assert.Equal(2, state.Level);
        Assert.Equal(0, state.ExperiencePoints);
    }

    [Fact]
    public void ALossBiggerThanTheBalanceIsRefusedRatherThanClamped()
    {
        // DK2 subtracts only when the magnitude fits, so experience floors at
        // zero by DECLINING the subtraction. A creature on ten points handed
        // minus fifty keeps its ten - it does not drop to zero. The distinction
        // exists because DK2 ships a per-second experience drain
        // (EPLossSecondWhileNotTraining) whose rate happens to be zero, and if a
        // mod ever turns it on this is the arithmetic it will use.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        state.ExperiencePoints = 10;

        Assert.False(CreatureLevelRules.Grant(new StateHookContext(world, db), imp, state, -50));
        Assert.Equal(10, state.ExperiencePoints);

        // A loss that fits is taken in full.
        Assert.False(CreatureLevelRules.Grant(new StateHookContext(world, db), imp, state, -10));
        Assert.Equal(0, state.ExperiencePoints);

        // And the shipped drain is zero, which is why none of this is visible
        // in a normal game.
        Assert.Equal(0, TrainingRules.ExperienceLostPerSecondWhileNotTraining(db));
    }

    [Fact]
    public void AThresholdOfZeroRefusesEverythingIncludingLosses()
    {
        // The original computes the level's threshold FIRST and returns without
        // touching the balance when it is zero. So ExpForNextLevel is not merely
        // the price of a level, it is the switch that makes a creature capable
        // of having experience at all.
        GameDatabase db = Db;
        CreatureDef zero = db.CreatureById(Dk2TerrainFixture.ImpCreatureId)
            with { ExpForNextLevel = 0 };
        var state = new CreatureState { Level = 1, ExperiencePoints = 500 };

        Assert.Equal(0, zero.ExpForNextLevel);
        Assert.False(CreatureLevelRules.Grant(DetachedHooks.Context, zero, state, 10_000));
        Assert.Equal(500, state.ExperiencePoints);
        Assert.Equal(1, state.Level);

        Assert.False(CreatureLevelRules.Grant(DetachedHooks.Context, zero, state, -100));
        Assert.Equal(500, state.ExperiencePoints);
    }

    [Fact]
    public void LevelSurvivesASnapshotRoundTrip()
    {
        // Level is hashed state, so it has to be in the snapshot too — and a
        // field that is hashed but not saved makes a restored run diverge from
        // the one it claims to continue.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        state.Level = 7;

        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        byte[] bytes = WorldSnapshot.ToBytes(sim);

        GameSimulation restored = WorldSnapshot.FromBytes(bytes, db);

        Assert.Equal(7, restored.World.Creatures[0].Level);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }
}
