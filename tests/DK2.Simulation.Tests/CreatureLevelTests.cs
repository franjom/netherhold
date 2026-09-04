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
/// <para><b>Gaining a level is deliberately not implemented</b> — the
/// experience rate's unit is ambiguous (Q124) — so these tests set the level
/// directly. That is the honest shape: the scaling is certain and the levelling
/// is not.</para>
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
        // nowhere. Nothing reads this yet (Q124).
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);

        Assert.Equal(3000, goblin.ExpForNextLevel);
        Assert.Equal(3000, CreatureLevelRules.ExperienceForNextLevel(db, goblin, 1));
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
