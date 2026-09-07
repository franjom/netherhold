// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Levels, which multiply a creature's stats by percentages the shipped data
/// states outright.
///
/// <para>The mechanic exists because of a remark rather than a question. The
/// owner, while trying to time an imp through water: <i>"imps gain speed as they
/// gain more levels, I will try to time one but I have only level 2 as the
/// lowest level imps now in the game"</i>. That sent this project looking, and
/// <c>GlobalVariables.kwd</c>'s 32-stat by 9-level table had been parsed into
/// <c>GlobalVariables.CreatureStats</c> since variables were first read and
/// examined by nothing (Q119).</para>
///
/// <para><b>Earning a level came an hour later</b>, when the owner answered
/// what raises one (Q124): <i>"imps get experience by working... they get exp
/// and passively their level raises. creatures also gain levels through
/// experience, mostly training and fighting."</i> So an imp levels here and
/// nothing else does, because training and fighting do not exist — and the
/// tests below assert that zero as deliberately as they assert the imp's
/// curve.</para>
/// </summary>
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
        // The consequence that would have been a bug: the clamp in Apply reads
        // the levelled maximum, so a level-10 goblin can be healed past 1500.
        // Before this change MaxHealth returned the record's Hp flat and the
        // clamp would have pinned it there.
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
    public void TheOwnersRemark_ALevelTwoImpIsVisiblyFasterThanALevelOneImp()
    {
        // OWNER (2026-09-04): "imps gain speed as they gain more levels."
        //
        // The Speed row is 105% at level 2, so an imp's 7536 raw units a second
        // becomes 7912 — 1.84 tiles a second against 1.93. This is the
        // assertion that makes the owner's remark true in the simulation rather
        // than only in a document, and it is measured through the mover rather
        // than off the rule, so a movement system that ignored the level would
        // fail it.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        Assert.Equal(105, CreatureLevelRules.Percent(db, CreatureStatId.Speed, 2));
        Assert.Equal(7536, CreatureLevelRules.SpeedRaw(db, imp, 1));
        Assert.Equal(7912, CreatureLevelRules.SpeedRaw(db, imp, 2));

        // Through the mover: two imps, same route, one a level higher. The
        // fixture's walkable room is (1,1)-(3,3), so the walk is short and the
        // faster one must be strictly further along after the same ticks.
        WorldState world = World(db);
        CreatureState slow = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        CreatureState fast = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        fast.Level = 2;

        foreach (CreatureState c in new[] { slow, fast })
        {
            c.Path.Clear();
            c.Path.Add(new TilePos(2, 1));
            c.Path.Add(new TilePos(3, 1));
            c.PathIndex = 0;
        }

        new GameSimulation(db, world, 42, [new CreatureMovementSystem()]).Run(Second / 2);

        Assert.True(fast.PosX > slow.PosX,
            $"level 2 imp at {fast.PosX} should be ahead of level 1 at {slow.PosX}");
    }

    [Fact]
    public void TheExperienceThresholdIsTheRecordsOwnNumberAtLevelOne()
    {
        // Our reading of an ambiguity, asserted so the choice is visible: the
        // table is indexed by the creature's CURRENT level, which makes the
        // first level cost exactly the record's 3000. Indexed by the target
        // level it would cost 3900 and the record's own number would appear
        // nowhere. CreatureExperienceSystem depends on this reading now, so it
        // is a live choice rather than a dormant one.
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);

        Assert.Equal(3000, goblin.ExpForNextLevel);
        Assert.Equal(3000, CreatureLevelRules.ExperienceForNextLevel(db, goblin, 1));
    }

    // ---- earning a level (Q124) -------------------------------------------

    [Fact]
    public void TheOwnersRule_AnImpEarnsExperienceOnlyWhileItIsWorking()
    {
        // OWNER (2026-09-04, Q124): "imps get experience by working, while they
        // do their usual things around the dungeon, digging, reinforcing walls,
        // mining gold, etc. they get exp and passively their level raises."
        //
        // Both halves are asserted, and the second is the one that makes it a
        // rule rather than a trickle: an idle imp earns nothing at all.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState worker = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        CreatureState idler = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 3));

        worker.Activity = CreatureActivity.Working;
        idler.Activity = CreatureActivity.Idle;

        new GameSimulation(db, world, 42, [new CreatureExperienceSystem()]).Run(Second * 10);

        // 20 A TICK, NOT A SECOND, and DK2's tick rather than ours. The shipped
        // name is ImpExperienceGainPerSecond and it is wrong by a factor of four
        // (Q79, 2026-09-07): the variable is read at nine sites in the imp's
        // task unit, every one of them awards it raw with no divide by variable
        // 19, and every one sits beside a work accrual that happens once an
        // original tick. So ten game seconds is forty awards, not ten.
        Assert.Equal(10 * 20 * SimulationTiming.OriginalTicksPerGameSecond,
                     worker.ExperiencePoints);
        Assert.Equal(800, worker.ExperiencePoints);
        Assert.Equal(0, idler.ExperiencePoints);
        Assert.Equal(1, worker.Level);                   // 3000 buys the level, not 800
    }

    [Fact]
    public void AnImpThatWorksForThirtySevenAndAHalfSecondsReachesLevelTwoAndSpeedsUp()
    {
        // THIS TEST SAID 150 SECONDS FOR THREE WEEKS AND THE ANSWER IS 37.5.
        // The old arithmetic was 20 a second against the record's
        // ExpForNextLevel of 3000. The rate is 20 an ORIGINAL TICK (Q79), which
        // is 80 a game second, so the second level costs 3000 / 80 = 37.5
        // seconds of work and lands mid-second - the 150th award falls on sim
        // tick 749. And the level is not bookkeeping: it moves the creature
        // faster, which is the thing the owner noticed in the first place
        // ("imps gain speed as they gain more levels").
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);

        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        state.Activity = CreatureActivity.Working;
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, 42, [new CreatureExperienceSystem()]);
        sim.ObserveWith(events);

        // Thirty-seven seconds is 148 awards - two short - and it is still
        // level 1 with the record's speed.
        sim.Run(Second * 37);
        Assert.Equal(1, state.Level);
        Assert.Equal(148 * 20, state.ExperiencePoints);
        Assert.Equal(7536, CreatureLevelRules.SpeedRaw(db, imp, state.Level));

        // Two more ticks carries the last two awards - one an award, now that
        // a tick is one of the original's - and the 150th lands exactly on 3000.
        sim.Run(2);
        Assert.Equal(2, state.Level);
        Assert.Equal(7912, CreatureLevelRules.SpeedRaw(db, imp, state.Level));

        // Zero because the balance is ZEROED on promotion, not because 3000
        // came off a total of exactly 3000 - and here the two happen to agree,
        // which is why the discard needed its own test.
        Assert.Equal(0, state.ExperiencePoints);

        // And the event says what it cost and what the next one costs, so a
        // log is enough to follow a creature up the curve.
        SimEvent up = Assert.Single(events.Events, e => e.Kind == "creature_level_up");
        Assert.Contains("1->2", up.Detail);
        Assert.Contains("next_at=3900", up.Detail);   // 130% of 3000
    }

    [Fact]
    public void ACreatureThatCannotTrainOrFightNeverLevelsAtAll()
    {
        // The honest zero. The owner: creatures "gain levels through experience,
        // mostly training and fighting" -- and this simulation has neither, so a
        // Goblin stays at level 1 for ever however long it stands there.
        //
        // Asserted rather than left implicit, because the tempting fix is to
        // give everything the imp's rate and call it progression, which would
        // invent a curve DK2 does not grant for existing.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Activity = CreatureActivity.Working;   // even so

        new GameSimulation(db, world, 42, [new CreatureExperienceSystem()]).Run(Second * 600);

        Assert.Equal(0, goblin.ExperiencePoints);
        Assert.Equal(1, goblin.Level);
    }

    [Fact]
    public void LevellingStopsAtTenBecauseTheTableStopsThere()
    {
        // The cap, and the reason it is ten: the shipped stat table has no rows
        // past level 10. The owner's own caps are lower and belong to rooms
        // rather than to the creature -- training to 4, the combat pit to 8, and
        // "to get to level 10 they need to fight actual fights" -- and both room
        // numbers are shipped variables (Q124). Neither room exists here, so
        // this asserts the outer ceiling only.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
        state.Activity = CreatureActivity.Working;
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
        // THIS TEST USED TO ASSERT THE OPPOSITE, and the opposite was wrong.
        // Until Q205 this project looped the promotion and carried the
        // remainder, on the reasoning that a single-step version "would silently
        // drop the second level the day combat pays out in lumps". DK2 drops it.
        // Its adder promotes once, sets the balance to zero, and returns - so
        // the overshoot is gone and two levels in one award are not possible.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        // 3000 buys level 2 and 3900 would buy level 3, so 6900 was the number
        // that "bought both exactly" under the old reading.
        Assert.True(CreatureLevelRules.Grant(db, imp, state, 6900));

        Assert.Equal(2, state.Level);
        Assert.Equal(0, state.ExperiencePoints);

        // And the discarded 3900 is really gone: the next level costs its full
        // price all over again.
        Assert.Equal(3900, CreatureLevelRules.ExperienceForNextLevel(db, imp, 2));
    }

    [Fact]
    public void LandingExactlyOnTheThresholdPromotes()
    {
        // The original's comparison is "balance < needed -> stop", so equality
        // promotes. One off either way is the whole difference between a
        // creature that levels on the second it earns its last point and one
        // that levels a tick later.
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        state.ExperiencePoints = 2999;
        Assert.False(CreatureLevelRules.Grant(db, imp, state, 0));
        Assert.Equal(1, state.Level);

        Assert.True(CreatureLevelRules.Grant(db, imp, state, 1));
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

        Assert.False(CreatureLevelRules.Grant(db, imp, state, -50));
        Assert.Equal(10, state.ExperiencePoints);

        // A loss that fits is taken in full.
        Assert.False(CreatureLevelRules.Grant(db, imp, state, -10));
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
        Assert.False(CreatureLevelRules.Grant(db, zero, state, 10_000));
        Assert.Equal(500, state.ExperiencePoints);
        Assert.Equal(1, state.Level);

        Assert.False(CreatureLevelRules.Grant(db, zero, state, -100));
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
