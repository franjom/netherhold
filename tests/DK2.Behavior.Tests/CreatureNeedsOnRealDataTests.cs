// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// Health and hunger against the shipped v1.70 install.
///
/// <para>Two jobs, as usual. The drift guard re-reads every number
/// <c>Dk2TerrainFixture</c> transcribed — and it has already earned its keep
/// twice on this record: the Imp's hunger rate was once wrong by a factor of
/// 4096, and its own-land healing was transcribed as 0 when the install says
/// 67.</para>
///
/// <para>The more interesting job is the campaign-wide claims. Several sentences
/// in <see cref="CreatureHealthRules"/> are about <i>all 46 creatures</i> rather
/// than about one: that 32767 is a general "never" sentinel and not the Imp's
/// quirk; that hunger and sleep are switched off independently; that the two
/// encodings of "never hungry" agree everywhere; that own-land regeneration is
/// real but almost unused. Each is checked here, because a claim nothing checks
/// may already be false.</para>
///
/// <para>Skips when no installation is configured (DK2_DIR).</para>
/// </summary>
public class CreatureNeedsOnRealDataTests
{
    private static string? Install() => Dk2Install.Find();

    private const int Never = CreatureHealthRules.Never;

    private static int Seconds(Fp32 value) => value.Raw / Fp32.One;

    // ---- the drift guard --------------------------------------------------

    [Fact]
    public void TheImpsNeedsAreWhatTheFixtureSaysTheyAre()
    {
        string? install = Install();
        if (install is null) return;

        CreatureDef imp = GameDatabaseLoader.Load(install).CreatureByName("Imp")!;

        Assert.Equal(500, imp.Hp);
        Assert.Equal(0, imp.HpFromChicken);
        Assert.Equal(0, imp.HungerFill);
        Assert.Equal(Never, Seconds(imp.HungerRate));
        Assert.Equal(Never, imp.TimeAwake);

        // The one the fixture had wrong. Only two creatures in the game carry a
        // non-zero value here and the Imp is one of them.
        Assert.Equal(67, imp.OwnLandHealthIncreasePerSecond);
    }

    [Fact]
    public void TheGoblinsNeedsAreWhatTheFixtureSaysTheyAre()
    {
        string? install = Install();
        if (install is null) return;

        CreatureDef goblin = GameDatabaseLoader.Load(install).CreatureById(7);

        Assert.Equal(1500, goblin.Hp);
        Assert.Equal(180, Seconds(goblin.HungerRate));
        Assert.Equal(1, goblin.HungerFill);
        Assert.Equal(375, goblin.HpFromChicken);
        Assert.Equal(240, goblin.TimeAwake);
        Assert.Equal(9, goblin.TimeSleep);
        Assert.Equal(200, goblin.DecomposeValue);
        Assert.Equal(0, goblin.OwnLandHealthIncreasePerSecond);
    }

    [Fact]
    public void TheFourNeedsVariablesAreWhatTheFixtureSaysTheyAre()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(100, db[VariableTypeId.ModifyHealthOfCreatureInLairPerSecond]);
        Assert.Equal(20, db[VariableTypeId.CreatureCriticalHealthPercentageOfMax]);
        Assert.Equal(50, db[VariableTypeId.CreatureSleepsWhenBelowPercentHealth]);
        Assert.Equal(60, db[VariableTypeId.CreatureDyingStateDurationSeconds]);
    }

    // ---- claims about all 46 creatures ------------------------------------

    [Fact]
    public void EveryCreatureHasHealth()
    {
        string? install = Install();
        if (install is null) return;

        // The invariant behind WorldState.SpawnCreature taking the database: a
        // creature seeded from a zero Hp would die on the tick it arrived. If
        // any record ever reads zero here, that constructor needs a different
        // answer rather than a silent corpse.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(46, db.Creatures.Count);
        Assert.All(db.Creatures.Values, c => Assert.True(c.Hp > 0, $"{c.Name} has {c.Hp} hp"));
    }

    [Fact]
    public void EightCreaturesNeverGetHungryAndTheyAreTheUndeadTheConstructsAndTheWorker()
    {
        string? install = Install();
        if (install is null) return;

        // The undead (Vampire, Skeleton and their uniques), the constructs
        // (Stone Knight, Horny), the worker (Imp) — and the Unique Firefly,
        // which is the odd one out, because the ordinary Firefly eats two
        // chickens. Whether that is deliberate or an editing slip in EA's data
        // is UNKNOWN and it does not matter: the rule reads the record.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var never = db.Creatures.Values
            .Where(c => !CreatureHealthRules.CanGetHungry(c))
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "Imp", "Vampire", "Skeleton", "Horny", "Stone Knight",
                "Unique Firefly", "Unique Skeleton", "Unique Vampire",
            },
            never);
    }

    [Fact]
    public void HungerAndSleepAreSwitchedOffIndependentlyAndBothDirectionsOccur()
    {
        string? install = Install();
        if (install is null) return;

        // This is what proves 32767 is a per-need sentinel rather than one
        // "undead" switch, and it is the answer to an open question that had
        // stood since creatures were parsed ("is it an Imp quirk?").
        //
        // Both directions exist in the shipped data, which is the strong form:
        //   - the two Vampires and the Unique Firefly sleep but never eat
        //   - the Dwarf eats but never sleeps
        // So neither set contains the other, and no single flag could produce
        // this. Written as a subset assertion first, and the Dwarf is what
        // refuted it.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var sleepless = db.Creatures.Values
            .Where(c => c.TimeAwake >= Never).Select(c => c.Name).ToHashSet();
        var starveless = db.Creatures.Values
            .Where(c => !CreatureHealthRules.CanGetHungry(c)).Select(c => c.Name).ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "Imp", "Skeleton", "Dwarf", "Horny", "Stone Knight", "Unique Skeleton",
            },
            sleepless);

        // Neither is a subset of the other.
        Assert.NotEmpty(sleepless.Except(starveless));     // the Dwarf
        Assert.NotEmpty(starveless.Except(sleepless));      // the Vampires, Unique Firefly
        Assert.Contains("Dwarf", sleepless.Except(starveless));
        Assert.Contains("Vampire", starveless.Except(sleepless));
    }

    [Fact]
    public void TheTwoEncodingsOfNeverHungryAgreeOnEveryCreature()
    {
        string? install = Install();
        if (install is null) return;

        // CreatureHealthRules.CanGetHungry tests HungerFill > 0. The record also
        // says "never" a second way, by setting HungerRate to 32767. Nothing
        // would notice if those two ever disagreed — so this notices.
        GameDatabase db = GameDatabaseLoader.Load(install);

        foreach (CreatureDef c in db.Creatures.Values)
        {
            bool byFill = c.HungerFill == 0;
            bool byRate = Seconds(c.HungerRate) >= Never;
            Assert.Equal(byFill, byRate);
        }
    }

    [Fact]
    public void TheVampireSleepsButNeverEats()
    {
        string? install = Install();
        if (install is null) return;

        // The single record that proves 32767 is a per-need sentinel rather than
        // one "undead" switch, which is what closed the old open question about
        // whether it was an Imp quirk. The Imp and the Skeleton have it for both
        // needs; the Vampire has it for hunger only.
        GameDatabase db = GameDatabaseLoader.Load(install);

        CreatureDef vampire = db.CreatureByName("Vampire")!;
        Assert.Equal(Never, Seconds(vampire.HungerRate));
        Assert.Equal(330, vampire.TimeAwake);
        Assert.Equal(9, vampire.TimeSleep);

        CreatureDef skeleton = db.CreatureByName("Skeleton")!;
        Assert.Equal(Never, Seconds(skeleton.HungerRate));
        Assert.Equal(Never, skeleton.TimeAwake);
    }

    [Fact]
    public void OnlyTwoCreaturesInTheGameHealOnTheirOwnLand()
    {
        string? install = Install();
        if (install is null) return;

        // The zeroes are the finding, the same shape as mana's three terrain
        // types out of 41 (ADR-0017). Own-land regeneration is a real
        // data-driven rule and a no-op for 44 of the 46, so a rule written from
        // the Imp alone would read as general and be wrong about the game.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var healers = db.Creatures.Values
            .Where(c => c.OwnLandHealthIncreasePerSecond != 0)
            .ToDictionary(c => c.Name, c => (int)c.OwnLandHealthIncreasePerSecond);

        Assert.Equal(
            new Dictionary<string, int> { ["Imp"] = 67, ["King Reginald"] = 400 },
            healers);
    }

    [Fact]
    public void AChickenDoesNotHaveToHealTheCreatureThatEatsIt()
    {
        string? install = Install();
        if (install is null) return;

        // The Firefly eats two chickens and gains nothing from either. So
        // HpFromChicken cannot be treated as "the reason a creature eats", and
        // CreatureHealthRules.Eat is right to count hunger down separately from
        // applying health.
        GameDatabase db = GameDatabaseLoader.Load(install);

        CreatureDef firefly = db.CreatureByName("Firefly")!;
        Assert.Equal(2, firefly.HungerFill);
        Assert.Equal(0, firefly.HpFromChicken);
        Assert.True(CreatureHealthRules.CanGetHungry(firefly));
    }

    [Fact]
    public void OneCreatureIsHealedByMoreThanItsWholeHealthByASingleChicken()
    {
        string? install = Install();
        if (install is null) return;

        // Jack: 240 health, and one chicken is worth 250 of it. This test was
        // written the other way round — asserting that no creature could be
        // over-healed — and the install said otherwise, which is the whole
        // reason it exists.
        //
        // The consequence is that **the clamp in Apply is load-bearing for
        // correctness, not tidiness**. Jack eats two chickens by its record, so
        // without the clamp it would finish a meal above its own maximum, and
        // anything later keyed off "health == max" would never fire for it.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var overHealed = db.Creatures.Values
            .Where(c => c.HpFromChicken > c.Hp)
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "Jack" }, overHealed);

        CreatureDef jack = db.CreatureByName("Jack")!;
        Assert.Equal(240, jack.Hp);
        Assert.Equal(250, jack.HpFromChicken);

        // And the clamp holds it, which is the part that matters.
        var state = new CreatureState
        {
            Id = 1,
            CreatureTypeId = jack.CreatureId,
            OwnerPlayerId = 3,
            Health = 1,
            HungerNeed = 2,
        };

        CreatureHealthRules.Eat(db, jack, state);

        Assert.Equal(240, state.Health);
    }

    // ---- eating (ADR-0067) ------------------------------------------------

    [Fact]
    public void TheBileDemonsHungerNumbersAreWhatTheFixtureSaysTheyAre()
    {
        // The fixture grew a Bile Demon for one number: hungerFill 5, the only
        // value in the game that tells the two ways of paying off hunger apart.
        string? install = Install();
        if (install is null) return;

        CreatureDef demon = GameDatabaseLoader.Load(install).CreatureByName("Bile Demon")!;

        Assert.Equal(3, demon.CreatureId);
        Assert.Equal(6000, demon.Hp);
        Assert.Equal(200, demon.HpFromChicken);
        Assert.Equal(5, demon.HungerFill);
        Assert.Equal(220, Seconds(demon.HungerRate));
        Assert.Equal(270, demon.TimeAwake);
        Assert.Equal(9, demon.TimeSleep);
    }

    [Fact]
    public void HungerFillIsOneForNearlyEverybodyAndFiveIsTheOutlier()
    {
        // What makes the accumulate-and-pay-one rule visible at all. If every
        // creature needed one chicken, "each bird pays off one" and "each bird
        // fills you up" would be the same sentence and the prison's difference
        // would be unobservable.
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        var fills = db.Creatures.Values
            .Where(c => c.HungerFill > 0)
            .GroupBy(c => (int)c.HungerFill)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.True(fills.ContainsKey(5), "no creature needs five chickens");
        Assert.True(fills.Keys.Max() >= 2, "no creature needs more than one chicken");
    }

    [Fact]
    public void CreatureStatRowNineNeverScalesAnythingInTheShippedTable()
    {
        // EatingRules puts HungerFill through stat row 9 because the original
        // does. This is the test that says the scaling is invisible in the
        // shipped data — so if it ever stops being invisible, it is because
        // somebody changed the table and not because we invented a curve.
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        for (int level = 1; level <= CreatureLevelRules.MaxLevel; level++)
        {
            Assert.Equal(
                100, CreatureLevelRules.Percent(db, CreatureStatId.HungerFill, level));
        }
    }

    [Fact]
    public void TwoObjectsCarryTheFoodFlagAndOnlyOneOfThemIsTheChicken()
    {
        // The original's two food searches do not agree on their test: the
        // hatchery matches the object id 9, the prison matches TypeFood. That
        // is only a difference if more than one object carries the flag — and
        // this is where the repo said "only the Chicken carries TypeFood" until
        // 2026-09-06, when somebody counted.
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        var food = db.Objects.Values.Where(o => o.IsFood).ToList();

        Assert.Equal(2, food.Count);
        Assert.Contains(food, o => o.ObjectId == HatcheryRules.ChickenObjectId);
        Assert.Contains(food, o => o.Name.Contains("Pendulum"));
    }

    // ---- beds ------------------------------------------------------------

    [Fact]
    public void TwelveCreaturesNeverGoForABedAndTheyAreTheOnesWithNowhereToLiveOrNoSleepToSleep()
    {
        string? install = Install();
        if (install is null) return;

        // The gate has two halves (Q104, read from the binary 2026-09-05): a
        // bed id, and a TimeSleep that is not zero.
        //
        // LairObjectId is the bed object itself, and 0 means there is none.
        // The owner CONFIRMED the two interesting members: "skeleton does not
        // have a lair", and "horny is only a summonable creature, it is not in
        // a regular roster of creatures, and only on special conditions/maps
        // when conditions are met" — which is also why Horny carries no
        // AvailableViaPortal (ADR-0047). The other seven fall out the same
        // way: the Imp lives nowhere, and the five hero lords plus Jack come
        // through hero gates rather than living in anybody's dungeon.
        //
        // The Dwarf and the Stone Knight are the other half: each carries the
        // hero bed (6) and a TimeSleep of 0, and the original never sends
        // either for a bed. See the test below.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var bedless = db.Creatures.Values
            .Where(c => !LairRules.NeedsBed(c))
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "Imp", "Skeleton", "Unique Skeleton", "Horny", "Jack",
                "Prince Tristran", "King Reginald", "Prince Felix",
                "Lord Of The Land", "Prince Balder",
                "Dwarf", "Stone Knight",
            },
            bedless);
    }

    [Fact]
    public void TheDwarfAndTheStoneKnightCarryABedIdAndAreNeverSentForOne()
    {
        string? install = Install();
        if (install is null) return;

        // Both carry a bed object *and* a TimeAwake of 32767 with a TimeSleep
        // of 0, so by the bed id alone they would each take a lair tile and
        // never sleep on it. Q104 asked which field DK2 gates on; the binary
        // says both — the lair attempt requires TimeSleep to be non-zero as
        // well as a bed id — so the bed id is dead data for exactly these two,
        // and LairRules.NeedsBed says no for them. Until 2026-09-05 it said
        // yes, and this test documented the contradiction instead.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var bedIdButNoSleep = db.Creatures.Values
            .Where(c => c.LairObjectId != 0 && !LairRules.NeedsBed(c))
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "Dwarf", "Stone Knight" }, bedIdButNoSleep);

        foreach (string name in bedIdButNoSleep)
        {
            CreatureDef creature = db.Creatures.Values.Single(c => c.Name == name);
            Assert.Equal(0, creature.TimeSleep);
            Assert.True(creature.TimeAwake >= Never);
            Assert.Equal(6, creature.LairObjectId);
        }
    }

    [Fact]
    public void TheGoblinsBedIsObjectTwentyTwoAndTheVampiresIsACoffin()
    {
        string? install = Install();
        if (install is null) return;

        // The fixture's Goblin carries 22, so this is its drift guard. The
        // Vampire's 26 is here because it is the one that makes the field
        // obviously a per-creature *object* rather than a flag — a vampire
        // sleeps in a different thing from a goblin.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(22, db.CreatureById(7).LairObjectId);
        Assert.Equal(26, db.CreatureByName("Vampire")!.LairObjectId);
        Assert.Equal(18, db.CreatureByName("Bile Demon")!.LairObjectId);
        Assert.Equal(0, db.CreatureByName("Imp")!.LairObjectId);
    }

    // ---- anger, which is not implemented but is parsed ---------------------

    [Fact]
    public void FiveSlapsKillAnImp_AndTheFixtureSaidZeroForAMonth()
    {
        string? install = Install();
        if (install is null) return;

        // slapDamage went unread from the day it was parsed until the Hand of
        // Evil arrived (ADR-0062), and Dk2TerrainFixture had transcribed the
        // Imp's as 0 - so the fixture quietly held that slapping an imp is
        // free. It is not: 100 against 500 health is five slaps, which is a
        // piece of DK2 anyone who has played it has done by accident.
        GameDatabase db = GameDatabaseLoader.Load(install);

        CreatureDef imp = db.CreatureByName("Imp")!;
        Assert.Equal(100, imp.SlapDamage);
        Assert.Equal(500, imp.Hp);
        Assert.Equal(5, imp.Hp / imp.SlapDamage);

        // The fixture's other two handled creatures, for the same reason.
        Assert.Equal(115, db.CreatureById(7).SlapDamage);
        Assert.Equal(125, db.CreatureByName("Vampire")!.SlapDamage);

        // And the one that says the field means what it looks like: Horny hits
        // hardest of all, at 240.
        Assert.Equal(240, db.CreatureByName("Horny")!.SlapDamage);
    }

    [Fact]
    public void TheHandsOwnBalanceVariablesAreWhatHandRulesReads()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        // What the hand holds (HandRules.Capacity).
        Assert.Equal(64, db[VariableTypeId.MaxNumberOfThingsInHand]);

        // And the pair that Q159 is about: the original's slap handler writes a
        // hardcoded 100 ticks into WorkFasterTimer, which at variable 19's four
        // ticks a game second is exactly SlapWorkFasterDurationSeconds. The
        // other one - 60 - is what the owner's stopwatch suggested and what
        // nothing in the binary has been found to read.
        Assert.Equal(25, db[VariableTypeId.SlapWorkFasterDurationSeconds]);
        Assert.Equal(25, db[VariableTypeId.SlapSpeedUpDurationSeconds]);
        Assert.Equal(60, db[VariableTypeId.IncreasedWorkRateDurationFromSlappingSeconds]);
        Assert.Equal(25, EfficiencyRules.SlapBonusSeconds);
    }

    [Fact]
    public void ThePrisonsOwnBalanceVariablesAndItsSkeletonAreWhatPrisonRulesReads()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        // The wither, which the 1999 manual states in words - "whilst in
        // Prison, creatures gradually lose health as they wither away" - and
        // which the variable is named for.
        Assert.Equal(-3, db[VariableTypeId.PrisonModifyCreatureHealthPerSecond]);
        Assert.Equal(-3, PrisonRules.WitherPerSecond(db, db.CreatureByName("Goblin")!));

        // And it is HUNGER (Q173): the original takes the health off only when
        // the creature's HungerFill is non-zero, so the four creatures that
        // never eat never wither. The Skeleton is one of them - which is what a
        // prison MAKES, so a cell full of skeletons is stable for ever.
        CreatureDef bones = db.CreatureByName("Skeleton")!;
        Assert.Equal(0, bones.HungerFill);
        Assert.Equal(0, PrisonRules.WitherPerSecond(db, bones));

        var neverWithers = db.Creatures.Values
            .Where(c => PrisonRules.WitherPerSecond(db, c) == 0)
            .Select(c => c.Name)
            .OrderBy(n => n)
            .ToList();
        Assert.Equal(
            db.Creatures.Values.Count(c => c.HungerFill == 0), neverWithers.Count);
        Assert.Contains("Skeleton", neverWithers);
        Assert.Contains("Imp", neverWithers);

        // The jail break, per extra creature over capacity.
        Assert.Equal(50, db[VariableTypeId.OvercrowdingJailBreakPercentagePerExtraCreature]);
        Assert.Equal(100, PrisonRules.JailBreakPercent(db, over: 2));
        Assert.Equal(0, PrisonRules.JailBreakPercent(db, over: 0));

        // And the ending, which is a room field rather than a variable: the
        // Prison's CreatedCreatureId is the Skeleton. Only two rooms in the
        // whole shipped table name a creature, and this is the one that is
        // built - the Graveyard's Vampire is not.
        RoomDef prison = db.Room(RoomTypeId.Prison);
        Assert.Equal(9, prison.CreatedCreatureId);
        Assert.Equal("Skeleton", db.CreatureById(prison.CreatedCreatureId).Name);

        // The Skeleton is the creature with no needs at all, and PrisonSystem
        // relies on it: a raised prisoner that wanted a bed, a chicken and a
        // wage would be a strange reward for a jailer.
        CreatureDef skeleton = db.CreatureById(prison.CreatedCreatureId);
        Assert.Equal(0, skeleton.Pay);
        Assert.Equal(0, skeleton.LairObjectId);

        // Parsed and read by nothing: how long a prisoner stands still for.
        Assert.Equal(8, db[VariableTypeId.MotionlessInPrisonSeconds]);
    }

    [Fact]
    public void TheAngerFieldsAreSignedAndTheLastTwoAreWhy()
    {
        string? install = Install();
        if (install is null) return;

        // Read as u16 — which is how the parser and kwd-creatures.md both had
        // them until 2026-09-04 — a Goblin's angerInHand comes back as 65286
        // and Jack's angerSlap as 60536. Signed, they are -250 and -5000, and
        // the whole table becomes coherent:
        //
        //   slapping ANGERS (+3000 Goblin, +7000 Warlock, +8000 Horny)
        //   being picked up CALMS (-250 Goblin, -500 Warlock)
        //
        // The first four fields are small positives on every shipped creature,
        // so signed reads them identically and is strictly safer.
        GameDatabase db = GameDatabaseLoader.Load(install);

        CreatureDef goblin = db.CreatureById(7);
        Assert.Equal(3000, goblin.AngerSlap);
        Assert.Equal(-250, goblin.AngerInHand);

        CreatureDef warlock = db.CreatureByName("Warlock")!;
        Assert.Equal(7000, warlock.AngerSlap);
        Assert.Equal(-500, warlock.AngerInHand);

        // Jack is the inverse of everything else — slapping calms it and being
        // held annoys it — which is of a piece with its other oddities (240
        // health, and a chicken worth 250).
        CreatureDef jack = db.CreatureByName("Jack")!;
        Assert.Equal(-5000, jack.AngerSlap);
        Assert.Equal(500, jack.AngerInHand);

        // Nothing in the shipped data needs more than a signed 16-bit range.
        Assert.All(db.Creatures.Values, c =>
        {
            Assert.InRange(c.AngerNoLair, (short)0, (short)1000);
            Assert.InRange(c.AngerNoFood, (short)0, (short)1000);
            Assert.InRange(c.AngerNoPay, (short)0, (short)1000);
            Assert.InRange(c.AngerNoWork, (short)0, (short)1000);
        });
    }

    [Fact]
    public void EveryCreatureBecomesUnhappyAtFiftyPercentOfWhateverAngerIs()
    {
        string? install = Install();
        if (install is null) return;

        // All 46 creatures share an UnhappyThreshold of 50, which is what makes
        // it read as a percentage rather than an absolute. What it is 50% *of*
        // is the blocking unknown for anger (Q106): there is no ceiling anywhere
        // in the balance table, and without it a percentage cannot be turned
        // into a number of seconds.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.All(db.Creatures.Values, c => Assert.Equal(50, c.UnhappyThreshold));
    }

    [Fact]
    public void OnlyTheSkeletonCannotBeAngeredByAnUnmetNeed()
    {
        string? install = Install();
        if (install is null) return;

        // The plain Skeleton is the *only* creature with 0 in all four
        // need-based anger fields — it has no lair, never eats, is not paid and
        // does not care about work.
        //
        // The **Unique Skeleton** is not, though it shares every one of those
        // properties. That is the second time a unique has diverged from the
        // creature it is a variant of, after the Unique Firefly eating where the
        // ordinary Firefly does not. Whether the uniques were edited
        // inconsistently or deliberately is UNKNOWN and does not matter: the
        // rules read the record.
        //
        // The Imp, the Dwarf and Horny carry 1, which is not zero but is a
        // hundredth of a Goblin's 100: present so that something can happen,
        // small enough that nothing does.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var unangerable = db.Creatures.Values
            .Where(c => c.AngerNoLair == 0 && c.AngerNoFood == 0
                        && c.AngerNoPay == 0 && c.AngerNoWork == 0)
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "Skeleton" }, unangerable);

        Assert.Equal(1, db.CreatureByName("Imp")!.AngerNoLair);
        Assert.Equal(100, db.CreatureById(7).AngerNoLair);
    }

    // ---- dying (Q99) ------------------------------------------------------

    [Fact]
    public void BothDyingTimersAreSixtySecondsAndTheyAreTwoDifferentClocks()
    {
        string? install = Install();
        if (install is null) return;

        // The owner's answer to Q99 describes two windows: a collapsed creature
        // lying rescuable, and then its corpse lying carryable. Both variables
        // were already parsed with nothing reading them, and both are 60.
        //
        // Asserted together precisely because they are equal. Two independently
        // named timers with the same value invite being collapsed into one
        // constant by someone tidying up, and they are not one thing: the first
        // is implemented, the second cannot be until corpses exist as objects
        // (M6). If EA ever shipped a level where these differ, this test says so.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(60, db[VariableTypeId.CreatureDyingStateDurationSeconds]);
        Assert.Equal(60, db[VariableTypeId.DeadBodyDiesAfterSeconds]);
    }

    [Fact]
    public void EightCreaturesLeaveNoBodyAndThatSetIsNotTheNeverHungryOne()
    {
        string? install = Install();
        if (install is null) return;

        // The third near-miss grouping in this record, and the third time the
        // tempting shortcut — "these are the undead, one flag would do" — is
        // refuted by a single creature.
        //
        //   never eats  : Imp Vampire Skeleton Horny StoneKnight
        //                 UniqueFirefly UniqueSkeleton UniqueVampire
        //   no body     : Imp Vampire Skeleton Horny StoneKnight
        //                 Jack           UniqueSkeleton UniqueVampire
        //
        // Seven in common and one each way: the **Unique Firefly** never eats
        // but does leave a body, and **Jack** eats but leaves none. Both sets
        // were guessed before they were read and the guesses were wrong, which
        // is why both are asserted by name.
        //
        // The Vampire leaving no body is the coherent member: a vampire is what
        // bodies are spent to make (DecomposeValueNeededForVampire = 1000).
        GameDatabase db = GameDatabaseLoader.Load(install);

        var noBody = db.Creatures.Values
            .Where(c => !CreatureDeathRules.LeavesBody(c))
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "Imp", "Vampire", "Skeleton", "Horny", "Stone Knight",
                "Jack", "Unique Skeleton", "Unique Vampire",
            },
            noBody);

        var neverEats = db.Creatures.Values
            .Where(c => !CreatureHealthRules.CanGetHungry(c))
            .Select(c => c.Name)
            .ToHashSet();

        // Neither set contains the other, in both directions, by one member.
        Assert.Equal(["Jack"], noBody.Except(neverEats).ToHashSet());
        Assert.Equal(["Unique Firefly"], neverEats.Except(noBody).ToHashSet());
    }

    [Fact]
    public void ADecomposingBodyIsWorthTwoHundredAndTheUndeadAreWorthNothing()
    {
        string? install = Install();
        if (install is null) return;

        // Why the body flag matters beyond cosmetics: bodies accumulate toward
        // a Vampire. Nothing implements that (M6), so this pins the numbers the
        // graveyard will read rather than leaving them to be rediscovered.
        //
        // The claim asserted here first was that a creature leaving no body is
        // worth nothing, since the two fields ought to agree. **They do not,
        // for exactly two creatures**, and the test caught it:
        //
        //   Imp, Vampire, Skeleton, Stone Knight, Unique Skeleton,
        //   Unique Vampire  -> no body, decompose 0     (consistent)
        //   Horny, Jack                -> no body, decompose 200  (unreachable)
        //
        // Horny is a summon rather than a roster creature (CONFIRMED by the
        // owner) and Jack is the creature whose whole record runs backwards, so
        // both are exactly where template values would survive unedited. The
        // **flag governs**: no body is generated, so there is nothing for the
        // 200 to be the value of, and it can never be read. Recorded because
        // reading DecomposeValue instead of the flag would put two ghosts into
        // a graveyard.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(200, db.CreatureById(7).DecomposeValue);       // Goblin

        var worthlessBodies = db.Creatures.Values
            .Where(c => !CreatureDeathRules.LeavesBody(c) && c.DecomposeValue == 0)
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "Imp", "Vampire", "Skeleton", "Stone Knight",
                "Unique Skeleton", "Unique Vampire",
            },
            worthlessBodies);

        var deadData = db.Creatures.Values
            .Where(c => !CreatureDeathRules.LeavesBody(c) && c.DecomposeValue != 0)
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "Horny", "Jack" }, deadData);
    }

    [Fact]
    public void TheEightCreaturesThatLeaveNoBodyAreExactlyTheOnesWithNoLyingState()
    {
        string? install = Install();
        if (install is null) return;

        // CONFIRMED from two directions that never met. The owner: "imps are
        // dead right away, they don't have a lying state." OpenKeeper's
        // HealthSystem.processHealthDepleted, before any unconscious handling:
        //
        //     // No body, just vanish from the world
        //     if (!isLeaveDeadBody(entityId)) { ...remove(); return; }
        //
        // ...where isLeaveDeadBody reads GENERATE_DEAD_BODY and nothing else.
        // So one flag decides both halves, which is coherent: the dying state
        // *is* a body on the floor, a live one. Nothing left behind means there
        // was nothing to lie there.
        GameDatabase db = GameDatabaseLoader.Load(install);

        foreach (CreatureDef c in db.Creatures.Values)
        {
            Assert.Equal(
                CreatureDeathRules.LeavesBody(c),
                CreatureDeathRules.HasDyingState(c));
        }

        Assert.False(CreatureDeathRules.HasDyingState(db.CreatureByName("Imp")!));
        Assert.True(CreatureDeathRules.HasDyingState(db.CreatureById(7)));   // Goblin
    }

    [Fact]
    public void OnlyTheTwoVampiresCanBeResurrectedAndTheirBedIsWhereTheyComeBack()
    {
        string? install = Install();
        if (install is null) return;

        // The owner's rule: "vampires also don't drop a body, they reincarnate
        // at their sarcophagus and sleep to recover, but they lose a level once
        // they reincarnate. If they were killed at level 1 they are permanently
        // dead. Exception to that rule for vampires are monks: if a monk kills
        // a vampire it is permanently dead, regardless of its level."
        //
        // CanBeResurrected turns out to be set on exactly the two Vampires, so
        // the rule is data-driven rather than a name check. **OpenKeeper does
        // not implement or even read this flag**, which makes this the second
        // mechanic where the owner's testimony is the only source there is.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var resurrects = db.Creatures.Values
            .Where(CreatureDeathRules.CanBeResurrected)
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "Vampire", "Unique Vampire" }, resurrects);

        // And the sarcophagus is its lair bed, which is what makes the rule
        // implementable at all when levels arrive: reincarnation puts the
        // creature in its own bed asleep, which is CreatureDeathRules.Rescue
        // with no imp involved.
        CreatureDef vampire = db.CreatureByName("Vampire")!;
        Assert.NotEqual(0, vampire.LairObjectId);
        Assert.True(LairRules.NeedsBed(vampire));

        // Neither of them leaves a body — there is no corpse because the
        // creature is not finished. Both facts, one cause.
        Assert.False(CreatureDeathRules.LeavesBody(vampire));
        Assert.False(CreatureDeathRules.LeavesBody(db.CreatureByName("Unique Vampire")!));

        // The monk exists and is an ordinary creature, so "a monk killed it"
        // will be answerable the day a kill has an attributed killer (M7).
        Assert.NotNull(db.CreatureByName("Monk"));
    }

    [Fact]
    public void KingReginaldIsTheCreatureTheDownGuardNowProtectsAgainst()
    {
        string? install = Install();
        if (install is null) return;

        // Own-land regeneration is suppressed at zero health so a collapsed
        // creature cannot heal itself off the floor. That guard was written for
        // the Imp — and the owner's answer then removed the Imp's lying state
        // altogether, which would have made the guard look pointless.
        //
        // It is not. King Reginald regenerates 400 a second on his own land
        // **and** leaves a body, so he collapses and would heal back up every
        // second he lay there: the one creature in the shipped data who would
        // be unkillable at home. He is also the only one, which is exactly why
        // this is asserted rather than assumed.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var healsAndCollapses = db.Creatures.Values
            .Where(c => c.OwnLandHealthIncreasePerSecond > 0
                        && CreatureDeathRules.HasDyingState(c))
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "King Reginald" }, healsAndCollapses);
        Assert.Equal(400, db.CreatureByName("King Reginald")!.OwnLandHealthIncreasePerSecond);
    }

    // ---- terrain health deltas (the water observation) --------------------

    [Fact]
    public void ThreeTerrainsCarryAHealthDeltaAndOneOfThemHeals()
    {
        string? install = Install();
        if (install is null) return;

        // The claim written first was that TerrainDef.Damage is non-zero on
        // Water and Lava alone. **It is not**, and this test is what caught it:
        // Hero Lair carries -67, so the field is *signed* and is not damage but
        // a health delta per second — the hero side's answer to a keeper's
        // lair, healing whatever stands on it.
        //
        // Three of 41, which is the same shape as mana (three of 41 generate
        // any, ADR-0017) and own-land regeneration (two of 46). Third time this
        // project has claimed a column was simpler than it is, so it is
        // asserted by name rather than by count.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var deltas = db.Terrain.Values
            .Where(t => t.Damage != 0)
            .ToDictionary(t => t.Name, t => (int)t.Damage);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Water"] = 100,
                ["Lava"] = 100,
                ["Hero Lair"] = -67,
            },
            deltas);
    }

    [Fact]
    public void WaterIsImpenetrableButNotSolid_WhichIsWhyImpsCanWadeIt()
    {
        string? install = Install();
        if (install is null) return;

        // CONFIRMED by the owner: a gem tile on an island cut off by water,
        // no bridge, and imps waded over, mined it and carried the gold home.
        // The original states the rule in its own tooltip — "Water. Impassable
        // to some, others are merely hindered by it."
        //
        // The distinction that makes it work: water carries Impenetrable, which
        // means "cannot be tunnelled through" and is true of water, and does
        // NOT carry Solid, which is what blocks a walker. Asserted because
        // reading Impenetrable as "impassable" is the obvious mistake and would
        // strand every imp on its own side of a lake.
        GameDatabase db = GameDatabaseLoader.Load(install);
        TerrainDef water = db.TerrainById(4);

        Assert.Equal("Water", water.Name);
        Assert.True(water.IsWater);
        Assert.False(water.IsSolid);
    }

    [Fact]
    public void TheTwoVampiresAreTheOnlyCreaturesWaterStops()
    {
        string? install = Install();
        if (install is null) return;

        // The owner named this from play, before the column had been read:
        // "I think vampires can't cross through water, they need bridges".
        //
        // It is right, and it is right about exactly two records out of 46.
        // Which makes it the strongest single confirmation this project has of
        // the CanWalkOnWater bit: a guess from memory of the original that
        // lands on a two-element set in a 46-element column, in a record whose
        // flag layout was reconstructed rather than documented.
        //
        // It also reframes water. 44 of 46 creatures cross it, so water is not
        // a barrier with exceptions but a hindrance with two exceptions — which
        // is what the tooltip says in the game's own words ("Impassable to
        // some, others are merely hindered by it") and what makes it worth
        // charging health only to those who cannot cross.
        //
        // Asserted by name rather than by count, because a count that changes
        // for a different install tells nobody which creature moved.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var landlubbers = db.Creatures.Values
            .Where(c => !c.Flags.HasFlag(CreatureFlags.CanWalkOnWater))
            .Select(c => c.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Unique Vampire", "Vampire"], landlubbers);
        Assert.Equal(46, db.Creatures.Count);

        // And the corollary the health rule depends on: the creature the owner
        // watched wade is one of the 44, as is the Goblin the fixture used to
        // charge for it.
        Assert.True(db.CreatureByName("Imp")!.Flags.HasFlag(CreatureFlags.CanWalkOnWater));
        Assert.True(db.CreatureById(7).Flags.HasFlag(CreatureFlags.CanWalkOnWater));

        // Lava is the opposite shape and is where the rule does its work: seven
        // creatures cross it, so the other 39 burn. The seven are named in
        // BridgesOnRealDataTests; here it is enough that the Imp is not one.
        Assert.False(db.CreatureByName("Imp")!.Flags.HasFlag(CreatureFlags.CanWalkOnLava));
    }

    [Fact]
    public void TheFixtureVampireIsTheShippedVampire()
    {
        string? install = Install();
        if (install is null) return;

        // The drift guard for the fixture's third creature, added because the
        // Vampire is the only one water can kill and so the only one a hazard
        // test can be honest with. Every number below is transcribed into
        // Dk2TerrainFixture.Vampire(); this project deliberately does not
        // reference the simulation fixture from here, so the values are
        // repeated rather than compared, exactly as the Imp's and the Goblin's
        // guards do it.
        //
        // Its three ABSENT flags are as much of the record as its present ones,
        // and each is a mechanic:
        //
        //   no CanWalkOnWater     water charges it 100 a second
        //   no GenerateDeadBody   it leaves no corpse but reincarnates (Q113)
        //   no AvailableViaPortal it is not recruited; it arrives from a
        //                         Graveyard, which its own attraction slot
        //                         asks for by room id 14
        GameDatabase db = GameDatabaseLoader.Load(install);
        CreatureDef vampire = db.CreatureByName("Vampire")!;

        Assert.Equal(8, vampire.CreatureId);

        Assert.False(vampire.Flags.HasFlag(CreatureFlags.CanWalkOnWater));
        Assert.False(vampire.Flags.HasFlag(CreatureFlags.CanWalkOnLava));
        Assert.False(vampire.Flags.HasFlag(CreatureFlags.GenerateDeadBody));
        Assert.False(vampire.Flags.HasFlag(CreatureFlags.AvailableViaPortal));
        Assert.True(vampire.Flags.HasFlag(CreatureFlags.IsEvil));
        Assert.True(vampire.Flags.HasFlag(CreatureFlags.IsFearless));
        Assert.True(vampire.Flags.HasFlag(CreatureFlags.CanBeResurrected));
        Assert.True(vampire.Flags.HasFlag(CreatureFlags.NeedBodyForFightIdle));

        Assert.Equal(2500, vampire.Hp);
        Assert.Equal(666, vampire.MeleeDamage);
        Assert.Equal(750, vampire.Pay);
        Assert.Equal(267, vampire.Fear);
        Assert.Equal(107, vampire.Threat);
        Assert.Equal(2867, vampire.Speed.Raw);
        Assert.Equal(5160, vampire.RunSpeed.Raw);
        Assert.Equal(1638, vampire.ShuffleSpeed.Raw);
        Assert.Equal(JobClass.Thinker, vampire.JobClass);

        // Never eats, and the sentinel is the same 32767 the Imp carries.
        Assert.False(CreatureHealthRules.CanGetHungry(vampire));
        Assert.Equal(0, vampire.HungerFill);
        Assert.Equal(0, vampire.HpFromChicken);

        // But does sleep, and its bed is its coffin: object 26, against the
        // Goblin's 22.
        Assert.Equal(330, vampire.TimeAwake);
        Assert.Equal(9, vampire.TimeSleep);
        Assert.Equal(26, vampire.LairObjectId);
        Assert.True(LairRules.NeedsBed(vampire));

        // The Graveyard attraction, which is how a vampire is acquired at all.
        Assert.Equal(14, vampire.Attractions[0].RoomId);

        // Anger. AngerNoFood is 1 on a creature that never eats — a needle in
        // a need it does not have — and AngerSlap 7000 is more than twice the
        // Goblin's 3000, so five slaps make a Vampire unhappy where a Goblin
        // takes six (Q114).
        Assert.Equal(100, vampire.AngerNoLair);
        Assert.Equal(1, vampire.AngerNoFood);
        Assert.Equal(60, vampire.AngerNoPay);
        Assert.Equal(60, vampire.AngerNoWork);
        Assert.Equal(7000, vampire.AngerSlap);
        Assert.Equal(-250, vampire.AngerInHand);
        Assert.Equal(50, vampire.UnhappyThreshold);

        // And an angry table with no Leave in it: a vampire rebels rather than
        // walking out, which is the one creature-specific consequence of the
        // anger ladder currently visible.
        Assert.Equal([JobType.Rebel], vampire.AngryJobs.Select(j => j.Job));
        Assert.Equal(100, vampire.AngryJobs[0].ChancePercent);

        Assert.Equal(0, vampire.DecomposeValue);
        Assert.Equal(7500, vampire.MaxGoldHeld);
        Assert.Equal(500, vampire.InitialGoldHeld);
    }

    // ---- levels (Q119) ----------------------------------------------------

    [Fact]
    public void ThePerLevelStatTableIsInTheShippedFileAndTheFixtureCopiesItExactly()
    {
        string? install = Install();
        if (install is null) return;

        // The owner, in passing, while trying to time an imp through water:
        // "imps gain speed as they gain more levels". This is the table that
        // makes that true, and it had been parsed into
        // GlobalVariables.CreatureStats since variables were first read and
        // examined by nothing.
        //
        // 320 rows: 32 stats x 9 levels (2 to 10) with a first-person set
        // alongside. Asserted by value rather than by count, because a count
        // that changed would not say which stat moved -- and because these
        // exact rows are transcribed into Dk2TerrainFixture.Variables().
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.NotEmpty(db.Variables.CreatureStats);

        // Health: 125% at level 2 to 600% at level 10. A level-10 Goblin has
        // 9000 health against its record's 1500.
        Assert.Equal(
            new[] { 125, 150, 175, 200, 225, 250, 300, 400, 600 },
            Levels(db, CreatureStatId.Health));

        // Speed: +5% a level to level 5, then a much steeper climb. This is the
        // row the owner was looking at without knowing it.
        Assert.Equal(
            new[] { 105, 110, 115, 120, 125, 150, 175, 200, 250 },
            Levels(db, CreatureStatId.Speed));

        // Run speed scales identically to walking speed, which is worth
        // pinning: it means a level does not change the ratio between them.
        Assert.Equal(Levels(db, CreatureStatId.Speed), Levels(db, CreatureStatId.RunSpeed));

        // Experience for the next level, to 1000% -- the last level costs ten
        // times the first.
        Assert.Equal(
            new[] { 130, 180, 250, 350, 470, 600, 720, 850, 1000 },
            Levels(db, CreatureStatId.ExperienceForNextLevel));

        // And the negative half of the finding: own-land regeneration never
        // scales, so an Imp heals at 67 a second at level 1 and at level 10.
        // Ten of the 32 stats are flat like this, so "levels scale everything"
        // would be wrong about a third of the table.
        Assert.Equal(
            new[] { 100, 100, 100, 100, 100, 100, 100, 100, 100 },
            Levels(db, CreatureStatId.OwnLandHealthIncreasePerSecond));
    }

    [Fact]
    public void TheLevelledCurveAppliesToTheShippedCreatureRecords()
    {
        string? install = Install();
        if (install is null) return;

        // The two stats this project actually reads, against the install rather
        // than the fixture, because the arithmetic only means something if the
        // shipped record is the one being scaled.
        GameDatabase db = GameDatabaseLoader.Load(install);
        CreatureDef goblin = db.CreatureById(7);
        CreatureDef imp = db.CreatureByName("Imp")!;

        Assert.Equal(1500, goblin.Hp);
        Assert.Equal(1500, CreatureLevelRules.MaxHealth(db, goblin, 1));
        Assert.Equal(9000, CreatureLevelRules.MaxHealth(db, goblin, 10));

        // The owner's remark, in raw fixed-point units: 1.84 tiles a second
        // becomes 1.93 at level 2, and 4.6 at level 10.
        Assert.Equal(7536, imp.Speed.Raw);
        Assert.Equal(7536, CreatureLevelRules.SpeedRaw(db, imp, 1));
        Assert.Equal(7912, CreatureLevelRules.SpeedRaw(db, imp, 2));
        Assert.Equal(18840, CreatureLevelRules.SpeedRaw(db, imp, 10));

        // Which is what makes Q118's water arithmetic level-dependent: the
        // ShuffleSpeed hypothesis has no stat row, so if water runs a creature
        // at shuffle speed the slowdown deepens as it levels. At level 1 an
        // imp in water would be 45.7% slower; at level 2, 48.2%; at level 10,
        // 78%. The owner eyeballed 50-60% on a level-2 imp.
        Assert.Equal(4096, imp.ShuffleSpeed.Raw);
        Assert.True(imp.ShuffleSpeed.Raw * 100 / CreatureLevelRules.SpeedRaw(db, imp, 2) == 51);
        Assert.True(imp.ShuffleSpeed.Raw * 100 / CreatureLevelRules.SpeedRaw(db, imp, 10) == 21);
    }

    /// <summary>One stat's percentages for levels 2 to 10, in order.</summary>
    private static int[] Levels(GameDatabase db, CreatureStatId stat) =>
        Enumerable.Range(2, 9).Select(l => db.StatPercent(stat, l)).ToArray();

    // ---- anger, against the owner's two measurements ----------------------

    [Fact]
    public void TheOwnersMeasurementsReproduceOnTheShippedRecord()
    {
        string? install = Install();
        if (install is null) return;

        // Both Q106 measurements, replayed against the install rather than the
        // fixture, because the arithmetic is only meaningful if the *shipped*
        // rates are the ones that were measured.
        GameDatabase db = GameDatabaseLoader.Load(install);

        CreatureDef goblin = db.CreatureById(7);
        CreatureDef salamander = db.CreatureByName("Salamander")!;

        // The stopwatch: a lairless Goblin at 100/s, unhappy ~2:45, angry ~5:30.
        Assert.Equal(100, goblin.AngerNoLair);
        Assert.Equal(164, CreatureAngerRules.UnhappyAt(goblin) / goblin.AngerNoLair + 1);
        Assert.Equal(328, CreatureAngerRules.AngryAt(goblin) / goblin.AngerNoLair + 1);

        // The slap count, taken on a Salamander — which works only because its
        // AngerSlap is also 3000. If it were not, the observation would say
        // nothing about a Goblin and this test is what would notice.
        Assert.Equal(3000, salamander.AngerSlap);
        Assert.Equal(goblin.AngerSlap, salamander.AngerSlap);

        // 6 to unhappy, 11 to angry — the brackets, both sides.
        Assert.True(5 * 3000 < CreatureAngerRules.UnhappyAt(salamander));
        Assert.True(6 * 3000 >= CreatureAngerRules.UnhappyAt(salamander));
        Assert.True(10 * 3000 < CreatureAngerRules.AngryAt(salamander));
        Assert.True(11 * 3000 >= CreatureAngerRules.AngryAt(salamander));
    }

    [Fact]
    public void EveryCreatureSharesTheSameCeilingBecauseEveryOneSharesTheThreshold()
    {
        string? install = Install();
        if (install is null) return;

        // The ceiling is global, and this is what makes it so: all 46 carry
        // UnhappyThreshold = 50, so UnhappyAt is 16383 for every creature in
        // the game. A per-creature ceiling would need a per-creature field, and
        // there is none.
        GameDatabase db = GameDatabaseLoader.Load(install);

        foreach (CreatureDef c in db.Creatures.Values)
        {
            Assert.Equal(50, c.UnhappyThreshold);
            Assert.Equal(16383, CreatureAngerRules.UnhappyAt(c));
        }
    }

    [Fact]
    public void ALairlessImpWouldTakeNineHoursToGetAngry()
    {
        string? install = Install();
        if (install is null) return;

        // Why "Imps never get angry" needs no special case. Its AngerNoLair is
        // 1, not 0 — present so something *can* happen — and against a ceiling
        // of 32767 that is nine hours. The arithmetic is the exemption.
        GameDatabase db = GameDatabaseLoader.Load(install);
        CreatureDef imp = db.CreatureByName("Imp")!;

        Assert.Equal(1, imp.AngerNoLair);

        int seconds = CreatureAngerRules.AngryAt(imp) / imp.AngerNoLair;
        Assert.Equal(32767, seconds);
        Assert.InRange(seconds / 3600.0, 9.0, 9.2);

        // And it is moot anyway: an imp is owed no bed, so nothing charges it.
        Assert.False(LairRules.NeedsBed(imp));
    }

    [Fact]
    public void OnlyTheSkeletonCannotBeAngeredAtAll()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        var unangerable = db.Creatures.Values
            .Where(c => !CreatureAngerRules.CanBeAngered(c))
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "Skeleton" }, unangerable);
    }

    // ---- the vampire, cross-checked against the DK2 wiki ------------------
    //
    // Source: the Dungeon Keeper wiki's Vampire page, **Dungeon Keeper 2
    // section only** — the page covers DK1, DK2 and DK Online together and
    // tags each claim, and the games differ (DK1 needs ten corpses, DK2 five).
    // Read through the MediaWiki API; see docs/research/wiki-as-a-source.md for
    // why no wiki prose is copied into this repository.

    [Fact]
    public void FiveCorpsesMakeAVampire_WhichIsTwoNumbersAgreeingFromTwoSources()
    {
        string? install = Install();
        if (install is null) return;

        // The DK2 wiki: "When five or more corpses rot in the Graveyard (DK2)"
        // and "When five or more corpses rot in your Graveyard a Vampire
        // appears."
        //
        // The shipped data never says "five". It says a corpse is worth 200
        // (DecomposeValue) and a vampire needs 1000
        // (DecomposeValueNeededForVampire). 1000 / 200 = 5.
        //
        // **Two independent sources landing on the same integer**, and it is
        // worth more than either alone: it confirms the wiki's number *and*
        // confirms that our reading of DecomposeValue as a per-corpse worth is
        // right, which nothing else could have checked. It also rules out the
        // reading where DecomposeValueNeededForVampire counts corpses directly.
        GameDatabase db = GameDatabaseLoader.Load(install);

        int perCorpse = db.CreatureById(7).DecomposeValue;             // Goblin
        int needed = db[VariableTypeId.DecomposeValueNeededForVampire];

        Assert.Equal(200, perCorpse);
        Assert.Equal(1000, needed);
        Assert.Equal(5, needed / perCorpse);
        Assert.Equal(0, needed % perCorpse);        // a whole number of corpses
    }

    [Fact]
    public void TwoBlackKnightsSacrificedMakeAVampire()
    {
        string? install = Install();
        if (install is null) return;

        // The DK2 wiki gives the Vampire's sacrifice recipe as "Black Knight
        // x2 (DK2)". The shipped temple table has it, and this is the first
        // time anything has read SacrificeEntry at all — so it doubles as a
        // check that the record is decoded correctly, since a wrong layout
        // would be very unlikely to produce this exact pair.
        GameDatabase db = GameDatabaseLoader.Load(install);

        byte blackKnight = db.CreatureByName("Black Knight")!.CreatureId;
        byte vampire = db.CreatureByName("Vampire")!.CreatureId;

        Assert.Contains(db.Variables.Sacrifices, s =>
            s.Type1 == 1 && s.Id1 == blackKnight &&
            s.Type2 == 1 && s.Id2 == blackKnight &&
            s.Id3 == 0 &&                            // a two-ingredient recipe
            s.RewardType == 1 && s.RewardValue == vampire);
    }

    [Fact]
    public void TheVampiresOwnNumbersMatchTheWikisDk2Column()
    {
        string? install = Install();
        if (install is null) return;

        // Four numbers the wiki states for DK2 specifically, checked against
        // the install. Cheap, and it is what earns the right to trust the
        // wiki's *unmeasurable* claims — the resurrection rule — since a source
        // that got the health and the wage wrong would be no source at all.
        GameDatabase db = GameDatabaseLoader.Load(install);
        CreatureDef vampire = db.CreatureByName("Vampire")!;

        Assert.Equal(2500, vampire.Hp);                     // "2500 (DK2)"
        Assert.Equal(750, vampire.Pay);                     // "wages 750"
        Assert.Equal(JobClass.Thinker, vampire.JobClass);   // "job Thinker"
        Assert.Equal(FightStyle.Blitzer, vampire.FightStyle);   // "Blitzer (DK2)"

        // And the two the wiki implies rather than states: undead do not eat,
        // and a vampire is not finished when it drops.
        Assert.False(CreatureHealthRules.CanGetHungry(vampire));
        Assert.False(CreatureDeathRules.LeavesBody(vampire));
        Assert.True(CreatureDeathRules.CanBeResurrected(vampire));
    }
}
