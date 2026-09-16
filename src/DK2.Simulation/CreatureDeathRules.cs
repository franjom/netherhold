// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Dying: in DK2 a process with two timers, not an event.</summary>
/// <remarks>
/// <para>Rule: a creature out of health collapses and lies there rescuable for
/// <c>CreatureDyingStateDurationSeconds</c> (60), in which an imp may carry it to its own bed or,
/// if an enemy, to a prison; then it dies and lies as a corpse for <c>DeadBodyDiesAfterSeconds</c>
/// (also 60), in which an imp may carry it to a graveyard; then it disappears. CONFIRMED (owner,
/// Q99; ADR-0051, ADR-0063).</para>
/// <para>A corpse is a creature in a state, not an object: none of the 145 OBJECTS records is a corpse (objects.md §4). Burial feeds the per-room tally in <see cref="GraveyardRules"/> (Q165).</para></remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-death.md")]
public static class CreatureDeathRules
{
    /// <summary>Whether this creature has run out of health — not the same as dead.</summary>
    public static bool HasCollapsed(CreatureState creature) => creature.Health <= 0;

    /// <summary>Whether running out of health puts this creature on the floor at all, or simply
    /// ends it. <b>Four gates, in DK2's own order</b>, read from <c>ModifyHealth</c>
    /// (<c>0x004B2CE0</c>) between <c>0x004B2F2E</c> and <c>0x004B2F96</c> — Q303,
    /// docs/research/creature-death.md §2.</summary>
    /// <remarks>
    /// <para>The last two are the quirk: a creature <b>standing in a room</b> is judged by the
    /// room alone and <see cref="DiesInstantly"/> is never asked, so a Lord Of The Land killed
    /// in a Treasury lies there rescuable and one killed in a corridor does not.</para>
    /// <para><see cref="LeavesBody"/> alone was the whole rule until Q303, and it is the first
    /// gate rather than a wrong one — <c>0x004B2F2E</c> tests the same
    /// <c>GenerateDeadBody</c> bit, definition flags bit 15.</para>
    /// </remarks>
    public static bool HasDyingState(
        WorldState world, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (!LeavesBody(creature)) return false;
        if (IsPossessed(world, state)) return false;

        RoomInstance? room = RoomIndex.RoomAt(
            world, database, (state.Tile.Y * world.Width) + state.Tile.X);

        return room is not null
            ? !RoomRefusesTheDyingWindow(room.Definition)
            : !DiesInstantly(creature, state);
    }

    /// <summary>The two rooms that kill outright: a creature dying on their tiles gets no
    /// collapsed window. <c>ModifyHealth</c> reads the room's <c>TypeId</c> at <c>+0x18</c>
    /// and refuses kinds 11 and 12 (<c>0x004B2F81</c>), which are the Prison and the Torture
    /// Chamber. Both have their own way to end a creature — the Prison's is built
    /// (ADR-0064, a Skeleton for the jailer) and the Torture Chamber's is not.</summary>
    public static bool RoomRefusesTheDyingWindow(RoomDef room) =>
        room.RoomId is (byte)RoomTypeId.Prison or (byte)RoomTypeId.TortureChamber;

    /// <summary>Whether this creature skips the collapsed window when it dies out of doors.
    /// <c>0x0048E110</c> answers true for creature types 21, 28 and 29 — King Reginald,
    /// Stone Knight and Lord Of The Land — <b>before</b> it looks at the flag at all, so
    /// those three carry it whatever a script asked for.</summary>
    public static bool DiesInstantly(CreatureDef creature, CreatureState state) =>
        creature.CreatureId is 21 or 28 or 29 || state.DieInstantly;

    /// <summary>Whether any keeper is possessing this creature. Its own gate in DK2
    /// (<c>0x004B2F37</c>): a possessed creature dies outright. Always false today, because
    /// <see cref="WorldState.PlayerState.PossessedCreatureId"/> is an interface act nothing
    /// here performs — the same standing as condition type 73 (ADR-0121).</summary>
    public static bool IsPossessed(WorldState world, CreatureState state)
    {
        for (int i = 0; i < world.PlayerCount; i++)
            if (world.PlayerAtIndex(i).PossessedCreatureId == state.Id) return true;

        return false;
    }

    /// <summary>Whether this creature comes back — <see cref="CreatureFlags.CanBeResurrected"/>, on
    /// exactly the two Vampires. CONFIRMED (owner, Q113): a vampire reincarnates at the Graveyard
    /// and sleeps to recover at the cost of one level; killed at level 1, or by a monk, it is
    /// permanently dead. The data agrees — the Graveyard's <c>CreatedCreatureId</c> is 8, the
    /// Vampire, and its object ids 31-34 are not the Vampire's <c>LairObjectId</c> of 26, so
    /// reincarnation is a Graveyard tile and not <see cref="Rescue"/>. Not implemented (Q111): no simulation reader, and the monk exception needs the killer's identity, which needs combat.</summary>
    public static bool CanBeResurrected(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.CanBeResurrected);

    /// <summary>Whether this creature is in the collapsed, rescuable window.</summary>
    public static bool IsDying(CreatureState creature) =>
        creature.Activity == CreatureActivity.Dying;

    /// <summary>Whether this creature is dead and lying as a corpse — the second window, and the one an imp carries to a graveyard.</summary>
    public static bool IsCorpse(CreatureState creature) =>
        creature.Activity == CreatureActivity.Dead;

    /// <summary>Whether this creature is off its feet: out of health, or already in the dying
    /// window. This, not <see cref="IsDying"/>, is what every system guards on.
    /// <see cref="Systems.CreatureHealthSystem"/> books the collapse once a game second, leaving a
    /// window in which a creature has no health and is still <see cref="CreatureActivity.Idle"/>;
    /// guarding on <c>IsDying</c> alone let a creature at zero health walk to its bed under
    /// <c>CreatureSleepsWhenBelowPercentHealth</c> and heal, and let an Imp regenerate 67 a second on its own land back to 500.</summary>
    public static bool IsDown(CreatureState creature) =>
        HasCollapsed(creature) || IsDying(creature);

    /// <summary>How long a collapsed creature lies there before it dies —
    /// <c>CreatureDyingStateDurationSeconds</c>, 60. From the database: levels are free to differ.</summary>
    public static int DyingWindowTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.CreatureDyingStateDurationSeconds]);

    /// <summary>How long a corpse lies there before it rots away — <c>DeadBodyDiesAfterSeconds</c>,
    /// also 60. The window an imp has to carry it to a graveyard (ADR-0063, <see cref="CarryRules"/>).</summary>
    public static int BodyWindowTicks(GameDatabase database) =>
        SimulationTiming.TicksFromSeconds(
            database[VariableTypeId.DeadBodyDiesAfterSeconds]);

    /// <summary>Whether the dying window has run out and this creature is now dead.</summary>
    public static bool DyingWindowElapsed(GameDatabase database, CreatureState creature) =>
        creature.DyingTicks >= DyingWindowTicks(database);

    /// <summary>Whether a corpse has lain there long enough to disappear.</summary>
    public static bool BodyWindowElapsed(GameDatabase database, CreatureState creature) =>
        creature.DyingTicks >= BodyWindowTicks(database);

    /// <summary>Turns a collapsed creature into a corpse: the second state of the owner's pipeline.
    /// Only for creatures that leave a body (<see cref="LeavesBody"/>) — an Imp and a Vampire skip
    /// it and are removed outright. The bed is released here rather than on collapse, which is what
    /// gives a rescue somewhere to carry the creature to.</summary>
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

    /// <summary>Whether this creature leaves a corpse behind —
    /// <see cref="CreatureFlags.GenerateDeadBody"/>. Eight of the 46 leave nothing: Imp, Vampire,
    /// Skeleton, Horny, Stone Knight, Unique Skeleton, Unique Vampire and Jack — nearly, not
    /// exactly, the set that never gets hungry, which holds the Unique Firefly and not Jack. There
    /// is no undead flag; <c>CreatureNeedsOnRealDataTests</c> asserts all three sets by name. Trap:
    /// this flag governs and <c>DecomposeValue</c> does not — Horny and Jack carry a decompose value of 200 while generating no body, so a graveyard counting it would be fed by corpses that never exist.</summary>
    public static bool LeavesBody(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.GenerateDeadBody);

    /// <summary>Drops a creature to the ground at zero health, starting the rescue window. Returns
    /// false if it was already dying. It keeps its bed on purpose: the rescue path is an imp
    /// carrying the creature to that bed, so a lair tile freed on collapse would be claimed by
    /// somebody else while its owner lay on the floor; the bed is released at death, by
    /// <see cref="Systems.CreatureHealthSystem"/> or <see cref="BecomeCorpse"/>. It keeps its gold too: DK2 drops what a creature carried when the creature is destroyed, not when it collapses (<see cref="FloorGold.DropFrom"/>).</summary>
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

    /// <summary>Puts a rescued creature into its own bed, where it recovers. Returns false if it was
    /// not dying or has no bed — a bed sold while its owner lay dying is not replaced, so such a
    /// creature cannot be rescued at all; UNKNOWN whether DK2 agrees (Q107). Adds no health: it
    /// wakes at zero and the lair heals it at <c>ModifyHealthOfCreatureInLairPerSecond</c> (100 a
    /// second), being now <see cref="CreatureActivity.Sleeping"/> in its own bed (ADR-0049). So the
    /// order in <see cref="Systems.CreatureHealthSystem"/> is load-bearing: healing runs before the collapse test, or a rescue would collapse what it saved.</summary>
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
