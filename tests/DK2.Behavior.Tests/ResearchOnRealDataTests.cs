// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// The Library's accrual against the shipped data. See ADR-0090 and
/// docs/research/library-and-research.md.
/// </summary>
public class ResearchOnRealDataTests
{
    private static string Install() => Dk2InstallGate.Path;

    [InstallFact]
    public void ElevenCreaturesCanResearchAndTheWizardIsTheBestOfThem()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);
        CreatureDef[] researchers =
            [.. db.Creatures.Values.Where(c => c.ResearchPerSecond > 0)
                 .OrderBy(c => c.CreatureId)];

        // ELEVEN, and the list is worth pinning because it is not the list a
        // player would guess: the Warlock is the Library's creature in every
        // description of the game and it is joint SECOND, with the Wizard at
        // twice its rate. The uniques are the campaign's named characters.
        Assert.Equal(11, researchers.Length);
        Assert.Equal(
            new[] { "Warlock", "Vampire", "Skeleton", "Wizard", "Monk", "Fairy", "Dark Angel ",
                    "Unique Warlock", "Unique Vampire", "Unique Dark Angel",
                    "Maiden Of The Nest" },
            researchers.Select(c => c.Name).ToArray());

        // "Dark Angel " ships with a TRAILING SPACE in its name field, and the
        // list above keeps it rather than trimming. Pinned deliberately: a
        // lookup by name silently returns null for it, which is how this test
        // found it, and anything that keys creatures by name has the same bug
        // waiting. Its unique counterpart, "Unique Dark Angel", has no space.
        Assert.Null(db.CreatureByName("Dark Angel"));
        Assert.NotNull(db.CreatureByName("Dark Angel "));

        Assert.Equal(5, db.CreatureByName("Warlock")!.ResearchPerSecond);
        Assert.Equal(10, db.CreatureByName("Wizard")!.ResearchPerSecond);
    }

    [InstallFact]
    public void TheLoadersFloorMAKESTheSkeletonResearchFourTimesFasterThanItsRecordSays()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);

        // THE FLOOR RUNS BOTH WAYS, and this is the half that had not been
        // found. SimulationTiming.LoaderRatePerTick divides a tabled rate by
        // variable 19's four and floors the result at ONE for any non-zero file
        // value - so a rate below four does not shrink, it GROWS:
        //
        //   Skeleton  tabled 1 -> 1 a tick -> 4 a second, FOUR TIMES its record
        //   Monk      tabled 3 -> 1 a tick -> 4 a second
        //   Dark Angel tabled 3 -> 1 a tick -> 4 a second
        //
        // That also corrects a claim this project had written into
        // SimulationTiming: that the floor is unreachable because "every one of
        // the ten is either zero or at least four". Three creatures carry 1 or
        // 3 in ResearchPerSecond. The sign-flip consequence the claim was
        // guarding against still cannot happen - these are positive - but the
        // evidence offered for it was false, and the floor is demonstrably
        // load-bearing rather than hygiene.
        CreatureDef skeleton = db.CreatureByName("Skeleton")!;
        Assert.Equal(1, skeleton.ResearchPerSecond);
        Assert.Equal(1, SimulationTiming.LoaderRatePerTick(skeleton.ResearchPerSecond));
        Assert.Equal(4, SimulationTiming.LoaderRatePerTick(skeleton.ResearchPerSecond)
                        * SimulationTiming.OriginalTicksPerGameSecond);

        // By id, not by name: "Dark Angel " ships with a trailing space (see
        // the test above). 19 is the Monk, 23 the Dark Angel.
        foreach (byte id in new byte[] { 19, 23 })
        {
            CreatureDef it = db.CreatureById(id);
            Assert.Equal(3, it.ResearchPerSecond);
            Assert.Equal(1, SimulationTiming.LoaderRatePerTick(it.ResearchPerSecond));
        }

        // And the other direction on the same column, which is the quirk the
        // project already knew: a rate that is not a multiple of four loses the
        // remainder. The Warlock's 5 becomes 1 a tick and delivers 4, not 5.
        CreatureDef warlock = db.CreatureByName("Warlock")!;
        Assert.Equal(1, SimulationTiming.LoaderRatePerTick(warlock.ResearchPerSecond));
        Assert.Equal(4, SimulationTiming.LoaderRatePerTick(warlock.ResearchPerSecond)
                        * SimulationTiming.OriginalTicksPerGameSecond);
    }

    [InstallFact]
    public void TheResearchLevelCurveIsTheSteepestProductionCurveInTheGame()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);

        // 100% at level 1 to SEVEN HUNDRED at level 10, and identical to the
        // Manufacture row - which is how those two columns were pinned in the
        // first place (CreatureStatId.ExpPerSecond). A level-10 Warlock
        // researches at 28 a second against a level-1's 4.
        int[] curve = [.. Enumerable.Range(1, 10)
                          .Select(l => db.StatPercent(CreatureStatId.ResearchPerSecond, l))];

        int[] manufacture = [.. Enumerable.Range(1, 10)
                                .Select(l => db.StatPercent(CreatureStatId.ManufacturePerSecond, l))];

        Assert.Equal(new[] { 100, 125, 175, 250, 300, 350, 400, 450, 550, 700 }, curve);
        Assert.Equal(curve, manufacture);

        CreatureDef warlock = db.CreatureByName("Warlock")!;
        var state = new CreatureState { Level = 10 };
        Assert.Equal(7, ResearchRules.PerOriginalTick(db, warlock, state));
    }

    [InstallFact]
    public void TheSlapsResearchWindowIsSixtySecondsInTheShippedTable()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);

        // The variable this project had recorded as read by nothing. It is read
        // by the keeper's research adder, and only there - which is why looking
        // for it in the work-rate path came back empty (Q159).
        Assert.Equal(60, ResearchRules.SlapBonusSeconds(db));
        Assert.Equal(50, db[VariableTypeId.IncreasedWorkRatePecentageFromSlapping]);

        // A slapped level-1 Warlock: four instalments of 1 a second, plus a
        // once-a-second bonus of 4 x 50 x 4 x 1 / 100 = 8. Twelve against four,
        // where the variable is named 50%.
        var state = new CreatureState { Level = 1, LastSlapTick = 1 };
        CreatureDef warlock = db.CreatureByName("Warlock")!;
        int perTick = ResearchRules.PerOriginalTick(db, warlock, state);

        Assert.Equal(1, perTick);
        Assert.Equal(8, ResearchRules.SlapBonus(db, state, perTick, now: 2));
    }
}
