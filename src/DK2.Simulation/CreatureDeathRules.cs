// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Dying, which in DK2 is a process with two timers rather than an event —
/// CONFIRMED by the owner (Q99, 2026-09-04).
///
/// <para>The owner's description, which this file implements the first half of:
/// <i>"when a creature drops, its petals on the flower above its head are
/// exhausted, it remains lying on the ground for a period of time, that gives
/// imps an opportunity to return a creature to its bed in the lair for healing,
/// or to prison if it is an enemy creature. Once that timer runs out a creature
/// dies and its corpse remains there lying, that gives imps an opportunity to
/// carry it to graveyard. If that timer runs out and no one carries the corpse
/// it just disappears."</i></para>
///
/// <para><b>Both timers are in the shipped data, and both are 60 seconds.</b>
/// This is the answer landing exactly on two variables that were already parsed
/// and had nothing reading them:</para>
///
/// <list type="table">
/// <item><term><c>CreatureDyingStateDurationSeconds</c> (26) = 60</term>
/// <description>how long the collapsed creature lies there rescuable</description></item>
/// <item><term><c>DeadBodyDiesAfterSeconds</c> (23) = 60</term>
/// <description>how long its corpse lies there carryable</description></item>
/// </list>
///
/// <para>Two independently-named variables with the same value is the kind of
/// coincidence that would have made one of them look redundant. The owner's
/// answer says they are two different clocks on two different things, which is
/// why <see cref="BodyWindowTicks"/> is defined here and deliberately unused —
/// see below.</para>
///
/// <para><b>Where the line is drawn: the dying creature is a creature, the
/// corpse is not</b> (ADR-0051). A collapsed creature still has health, an
/// owner, a tile and a bed, and the way it recovers is by being healed — so it
/// stays in <see cref="WorldState.Creatures"/> and every rule that reads a
/// creature keeps working on it. A corpse has none of those. In DK2 it is an
/// <i>object</i>: it carries a <c>DecomposeValue</c> (200 for most, 0 for the
/// undead), imps haul it the way they haul gold, and a graveyard counts
/// <c>DecomposeValueNeededForVampire</c> (1000) of them to make a Vampire.
/// OBJECTS is unparsed (M6), so the corpse cannot be modelled yet — and
/// modelling it as a fake creature would put a thing with no needs into the
/// creature list, where every present and future system would have to remember
/// to skip it. So the second timer is recorded and not implemented, and the
/// death event names whether a body was owed.</para>
///
/// <para><b>What this costs today, stated plainly:</b> a creature that dies
/// vanishes at the end of the dying window instead of leaving a corpse for 60
/// more seconds, so nothing accumulates toward a graveyard and no Vampire can
/// ever be made. That is a known gap with a named cause, not an oversight.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-death.md")]
public static class CreatureDeathRules
{
    /// <summary>
    /// Whether this creature has run out of health. Not the same as dead — see
    /// the type summary.
    /// </summary>
    public static bool HasCollapsed(CreatureState creature) => creature.Health <= 0;

    /// <summary>
    /// Whether running out of health puts this creature on the floor at all, or
    /// simply ends it. <b>A creature that leaves no body has no dying state</b>
    /// — it vanishes on the spot.
    ///
    /// <para><b>CONFIRMED twice over, from two independent directions.</b> The
    /// owner: <i>"imps are dead right away, they don't have a lying state."</i>
    /// And OpenKeeper's <c>HealthSystem.processHealthDepleted</c> opens with
    /// exactly this test before any unconscious handling:</para>
    ///
    /// <code>
    /// // No body, just vanish from the world
    /// if (!isLeaveDeadBody(entityId)) { ...remove(); return; }
    /// </code>
    ///
    /// <para>...where <c>isLeaveDeadBody</c> reads the <c>GENERATE_DEAD_BODY</c>
    /// flag and nothing else. So the same flag decides both halves, which is
    /// coherent in a way worth saying out loud: <b>the dying state <i>is</i> a
    /// body on the floor</b> — a live one. A creature that leaves nothing behind
    /// when it dies had nothing to lie there in the first place.</para>
    ///
    /// <para>The owner's testimony covers the Imp; OpenKeeper generalises it to
    /// all eight bodyless creatures. The other seven are Vampire, Skeleton,
    /// Horny, Stone Knight, Unique Skeleton, Unique Vampire and Jack — a
    /// summon, three undead, a construct and Jack, all of which vanishing on
    /// the spot is unsurprising for.</para>
    /// </summary>
    public static bool HasDyingState(CreatureDef creature) => LeavesBody(creature);

    /// <summary>
    /// Whether this creature comes back — <see cref="CreatureFlags.CanBeResurrected"/>,
    /// which is set on <b>exactly the two Vampires and nobody else</b> in the
    /// shipped data.
    ///
    /// <para><b>Nothing reads this, and the reason is that reading it would
    /// make vampires immortal.</b> The owner's rule (2026-09-04):</para>
    ///
    /// <para><i>"vampires also don't drop a body, they reincarnate at their
    /// sarcophagus and sleep to recover, but they lose a level once they
    /// reincarnate. If they were killed at level 1 they are permanently dead.
    /// Exception to that rule for vampires are monks: if a monk kills a vampire
    /// it is permanently dead, regardless of its level."</i></para>
    ///
    /// <para><b>One confirmation falls out of that, and one mistake was made
    /// out of it.</b> The vampire having no dead body stops being an oddity:
    /// there is no corpse because the creature is not finished. But this
    /// comment used to add that the <i>sarcophagus is the vampire's lair
    /// bed</i>, so that reincarnation would be <see cref="Rescue"/> with no imp
    /// involved — and that was a pun on the word, not a finding. The owner
    /// retracted the wording it rested on the same day: <i>"sarchopagus at the
    /// graveyard, I made a wrong claim earlier."</i></para>
    ///
    /// <para><b>A vampire comes back at the Graveyard</b> (Q113, closed), and
    /// the shipped data says so in two places that were never consulted while
    /// the wrong claim stood: the <b>Graveyard's <c>CreatedCreatureId</c> is 8,
    /// the Vampire</b> — one of only two rooms in the table that create a
    /// creature, the other being the Prison's Skeleton — and the Graveyard's
    /// object ids are <b>31-34</b> against the Vampire's <c>LairObjectId</c> of
    /// <b>26</b>. Two different objects. The binary agrees about the vocabulary:
    /// <c>Object.cpp</c>'s union names <c>Lair</c> and <c>TombStone</c>, and the
    /// words <i>sarcophagus</i> and <i>coffin</i> appear nowhere in the
    /// executable's strings (ADR-0046 — facts only). A bed is a <c>Lair</c>
    /// object whatever art it wears.</para>
    ///
    /// <para>So the day this is implemented it needs a <b>Graveyard tile</b> to
    /// put the creature on, not <see cref="Rescue"/> — which also makes the
    /// wiki's third permanent-death condition (no Graveyard under your control)
    /// corroborated rather than merely repeated.</para>
    ///
    /// <para><b>Why it stays unimplemented.</b> The cost of reincarnation is a
    /// level, and levels do not exist. A vampire that reincarnated without
    /// paying would come back for ever — the immortal Imp again, in a cape. And
    /// the two ways it ends are both out of reach: level 1 needs levels, and
    /// "a monk killed it" needs the killer's identity, which needs combat
    /// (M7).</para>
    ///
    /// <para><b>So instant death is not a placeholder here — it is correct for
    /// the world that currently exists.</b> With no levels, every creature is
    /// level 1, and a level-1 vampire is permanently dead by the owner's own
    /// rule. The behaviour is right; it will need this flag the day levels
    /// arrive, and not before. Q111.</para>
    /// </summary>
    public static bool CanBeResurrected(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.CanBeResurrected);

    /// <summary>Whether this creature is in the collapsed, rescuable window.</summary>
    public static bool IsDying(CreatureState creature) =>
        creature.Activity == CreatureActivity.Dying;

    /// <summary>
    /// Whether this creature is off its feet: out of health, or already in the
    /// dying window. <b>This, and not <see cref="IsDying"/>, is what every
    /// system guards on.</b>
    ///
    /// <para>The distinction is a tick of bookkeeping and it was a real bug.
    /// <see cref="Systems.CreatureHealthSystem"/> books the collapse once a game
    /// second, so between health reaching zero and the collapse being recorded
    /// there is a window in which a creature has no health and is still
    /// <see cref="CreatureActivity.Idle"/>. Guarding on <c>IsDying</c> alone let
    /// things act in that window, and two of them did something absurd:</para>
    ///
    /// <list type="bullet">
    /// <item>A creature at zero health standing near its bed decided it was
    /// wounded enough to sleep (<c>CreatureSleepsWhenBelowPercentHealth</c>),
    /// walked there, and healed — so it <b>never collapsed at all</b> and the
    /// dying window was skipped whenever a bed was in reach.</item>
    /// <item>An Imp at zero health on its own land regenerated its 67 a second
    /// before the collapse was booked, so it climbed back to 500 and could not
    /// be killed at home.</item>
    /// </list>
    ///
    /// <para>Both were found by tests written for the second one. Guarding on
    /// "has no health" rather than "has been marked as dying" closes the window
    /// without needing the collapse to be instantaneous, which matters because
    /// combat will subtract health per tick rather than per second.</para>
    /// </summary>
    public static bool IsDown(CreatureState creature) =>
        HasCollapsed(creature) || IsDying(creature);

    /// <summary>
    /// How long a collapsed creature lies there before it actually dies —
    /// <c>CreatureDyingStateDurationSeconds</c>, which is <b>60</b>.
    ///
    /// <para>Read from the database rather than hard-coded because it is a
    /// balance variable and levels are free to differ.</para>
    /// </summary>
    public static int DyingWindowTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.CreatureDyingStateDurationSeconds]);

    /// <summary>
    /// How long a corpse lies there before it rots away —
    /// <c>DeadBodyDiesAfterSeconds</c>, which is also <b>60</b>.
    ///
    /// <para><b>Nothing calls this, and that is the point.</b> It is the second
    /// half of the owner's answer, it is a real number in the shipped data, and
    /// it cannot be applied to anything until corpses exist as objects (M6). It
    /// is defined here so that the half of the mechanic that is missing is
    /// missing <i>visibly</i>, at the place the rest of it lives, rather than
    /// being rediscovered from the variable table later.</para>
    /// </summary>
    public static int BodyWindowTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.DeadBodyDiesAfterSeconds]);

    /// <summary>Whether the dying window has run out and this creature is now dead.</summary>
    public static bool DyingWindowElapsed(GameDatabase database, CreatureState creature) =>
        creature.DyingTicks >= DyingWindowTicks(database);

    /// <summary>
    /// Whether this creature leaves a corpse behind —
    /// <see cref="CreatureFlags.GenerateDeadBody"/>.
    ///
    /// <para><b>Eight of the 46 leave nothing:</b> the Imp, Vampire, Skeleton,
    /// Horny, Stone Knight, Unique Skeleton, Unique Vampire and <b>Jack</b>.
    /// Which is nearly, and importantly not exactly, the set that never gets
    /// hungry — that one contains the <b>Unique Firefly</b> (which does leave a
    /// body) and not Jack (which eats). The two sets differ by exactly one
    /// member each way.</para>
    ///
    /// <para>So this is the <b>third</b> near-miss grouping in the same record,
    /// after never-eats and never-sleeps, and the third time the tempting
    /// shortcut — "these are the undead, one flag would do" — is refuted by a
    /// single creature. There is no undead flag. Each need is its own column,
    /// and <c>CreatureNeedsOnRealDataTests</c> asserts all three sets by name
    /// because every one of them was guessed wrong before it was read.</para>
    ///
    /// <para>The Vampire having no body is the coherent one: a vampire is what
    /// bodies are spent to make.</para>
    ///
    /// <para><b>This flag governs, and <c>DecomposeValue</c> does not.</b> The
    /// two disagree for exactly two creatures: <b>Horny</b> and <b>Jack</b> both
    /// carry a decompose value of 200 while generating no body, so those 200s
    /// can never be read by anything. Both are records where a template value
    /// would plausibly survive unedited — Horny is a summon rather than a roster
    /// creature (CONFIRMED by the owner) and Jack's record runs backwards in
    /// several other places. A graveyard that counted <c>DecomposeValue</c>
    /// instead of asking this question would be fed by two corpses that never
    /// exist.</para>
    /// </summary>
    public static bool LeavesBody(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.GenerateDeadBody);

    /// <summary>
    /// Drops a creature to the ground at zero health, starting the rescue
    /// window. Returns false if it was already dying.
    ///
    /// <para><b>It keeps its bed.</b> Releasing it here would be the obvious
    /// tidy-up and it would be wrong: the owner's rescue path is an imp carrying
    /// the creature <i>to that bed</i>, so a lair tile freed on collapse would
    /// be a tile some other creature claims while its owner is lying on the
    /// floor. The bed is released at death, in
    /// <see cref="Systems.CreatureHealthSystem"/>.</para>
    ///
    /// <para><b>It keeps its gold, too.</b> DK2 makes a dying imp drop what it
    /// carried, and floor gold is an object (M6) — so the gold stays on the
    /// creature and is reported lost when it dies, rather than being
    /// invented onto a tile.</para>
    /// </summary>
    public static bool Collapse(CreatureState creature)
    {
        if (IsDying(creature)) return false;

        creature.Activity = CreatureActivity.Dying;
        creature.DyingTicks = 0;
        creature.SleepTicks = 0;
        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.Path.Clear();
        creature.PathIndex = 0;
        return true;
    }

    /// <summary>
    /// Puts a rescued creature into its own bed, which is where it recovers.
    /// Returns false if it was not dying, or has no bed to be put into.
    ///
    /// <para><b>Nothing calls this yet</b> — an imp cannot pick anything up, and
    /// that is the other missing half of M4 — and it is here for the same reason
    /// <see cref="CreatureHealthRules.Eat"/> is: the transition is the part worth
    /// getting right, it can be tested now, and waiting for carrying would mean
    /// writing it blind.</para>
    ///
    /// <para><b>Note what it does not do: it does not add any health.</b> The
    /// creature wakes up at zero and is healed by the lair, at
    /// <c>ModifyHealthOfCreatureInLairPerSecond</c> (100 a second), because it
    /// is now <see cref="CreatureActivity.Sleeping"/> in its own bed and that is
    /// the only state a lair heals in (ADR-0049). Which makes the order inside
    /// <see cref="Systems.CreatureHealthSystem"/> load-bearing: healing is
    /// applied <i>before</i> the collapse test, so a creature rescued at zero
    /// health is above zero by the time anything asks whether it has collapsed.
    /// Reverse those two and a rescue would collapse the creature it just
    /// saved.</para>
    /// </summary>
    /// <remarks>
    /// A creature whose bed was sold out from under it while it lay dying has
    /// nowhere to be carried and cannot be rescued at all — see the guard in
    /// <see cref="Systems.CreatureSleepSystem"/> for why a collapsed creature is
    /// not allowed to claim a replacement, and Q107 for the observation that
    /// would settle it.
    /// </remarks>
    public static bool Rescue(WorldState world, CreatureState creature)
    {
        if (!IsDying(creature)) return false;
        if (creature.LairTileIndex < 0) return false;

        int bed = creature.LairTileIndex;
        if (bed >= world.Width * world.Height) return false;

        creature.PosX = CreatureState.CentreOf(bed % world.Width);
        creature.PosY = CreatureState.CentreOf(bed / world.Width);
        creature.MoveRemainder = 0;
        creature.DyingTicks = 0;
        creature.SleepTicks = 0;
        creature.AwakeTicks = 0;
        creature.Activity = CreatureActivity.Sleeping;
        return true;
    }
}
