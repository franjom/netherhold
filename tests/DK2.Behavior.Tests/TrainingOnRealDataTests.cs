// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// The shipped numbers training rests on, re-asserted against the reference
/// install — ADR-0083, Q205.
///
/// <para><b>Why this file exists.</b> The fixture Goblin had been inheriting the
/// Imp's `ExpPerSecondTraining` of 30 and `ExpPerSecond` of 1200. Its own are 40
/// and 200. Nothing noticed for as long as nothing read them, and the moment
/// training paid, the fixture would have been quietly testing a Goblin that does
/// not exist. That is the fourth field this guard has caught.</para>
///
/// <para>Skips when no installation is configured (DK2_DIR), so CI stays
/// green without proprietary assets.</para>
/// </summary>
public sealed class TrainingOnRealDataTests
{
    private static GameDatabase? RealDatabase()
    {
        string? install = Dk2Install.Find();
        return install is null ? null : GameDatabaseLoader.Load(install);
    }

    [Fact]
    public void TheTrainingRateColumnTakesFiveValuesAndMostOfThemTruncate()
    {
        // THE QUIRK, stated over the whole shipped table rather than one
        // creature. The loader divides these rates by variable 19 and truncates,
        // so any value that is not a multiple of four loses up to three of
        // itself a game second - and most of the table is not a multiple of
        // four.
        GameDatabase? maybe = RealDatabase();
        if (maybe is null) return; // skipped: no DK2 install
        GameDatabase db = maybe;

        var byRate = db.Creatures.Values
            .GroupBy(c => (int)c.ExpPerSecondTraining)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal([15, 20, 25, 30, 40], byRate.Keys.Order().ToArray());
        Assert.Equal(46, byRate.Values.Sum());

        int truncating = db.Creatures.Values.Count(c =>
            c.ExpPerSecondTraining % SimulationTiming.OriginalTicksPerGameSecond != 0);
        Assert.Equal(26, truncating);

        // The two ends of the loss, named: Horny's 15 becomes 12 and the
        // Goblin's 40 stays 40.
        Assert.Equal(3, SimulationTiming.LoaderRatePerTick(15));
        Assert.Equal(6, SimulationTiming.LoaderRatePerTick(25));
        Assert.Equal(7, SimulationTiming.LoaderRatePerTick(30));
        Assert.Equal(10, SimulationTiming.LoaderRatePerTick(40));
    }

    [Fact]
    public void TheLoadersFloorIsReachableAndTheSkeletonIsWhereItShows()
    {
        // The loader writes 1 whenever the file value is non-zero and the
        // quotient is zero. Not rounding hygiene: the Skeleton's
        // ResearchPerSecond is 1 and the Dark Angel's is 3, and both become 4 a
        // game second - four times and a third more than their records say. The
        // floor is what makes those two able to research at all.
        GameDatabase? maybe = RealDatabase();
        if (maybe is null) return; // skipped: no DK2 install
        GameDatabase db = maybe;

        foreach (CreatureDef c in db.Creatures.Values)
        {
            if (c.ResearchPerSecond is 0 or >= 4) continue;

            Assert.InRange((int)c.ResearchPerSecond, 1, 3);
            Assert.Equal(1, SimulationTiming.LoaderRatePerTick(c.ResearchPerSecond));
        }

        CreatureDef skeleton = db.Creatures.Values.Single(c => c.Name == "Skeleton");
        Assert.Equal(1, skeleton.ResearchPerSecond);
        Assert.Equal(1, SimulationTiming.LoaderRatePerTick(skeleton.ResearchPerSecond));
    }

    [Fact]
    public void CombatExperienceIsFiveTimesTrainingInFortyOneOfTheFortySixRecords()
    {
        // Read off the data before any code was, and it is the argument that the
        // two fields are the same kind of quantity in the same unit: they were
        // maintained as one decision. The five exceptions are recorded rather
        // than smoothed over - three uniques whose combat rate was raised
        // without the training column following, the Maiden Of The Nest, and the
        // Imp at forty times (Q208).
        GameDatabase? maybe = RealDatabase();
        if (maybe is null) return; // skipped: no DK2 install
        GameDatabase db = maybe;

        var exceptions = db.Creatures.Values
            .Where(c => c.ExpPerSecondTraining != 0
                        && c.ExpPerSecond != c.ExpPerSecondTraining * 5)
            .Select(c => c.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            ["Imp", "Maiden Of The Nest", "Unique Bile Demon", "Unique Mistress", "Unique Troll"],
            exceptions);

        // And the Imp is the outlier among the outliers, on the one creature
        // that neither trains nor fights.
        CreatureDef imp = db.Creatures.Values.Single(c => c.Name == "Imp");
        Assert.Equal(1200, imp.ExpPerSecond);
        Assert.Equal(30, imp.ExpPerSecondTraining);
        Assert.True(imp.Flags.HasFlag(CreatureFlags.IsWorker));
    }

    [Fact]
    public void TheGoblinsOwnRatesAreFortyAndTwoHundred()
    {
        // The transcription the fixture got wrong, asserted where a drift shows.
        GameDatabase? maybe = RealDatabase();
        if (maybe is null) return; // skipped: no DK2 install
        GameDatabase db = maybe;
        CreatureDef goblin = db.Creatures.Values.Single(c => c.Name == "Goblin");

        Assert.Equal(40, goblin.ExpPerSecondTraining);
        Assert.Equal(200, goblin.ExpPerSecond);
        Assert.Equal(3000, goblin.ExpForNextLevel);

        // Forty divides by four exactly, so the Goblin is the creature for which
        // the shipped name is the shipped truth.
        Assert.Equal(
            10 * SimulationTiming.OriginalTicksPerGameSecond,
            SimulationTiming.LoaderRatePerTick(goblin.ExpPerSecondTraining)
            * SimulationTiming.OriginalTicksPerGameSecond);
    }

    [Fact]
    public void TheTwoRoomCapsAndTheTrainingBillAreTheShippedNumbers()
    {
        GameDatabase? maybe = RealDatabase();
        if (maybe is null) return; // skipped: no DK2 install
        GameDatabase db = maybe;

        Assert.Equal(4, db[VariableTypeId.TrainingRoomMaxExperienceLevel]);
        Assert.Equal(8, db[VariableTypeId.CombatPitMaxExperienceLevel]);

        // Negative in the table and negated by the original before it is spent,
        // so the charge is five.
        Assert.Equal(-5, db[VariableTypeId.ModifyPlayerColdWhileTrainingPerSecond]);
        Assert.Equal(5, TrainingRules.GoldPerSecond(db));

        // The drain DK2 built and then switched off.
        Assert.Equal(0, TrainingRules.ExperienceLostPerSecondWhileNotTraining(db));
    }

    [Fact]
    public void TheTwoExperienceStatColumnsAreFlatAndTheProductionPairIsNot()
    {
        // How stats 15 and 16 were named: the four columns the four rate hooks
        // scale by are two matched pairs, and OpenKeeper already had names for
        // the far pair that the binary's hooks confirm. So the near pair is
        // pinned by position with an anchor on each side.
        GameDatabase? maybe = RealDatabase();
        if (maybe is null) return; // skipped: no DK2 install
        GameDatabase db = maybe;

        for (int level = 2; level <= 10; level++)
        {
            Assert.Equal(100, db.StatPercent(CreatureStatId.ExpPerSecond, level));
            Assert.Equal(100, db.StatPercent(CreatureStatId.ExpPerSecondTraining, level));

            // Research and manufacture carry the SAME curve as each other, which
            // is the other half of the pairing.
            Assert.Equal(
                db.StatPercent(CreatureStatId.ResearchPerSecond, level),
                db.StatPercent(CreatureStatId.ManufacturePerSecond, level));
        }

        Assert.Equal(125, db.StatPercent(CreatureStatId.ResearchPerSecond, 2));
        Assert.Equal(700, db.StatPercent(CreatureStatId.ResearchPerSecond, 10));

        // And the threshold column is the one that makes later levels slower,
        // since the rate does not scale at all.
        Assert.Equal(130, db.StatPercent(CreatureStatId.ExperienceForNextLevel, 2));
        Assert.Equal(1000, db.StatPercent(CreatureStatId.ExperienceForNextLevel, 10));
    }
}
