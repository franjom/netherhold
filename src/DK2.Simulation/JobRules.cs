// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a non-worker's jobs need, and what doing one is worth. Read from the
/// original's job dispatcher <c>FUN_00478800</c> (ADR-0082, docs/research/job-selection.md):
/// each mood chooser calls a job's handler, and <b>the handler's boolean is the whole of
/// job availability</b> — the choosers never inspect rooms, they try jobs until one says
/// yes. Every handler is a cheap keeper-or-creature test (some have none), a search for
/// the nearest suitable room, then entering the state, which can still fail.
/// <para>The job-to-room map is CONFIRMED twice over: the Guard handler passes the
/// literal 9 (<see cref="RoomTypeId.GuardRoom"/>), and DK2's per-room-type handler table
/// carries a job id in its last slot, giving eight agreements plus Prison→<see cref="JobType.Wait"/>
/// (Q210). That table is one dispatch path, not the whole map — Lair and Combat Pit
/// carry no job id, so <see cref="JobType.Sleep"/>, <see cref="JobType.Sulk"/> and
/// <see cref="JobType.CombatPitSpectate"/> are reached another way.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/job-selection.md")]
public static class JobRules
{
    /// <summary>The room a job is done in, or null when the job needs none. <b>Sulk
    /// needs a Lair</b>: its handler (<c>FUN_00482ef0</c>) refuses unless the creature
    /// sleeps, has its own bed and that bed sits in a room, then enters state <c>0x55</c>
    /// beside sleep's <c>0x51</c> — so a bedless creature cannot sulk, and every unhappy table is Sulk and Drink.</summary>
    public static RoomTypeId? RoomFor(JobType job) => job switch
    {
        JobType.Sleep or JobType.Sulk => RoomTypeId.Lair,
        JobType.Eat => RoomTypeId.Hatchery,
        JobType.Research => RoomTypeId.Library,
        JobType.Train => RoomTypeId.TrainingRoom,
        JobType.Manufacture => RoomTypeId.Workshop,
        JobType.Guard => RoomTypeId.GuardRoom,
        JobType.Torture => RoomTypeId.TortureChamber,
        JobType.Praying => RoomTypeId.Temple,
        JobType.Drink => RoomTypeId.Casino,
        JobType.CombatPitSpectate => RoomTypeId.CombatPit,

        // From DK2's own room -> job table (Q210) and unreachable today: no creature's
        // job tables list Wait. Present because the table states it.
        JobType.Wait => RoomTypeId.Prison,

        _ => null,
    };

    /// <summary>Whether this job's <i>effect</i> is built, as opposed to its choosing
    /// and its walk. <b>Two are: Praying and Training</b> (ADR-0081; Q205). Not
    /// implemented: Research, Guard, Drink, Manufacture and the rest are walked to and
    /// stood in, and say so in the log. Research and Manufacture are nearest to buildable — their rates are in training's four-field family through the same loader divide, crediting the <i>player</i> — and what is missing is a keeper with anything to research.</summary>
    public static bool EffectIsBuilt(JobType job) =>
        job is JobType.Praying or JobType.Train;

    /// <summary>The keeper-or-creature test each handler makes before it looks for a
    /// room. Null means it passes; a non-null string is the reason and goes into the
    /// log. Most jobs have no such test; the three that do are below, each read.</summary>
    public static string? Refused(
        WorldState world, GameDatabase database, CreatureDef creature,
        CreatureState state, JobType job)
    {
        switch (job)
        {
            case JobType.Train:
                // Two tests, and the GOLD TEST COMES FIRST: the original's condition is
                // `gold >= cost && level < cap`, in that order, so a broke keeper's
                // level-9 creature is refused for poverty. Only the logged reason
                // differs, so getting it wrong is invisible (Q205).
                if (!TrainingRules.CanAfford(world, database, state.OwnerPlayerId))
                    return "keeper_cannot_pay";

                // `level < TrainingRoomMaxExperienceLevel`, against the shipped 4. Above
                // it the handler falls through to the combat pit's call — unbuilt, which
                // is why a level-4 creature simply stops training.
                return state.Level < database[VariableTypeId.TrainingRoomMaxExperienceLevel]
                    ? null
                    : "at_training_cap";

            case JobType.Manufacture:
                // The original tests a per-player field, non-zero when the workshop has
                // something queued. Nothing ever is here, traps and doors not existing,
                // so this always refuses — faithful, not a shortcut: a Troll with no
                // order has nothing to do in DK2 either.
                return "nothing_queued";

            case JobType.Sulk or JobType.Sleep:
                // Its handler wants a bed of its own before it walks anywhere (RoomFor).
                return LairRules.NeedsBed(creature) ? null : "needs_no_bed";

            default:
                return null;
        }
    }

    /// <summary>What one game second of praying is worth: <b>−275 to every one of the
    /// six anger counters</b> and <b>+33 health</b>, both shipped and both live — checked,
    /// seven neighbours in the same block having no reader (ADR-0082). <b>Six calls, not
    /// one</b>: the Temple is DK2's only source that drains a bucket it did not fill, so the only way a keeper takes back the resentment of unpaid wages (<see cref="CreatureAnger"/>, ADR-0081).</summary>
    public static CreatureAnger PrayingCalm(GameDatabase database)
    {
        int per = database[VariableTypeId.ModifyCreatureAngerWhilePrayingPerSecond];
        return new CreatureAnger
        {
            Company = per,
            Hunger = per,
            Lair = per,
            Work = per,
            Pay = per,
            Treatment = per,
        };
    }

    /// <summary>Health a praying creature gains a game second.</summary>
    public static int PrayingHeal(GameDatabase database) =>
        database[VariableTypeId.ModifyCreatureHealthWhilePrayingPerSecond];

    /// <summary>How long a creature waits between job selections that came to something:
    /// <b>five game seconds</b>, the same expression (<c>5 ×</c> variable 19) in all three
    /// choosers. The shipped <c>MinimumJobDurationSeconds</c> is <b>not</b> it — it reads 15 and a <c>.text</c> scan of its global finds only the loader stub writing it.</summary>
    public const int ReselectionGateTicks = CreatureAngerRules.ReselectionGateTicks;

    /// <summary>Which table a creature rolls on, by mood. The <b>Angry</b> case is
    /// deliberately elsewhere: <see cref="Systems.CreatureAngerSystem"/>, where leaving the dungeon lives (ADR-0061).</summary>
    public static IReadOnlyList<CreatureJobPreference> TableFor(
        CreatureDef creature, CreatureMood mood) => mood switch
    {
        CreatureMood.Content => creature.HappyJobs,
        CreatureMood.Unhappy => creature.UnhappyJobs,
        _ => creature.AngryJobs,
    };

    /// <summary><b>Only a happy creature is angered by having nothing to do</b>, and the
    /// asymmetry is the original's: with every job failed the happy chooser charges
    /// <c>angerNoWork</c> (<c>def+0x6c7</c>) where the unhappy and angry choosers have a
    /// bare <c>return 0</c>, so a creature already unhappy is never made crosser by idleness. Same shape as <c>AngerNoPay</c> having no treasury clause (ADR-0079).</summary>
    public static bool ChargesNoWorkAnger(CreatureMood mood) => mood == CreatureMood.Content;
}
