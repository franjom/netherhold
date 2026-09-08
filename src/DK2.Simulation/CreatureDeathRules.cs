// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Dying: in DK2 a process with two timers, not an event.</summary>
/// <remarks>
/// <para>Rule: a creature out of health collapses and lies there rescuable for
/// <c>CreatureDyingStateDurationSeconds</c> (60), in which an imp may carry it
/// to its own bed or, if an enemy, to a prison; then it dies and lies there as a
/// corpse for <c>DeadBodyDiesAfterSeconds</c> (also 60), in which an imp may
/// carry it to a graveyard; then it disappears. CONFIRMED (owner, Q99;
/// ADR-0051, ADR-0063).</para>
/// <para>A corpse is a creature in a state, not an object: none of the 145
/// OBJECTS records is a corpse (docs/research/objects.md §4). Not implemented:
/// what a graveyard does with its corpses, so no Vampire is made (Q165).</para>
/// </remarks>
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
    /// <para>CONFIRMED by the owner for the Imp, and generalised to all eight
    /// bodyless creatures (<see cref="LeavesBody"/>) by OpenKeeper, whose
    /// <c>HealthSystem.processHealthDepleted</c> tests
    /// <c>GENERATE_DEAD_BODY</c> and nothing else before any unconscious
    /// handling. One flag decides both halves, which is coherent: the dying
    /// state <i>is</i> a body on the floor, a live one.</para>
    /// </summary>
    public static bool HasDyingState(CreatureDef creature) => LeavesBody(creature);

    /// <summary>
    /// Whether this creature comes back —
    /// <see cref="CreatureFlags.CanBeResurrected"/>, on exactly the two Vampires.
    /// <para>CONFIRMED by the owner (Q113): a vampire reincarnates at the
    /// <b>Graveyard</b> and sleeps to recover, at the cost of one level; killed
    /// at level 1, or by a monk, it is permanently dead. The data agrees — the
    /// Graveyard's <c>CreatedCreatureId</c> is 8, the Vampire, and its object ids
    /// 31-34 are not the Vampire's <c>LairObjectId</c> of 26, so reincarnation is
    /// a Graveyard tile and not <see cref="Rescue"/>.</para>
    /// <para>Not implemented (Q111): no simulation reader; the monk exception
    /// needs the killer's identity, which needs combat.</para>
    /// </summary>
    public static bool CanBeResurrected(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.CanBeResurrected);

    /// <summary>Whether this creature is in the collapsed, rescuable window.</summary>
    public static bool IsDying(CreatureState creature) =>
        creature.Activity == CreatureActivity.Dying;

    /// <summary>
    /// Whether this creature is dead and lying on the floor as a corpse — the
    /// second window, and the one an imp carries to a graveyard.
    ///
    /// </summary>
    public static bool IsCorpse(CreatureState creature) =>
        creature.Activity == CreatureActivity.Dead;

    /// <summary>
    /// Whether this creature is off its feet: out of health, or already in the
    /// dying window. <b>This, and not <see cref="IsDying"/>, is what every
    /// system guards on.</b>
    /// <para><see cref="Systems.CreatureHealthSystem"/> books the collapse once
    /// a game second, leaving a window in which a creature has no health and is
    /// still <see cref="CreatureActivity.Idle"/>. Guarding on <c>IsDying</c>
    /// alone let a creature at zero health walk to its bed under
    /// <c>CreatureSleepsWhenBelowPercentHealth</c> and heal, and let an Imp
    /// regenerate 67 a second on its own land back to 500. Guarding on "has no
    /// health" closes it without needing the collapse to be instantaneous.</para>
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
    /// <c>DeadBodyDiesAfterSeconds</c>, which is also <b>60</b>. The window an
    /// imp has to carry it to a graveyard (ADR-0063,
    /// <see cref="CarryRules"/>).
    /// </summary>
    public static int BodyWindowTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.DeadBodyDiesAfterSeconds]);

    /// <summary>Whether the dying window has run out and this creature is now dead.</summary>
    public static bool DyingWindowElapsed(GameDatabase database, CreatureState creature) =>
        creature.DyingTicks >= DyingWindowTicks(database);

    /// <summary>Whether a corpse has lain there long enough to disappear.</summary>
    public static bool BodyWindowElapsed(GameDatabase database, CreatureState creature) =>
        creature.DyingTicks >= BodyWindowTicks(database);

    /// <summary>
    /// Turns a collapsed creature into a corpse: the second state of the
    /// owner's pipeline, and the one that gives a graveyard something to be for.
    ///
    /// <para>Only for creatures that leave a body (<see cref="LeavesBody"/>): an
    /// Imp and a Vampire skip this and are removed outright. The bed is released
    /// here rather than on collapse, which is what gives a rescue somewhere to
    /// carry the creature <i>to</i>.</para>
    /// </summary>
    public static void BecomeCorpse(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        LairRules.ReleaseBed(world, database, creature);

        creature.Activity = CreatureActivity.Dead;

        // A fresh sixty seconds: the corpse and rescue windows share one field,
        // so a corpse would inherit a spent clock without this.
        creature.DyingTicks = 0;

        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.Path.Clear();
        creature.PathIndex = 0;
    }

    /// <summary>
    /// Whether this creature leaves a corpse behind —
    /// <see cref="CreatureFlags.GenerateDeadBody"/>.
    /// <para>Eight of the 46 leave nothing: Imp, Vampire, Skeleton, Horny, Stone
    /// Knight, Unique Skeleton, Unique Vampire and Jack. Nearly, and not exactly,
    /// the set that never gets hungry — which holds the Unique Firefly (it leaves
    /// a body) and not Jack (it eats). There is no undead flag;
    /// <c>CreatureNeedsOnRealDataTests</c> asserts all three sets by name.</para>
    /// <para>Trap: this flag governs and <c>DecomposeValue</c> does not. Horny
    /// and Jack carry a decompose value of 200 while generating no body, so a
    /// graveyard counting it would be fed by corpses that never exist.</para>
    /// </summary>
    public static bool LeavesBody(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.GenerateDeadBody);

    /// <summary>
    /// Drops a creature to the ground at zero health, starting the rescue
    /// window. Returns false if it was already dying.
    /// <para>It keeps its bed on purpose: the rescue path is an imp carrying the
    /// creature <i>to that bed</i>, so a lair tile freed on collapse would be
    /// claimed by somebody else while its owner lay on the floor. The bed is
    /// released at death, by <see cref="Systems.CreatureHealthSystem"/> or
    /// <see cref="BecomeCorpse"/>.</para>
    /// <para>It keeps its gold too: DK2 drops what a creature carried when the
    /// creature is <i>destroyed</i>, not when it collapses, and so does this
    /// (<see cref="FloorGold.DropFrom"/>).</para>
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
    /// Puts a rescued creature into its own bed, where it recovers. Returns
    /// false if it was not dying or has no bed — a bed sold while its owner lay
    /// dying is not replaced, so such a creature cannot be rescued at all;
    /// UNKNOWN whether DK2 agrees (Q107).
    /// <para>Adds no health: it wakes at zero and the lair heals it at
    /// <c>ModifyHealthOfCreatureInLairPerSecond</c> (100 a second), being now
    /// <see cref="CreatureActivity.Sleeping"/> in its own bed (ADR-0049). So the
    /// order in <see cref="Systems.CreatureHealthSystem"/> is load-bearing:
    /// healing runs <i>before</i> the collapse test, or a rescue would collapse
    /// what it saved.</para>
    /// </summary>
    public static bool Rescue(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (!IsDying(creature)) return false;

        int bed = LairRules.BedTileIndex(world, database, creature);
        if (bed < 0) return false;
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
