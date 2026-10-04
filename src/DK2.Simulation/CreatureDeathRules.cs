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
/// <para>A corpse is no OBJECTS record; DK2 makes a <c>CDeadBody</c> thing, and ours keeps the creature in
/// <see cref="CreatureActivity.Dead"/> with no state id (ADR-0206). Burial feeds the per-room tally in
/// <see cref="GraveyardRules"/> (Q165).</para></remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-death.md")]
public static class CreatureDeathRules
{
    /// <summary>State 216, the fall, which the rule admits from any state; its handler moves to
    /// <see cref="DyingState"/> once the fall's action ends (ADR-0206).</summary>
    public const int FallingState = 216;

    /// <summary>State 217, lying while the countdown runs; healed above zero, it leaves for 1.</summary>
    public const int DyingState = 217;

    /// <summary>Whether this creature has run out of health — not the same as dead.</summary>
    public static bool HasCollapsed(CreatureState creature) => creature.Health <= 0;

    /// <summary>Whether running out of health puts this creature on the floor at all, or simply
    /// ends it. <b>Four gates, in DK2's own order</b>, read from <c>ModifyHealth</c>
    /// (<c>0x004B2CE0</c>) between <c>0x004B2F2E</c> and <c>0x004B2F96</c> — Q303,
    /// docs/research/creature-death.md §2.</summary>
    /// <remarks>
    /// <para>The last two are the quirk: a creature <b>attached to a room</b>
    /// (<see cref="AttachedRoom"/>) is judged by the room alone and <see cref="DiesInstantly"/>
    /// is never asked. Standing in a room is not being attached to it.</para>
    /// <para><see cref="LeavesBody"/> alone was the whole rule until Q303, and it is the first
    /// gate rather than a wrong one — <c>0x004B2F2E</c> tests the same
    /// <c>GenerateDeadBody</c> bit, definition flags bit 15.</para>
    /// </remarks>
    public static bool HasDyingState(
        WorldState world, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (!LeavesBody(creature)) return false;
        if (IsPossessed(world, state)) return false;

        RoomInstance? room = AttachedRoom(world, database, state);

        return room is not null
            ? !RoomRefusesTheDyingWindow(room.Definition)
            : !DiesInstantly(creature, state);
    }

    /// <summary>The room this creature is attached to, DK2's <c>RoomId</c> at <c>+0x3B9</c>, which
    /// the gates read (<c>0x004B2F61</c>) — not the room under its feet. A room's add-creature
    /// routine (<c>0x004E5A70</c>) writes it for a creature that joins the room as a user: its
    /// prisoners and torture victims, and a creature using it for its job. So a hero fighting
    /// on a keeper's Lair is attached to nothing and a Lord Of The Land dies outright there, as
    /// the Level 2 capture shows at 3241. Ours derives it: STRONGLY INFERRED from the writers.</summary>
    public static RoomInstance? AttachedRoom(WorldState world, GameDatabase database, CreatureState state)
    {
        int tile = -1;
        if (PrisonRules.IsImprisoned(state) || TortureRules.IsBeingTortured(state))
            tile = (state.Tile.Y * world.Width) + state.Tile.X;
        else if (state.CurrentJob != JobType.None && state.Activity == CreatureActivity.AtWork)
            tile = state.JobTileIndex;
        return tile < 0 ? null : RoomIndex.RoomAt(world, database, tile);
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
    /// (<c>0x004B2F37</c>): a possessed creature dies outright. PossessionRules
    /// maintains the keeper pointer together with the creature's flag.</summary>
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

    /// <summary>Whether this creature is off its feet: out of health, or already dying. Every system
    /// guards on this, not <see cref="IsDying"/>: the collapse is booked once a game second
    /// (<see cref="Systems.CreatureHealthSystem"/>), so a creature can have no health and still be idle;
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

        CreatureStateMachine.Release(creature);

        // A fresh sixty seconds: the corpse and rescue windows share one field.
        creature.DyingTicks = 0;

        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.StopMoving();
    }

    /// <summary>Whether this creature leaves a corpse behind —
    /// <see cref="CreatureFlags.GenerateDeadBody"/>. Eight of the 46 leave nothing: Imp, Vampire,
    /// Skeleton, Horny, Stone Knight, Unique Skeleton, Unique Vampire and Jack — nearly, not
    /// exactly, the set that never gets hungry, which holds the Unique Firefly and not Jack. There
    /// is no undead flag; <c>CreatureNeedsOnRealDataTests</c> asserts all three sets by name. Trap:
    /// this flag governs and <c>DecomposeValue</c> does not — Horny and Jack carry a decompose value of 200 while generating no body, so a graveyard counting it would be fed by corpses that never exist.</summary>
    public static bool LeavesBody(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.GenerateDeadBody);

    /// <summary>The death bookkeeping's gate (<c>0x00487C70</c>): the definition flag and its
    /// runtime copy, which mana-removal selection clears (<see cref="CreatureState.CorpseSuppressed"/>).
    /// <see cref="HasDyingState"/> reads the definition alone, as <c>ModifyHealth</c> does.</summary>
    public static bool LeavesBody(CreatureDef creature, CreatureState state) =>
        LeavesBody(creature) && !state.CorpseSuppressed;

    /// <summary>Drops a creature to the ground at zero health, starting the rescue window. Returns
    /// false if it was already dying. It keeps its bed on purpose: the rescue path is an imp
    /// carrying the creature to that bed, so a lair tile freed on collapse would be claimed by
    /// somebody else while its owner lay on the floor; the bed is released at death, by
    /// <see cref="Systems.CreatureHealthSystem"/> or <see cref="BecomeCorpse"/>. It keeps its gold too: DK2 drops what a creature carried when the creature is destroyed, not when it collapses (<see cref="FloorGold.DropFrom"/>).</summary>
    public static bool Collapse(in StateHookContext hooks, CreatureState creature)
    {
        if (IsDying(creature)) return false;

        if (CreatureStateMachine.TrySetState(hooks, creature, FallingState))
        {
            creature.TurncoatTicks = 0;
            creature.TurncoatPlayerId = 0;
        }
        creature.DyingTicks = 0;
        creature.SleepTicks = 0;
        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.StopMoving();
        return true;
    }

    /// <summary>Puts a rescued creature into its own bed, where it recovers. Returns false if it was
    /// not dying or has no bed — a bed sold while its owner lay dying is not replaced, so such a
    /// creature cannot be rescued at all; UNKNOWN whether DK2 agrees (Q107). Adds no health: asleep
    /// in its own bed, the lair heals it at 100 a second (ADR-0049). So the order in <see cref="Systems.CreatureHealthSystem"/> is load-bearing: healing runs before the collapse test, or a rescue would collapse what it saved.</summary>
    public static bool Rescue(in StateHookContext hooks, CreatureState creature)
    {
        WorldState world = hooks.World;
        if (!IsDying(creature)) return false;

        int bed = LairRules.BedTileIndex(world, hooks.Database, creature);
        if (bed < 0) return false;
        if (bed >= world.Width * world.Height) return false;

        creature.PosX = CreatureState.CentreOf(bed % world.Width);
        creature.PosY = CreatureState.CentreOf(bed / world.Width);
        creature.Body.Vel = Steering.Fixed16Vector.Zero;
        creature.DyingTicks = 0;
        creature.SleepTicks = 0;
        creature.AwakeTicks = 0;
        // 217's way out is SetState(1); ours then lies down in the bed, 83 (ADR-0206).
        CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle);
        CreatureStateMachine.TrySetState(hooks, creature, LairRules.SleepingState);
        return true;
    }
}
