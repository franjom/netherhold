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
            ChickensNeeded = 2,
        };

        CreatureHealthRules.Eat(jack, state);

        Assert.Equal(240, state.Health);
    }

    // ---- beds ------------------------------------------------------------

    [Fact]
    public void TenCreaturesHaveNoBedAndTheyAreTheOnesWithNowhereToLive()
    {
        string? install = Install();
        if (install is null) return;

        // LairObjectId is the bed object itself, and 0 means there is none.
        // The owner CONFIRMED the two interesting members: "skeleton does not
        // have a lair", and "horny is only a summonable creature, it is not in
        // a regular roster of creatures, and only on special conditions/maps
        // when conditions are met" — which is also why Horny carries no
        // AvailableViaPortal (ADR-0047).
        //
        // The other seven fall out the same way: the Imp lives nowhere, and
        // the five hero lords plus Jack come through hero gates rather than
        // living in anybody's dungeon.
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
            },
            bedless);
    }

    [Fact]
    public void TwoCreaturesOwnABedTheyCanNeverSleepIn()
    {
        string? install = Install();
        if (install is null) return;

        // The Dwarf and the Stone Knight both carry a bed object *and* a
        // TimeAwake of 32767, so by the record they occupy a lair tile and
        // never sleep in it. Which of the two fields DK2 gates on is Q104.
        //
        // Nothing depends on it — a creature that never sleeps never heals in a
        // bed either way — but it is exactly the sort of quiet contradiction
        // that a rule written from one field would get wrong, so it is checked
        // rather than assumed away.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var bedButSleepless = db.Creatures.Values
            .Where(c => LairRules.NeedsBed(c) && c.TimeAwake >= Never)
            .Select(c => c.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "Dwarf", "Stone Knight" }, bedButSleepless);
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
