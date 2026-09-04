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
}
