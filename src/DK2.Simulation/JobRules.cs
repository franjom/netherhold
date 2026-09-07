// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What a non-worker's jobs need, and what doing one is worth.
///
/// <para><b>Read from the original's job dispatcher</b>, <c>FUN_00478800</c>
/// (2026-09-06, ADR-0082; <c>docs/research/job-selection.md</c>). Every one of
/// the three mood choosers ends up here: it takes a job id, switches to that
/// job's handler, and the handler returns non-zero if the creature took the
/// job. <b>That boolean is the whole of job availability</b> — the choosers do
/// not inspect rooms themselves, they simply try jobs until one says yes.</para>
///
/// <para><b>Every handler has the same three parts</b>, which is what makes this
/// class possible at all:</para>
///
/// <list type="number">
/// <item>A cheap test on the keeper or the creature — <i>does this player have a
/// library at all</i>, <i>is anything queued in the workshop</i>, <i>is this
/// creature below the training cap</i>. Some jobs have none.</item>
/// <item>A search for the nearest suitable room.</item>
/// <item>Entering that job's state, which can <b>still</b> fail — and when it
/// does, the job has failed and the chooser moves on.</item>
/// </list>
///
/// <para><b>One room type is confirmed outright rather than inferred</b>: the
/// Guard handler passes the literal <c>9</c> to its room search, and 9 is
/// <see cref="RoomTypeId.GuardRoom"/>. The others follow from which finder each
/// handler calls, and they are the obvious ones.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/job-selection.md")]
public static class JobRules
{
    /// <summary>
    /// The room a job is done in, or null when the job needs no room.
    ///
    /// <para><b>Sulk needs a Lair, which is the finding here</b> and is not
    /// what the name suggests. Its handler (<c>FUN_00482ef0</c>) refuses unless
    /// the creature sleeps at all, <i>has its own bed</i>, and that bed sits in
    /// a room — then walks it to that room and enters state <c>0x55</c>, one
    /// away from the sleep handler's <c>0x51</c> and by exactly the same route.
    /// <b>Sulking is going to your lair and moping in it</b>, and a creature
    /// with no bed cannot sulk at all — which matters, because every unhappy
    /// table in the game is Sulk and Drink.</para>
    /// </summary>
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
        _ => null,
    };

    /// <summary>
    /// Whether this job's <i>effect</i> is built, as opposed to merely its
    /// choosing and its walk.
    ///
    /// <para><b>Two are: Praying and Training.</b> Praying came first because
    /// since ADR-0081 the Temple is the only thing in the game that takes back a
    /// grievance it did not cause, so it was the one job whose absence left a
    /// hole in a mechanic that already existed. Training came second because it
    /// is the one job whose <i>worth</i> could be read (Q205) — experience per
    /// original tick, a level cap, and a bill to the keeper.</para>
    ///
    /// <para>Research, Guard, Drink, Manufacture and the rest are walked to and
    /// stood in, and say so in the log. Research and Manufacture are the nearest
    /// to buildable: their rates sit in the same four-field family as training's
    /// and go through the same loader divide, and their category hooks add the
    /// result to the <i>player</i> rather than to the creature. What is missing
    /// is a keeper who has anything to research or manufacture.</para>
    /// </summary>
    public static bool EffectIsBuilt(JobType job) =>
        job is JobType.Praying or JobType.Train;

    /// <summary>
    /// The keeper-or-creature test each handler makes before it looks for a
    /// room. Null means it passes; a non-null string is the reason it did not,
    /// and goes straight into the log.
    ///
    /// <para><b>Most jobs have no such test</b> — Praying's handler is three
    /// lines and goes straight to the temple search, and so do Research's
    /// sibling jobs once their room exists. The three that do are below, and
    /// each is read rather than supposed.</para>
    /// </summary>
    public static string? Refused(
        WorldState world, GameDatabase database, CreatureDef creature,
        CreatureState state, JobType job)
    {
        switch (job)
        {
            case JobType.Train:
                // Its handler makes TWO tests and this project long had only
                // one of them. THE GOLD TEST COMES FIRST - the original's
                // condition is `gold >= cost && level < cap`, in that order, so
                // a broke keeper's level-9 creature is refused for poverty
                // rather than for being too good. Cheap to get right and
                // impossible to notice later, because only the logged reason
                // differs (Q205).
                if (!TrainingRules.CanAfford(world, database, state.OwnerPlayerId))
                    return "keeper_cannot_pay";

                // "level < TrainingRoomMaxExperienceLevel", the original's
                // comparison against the shipped 4. Above it, the handler falls
                // through to a second call that is the combat pit's - unbuilt
                // here, and the reason a level-4 creature simply stops
                // training.
                return state.Level < database[VariableTypeId.TrainingRoomMaxExperienceLevel]
                    ? null
                    : "at_training_cap";

            case JobType.Manufacture:
                // The original tests a per-player field that is non-zero when
                // something is queued in the workshop. NOTHING IS EVER QUEUED
                // HERE, because traps and doors do not exist - so this refuses
                // always, and that is the faithful answer rather than a
                // shortcut. A Troll in a dungeon with no manufacturing order is
                // a Troll with nothing to do in DK2 too.
                return "nothing_queued";

            case JobType.Sulk or JobType.Sleep:
                // Its handler wants a bed of its own before it will walk
                // anywhere. See RoomFor.
                return LairRules.NeedsBed(creature) ? null : "needs_no_bed";

            default:
                return null;
        }
    }

    /// <summary>
    /// What one game second of praying is worth: <b>−275 to every one of the
    /// six anger counters</b>, and <b>+33 health</b>.
    ///
    /// <para>Both numbers are shipped and both are live — checked, because
    /// seven of their neighbours in the same block turned out to be read by
    /// nothing at all (ADR-0082). <c>ModifyCreatureAngerWhilePrayingPerSecond</c>
    /// has 32 read sites and <c>ModifyCreatureHealthWhilePrayingPerSecond</c>
    /// two.</para>
    ///
    /// <para><b>Six calls, not one</b>, and that is the whole point of the
    /// Temple: it is the only source in DK2 that drains a bucket it did not
    /// fill, so it is the only way a keeper takes back the resentment of a
    /// month of unpaid wages (<see cref="CreatureAnger"/>, ADR-0081).</para>
    /// </summary>
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

    /// <summary>
    /// How long a creature must wait between job selections that came to
    /// something: <b>five game seconds</b>, the same gate the angry chooser
    /// uses and literally the same expression in the binary — <c>5 ×</c>
    /// variable 19, in all three choosers.
    ///
    /// <para><b>And the shipped <c>MinimumJobDurationSeconds</c> is not it.</b>
    /// That variable reads 15, and a raw scan of <c>.text</c> for its global
    /// finds exactly one hit: the loader stub writing it. The choosers use a
    /// hardcoded five seconds instead. Two independent facts agreeing that the
    /// gate is five, one of them by being conspicuously unused.</para>
    /// </summary>
    public const int ReselectionGateTicks = CreatureAngerRules.ReselectionGateTicks;

    /// <summary>
    /// Which table a creature rolls on, by mood — and the <b>Angry</b> case is
    /// deliberately absent from this system: it belongs to
    /// <see cref="Systems.CreatureAngerSystem"/>, which has had it since
    /// ADR-0061 and where leaving the dungeon lives.
    /// </summary>
    public static IReadOnlyList<CreatureJobPreference> TableFor(
        CreatureDef creature, CreatureMood mood) => mood switch
    {
        CreatureMood.Content => creature.HappyJobs,
        CreatureMood.Unhappy => creature.UnhappyJobs,
        _ => creature.AngryJobs,
    };

    /// <summary>
    /// <b>Only a happy creature is angered by having nothing to do</b>, and the
    /// asymmetry is the original's.
    ///
    /// <para>The happy chooser ends, when every job in the table has failed,
    /// with a call charging <c>angerNoWork</c> (<c>def+0x6c7</c>). The unhappy
    /// chooser's identical tail is a bare <c>return 0</c>, and the angry one's
    /// too. So a creature that is <i>already</i> unhappy is never made crosser
    /// by idleness — the grievance belongs to the creature that was fine until
    /// its keeper ran out of work for it.</para>
    ///
    /// <para>The same shape as <c>AngerNoPay</c> having no treasury clause
    /// where both its siblings test for the room (ADR-0079): a plausible
    /// symmetry that the game does not have, and that would have been entirely
    /// natural to write.</para>
    /// </summary>
    public static bool ChargesNoWorkAnger(CreatureMood mood) => mood == CreatureMood.Content;
}
