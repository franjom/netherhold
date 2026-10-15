// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Awards work experience and the levels it buys, on the original's tick.
/// <para>CONFIRMED (owner, 2026-09-04, Q124): imps gain by working; others by
/// training and fighting, capped at 4 in the training room and 8 in the combat
/// pit — the shipped <c>TrainingRoomMaxExperienceLevel</c> and
/// <c>CombatPitMaxExperienceLevel</c> — with level 10 needing real fights.</para>
/// <para>The working half and the fighting half are here (<see cref="CombatExperiencePerOriginalTick"/>,
/// ADR-0366); training pays through the Train job (ADR-0083); work pays 20 a hit through
/// <see cref="AwardWork"/> (ADR-0386), so an imp's second level costs 150 hits.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
public sealed class CreatureExperienceSystem : ISimulationSystem
{
    private const string SystemName = "CreatureExperience";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        // On the original's tick, not once a game second: DK2 awards the raw 20
        // once per original tick and throws away the overshoot on promotion
        // (CreatureLevelRules.Grant), so four awards of 20 and one award of 80
        // leave different balances behind. The cadence is behaviour (Q79/Q205).
        if (!SimulationTiming.OnOriginalTick(context.Tick.Value)) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        foreach (CreatureState state in world.Creatures)
        {
            if (state.Possessed) continue;
            CreatureDef creature = database.CreatureById(state.CreatureTypeId);

            // Nothing earns while held in the hand (a held creature worked
            // none, ADR-0062), imprisoned, down on the floor (ADR-0051), or
            // walking out of the dungeon.
            if (CreatureEligibility.IsExcluded(state, CreatureGuards.Held | CreatureGuards.Imprisoned | CreatureGuards.Down)) continue;
            if (state.Activity == CreatureActivity.Leaving) continue;

            int gained = CombatExperiencePerOriginalTick(world, database, creature, state);
            if (gained == 0) continue;
            Award(context, creature, state, gained);
        }
    }

    /// <summary>One work hit's experience (<see cref="CreatureLevelRules.WorkExperiencePerHit"/>,
    /// ADR-0386), paid by <see cref="TileWorkSystem"/> for every hit whose work applied, to the
    /// creature that made it, worker or not (the room attack's reader pays a hero too).</summary>
    internal static void AwardWork(SimulationContext context, CreatureState state)
    {
        if (state.Level >= CreatureLevelRules.MaxLevel) return;
        Award(context, context.Database.CreatureById(state.CreatureTypeId), state,
            CreatureLevelRules.WorkExperiencePerHit(context.Database));
    }

    private static void Award(SimulationContext context, CreatureDef creature, CreatureState state, int gained)
    {
        GameDatabase database = context.Database;
        int before = state.Level;
        int needed = CreatureLevelRules.ExperienceForNextLevel(database, creature, before);
        int banked = state.ExperiencePoints;
        if (!CreatureLevelRules.Grant(context.Hooks, creature, state, gained)) return;

        // "discarded", not "spare": DK2 zeroes the balance on promotion, so
        // the overshoot is gone rather than carried (Q127).
        context.Emit(SystemName, "creature_level_up",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} {before}->{state.Level} " +
            $"discarded={banked + gained - needed} " +
            $"next_at={CreatureLevelRules.ExperienceForNextLevel(database, creature, state.Level)} " +
            $"health={state.Health}/{CreatureHealthRules.MaxHealth(database, creature, state)} " +
            $"speed={CreatureLevelRules.SpeedRaw(database, creature, state.Level)}");
    }

    /// <summary>The Combat category's update <c>0x0047A870</c>: in state 199 or 200, a creature that is
    /// not a worker, or is Good, and whose fight target is a live creature gains its record's
    /// <c>ExpPerSecond</c> made per-tick by the loader; above level 1 scaled by stat 15 and kept
    /// within 0 to 0xFFFE. So the hero Dwarf earns in a fight (owner, 2026-10-05) and the Imp never.
    /// The walk to the target is DK2's state 8, whose word fails the 199/200 test, so it earns
    /// nothing; ours keeps 199 and flags the walk (<see cref="CreatureState.Approaching"/>, ADR-0366 amendment).</summary>
    public static int CombatExperiencePerOriginalTick(
        WorldState world, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.Dk2StateId is not (Combat.CombatStates.Engaging or 200) || state.Approaching) return 0;
        if (creature.Flags.HasFlag(CreatureFlags.IsWorker) && !creature.Flags.HasFlag(CreatureFlags.IsGood)) return 0;
        if (state.FightTargetKind != Combat.FightTargetKind.Creature) return 0;
        CreatureState? target = world.CreatureById(state.FightTargetId);
        if (target is null || target.Dk2StateId == CreatureStateMachine.NoState) return 0;

        int perTick = SimulationTiming.LoaderRatePerTick(creature.ExpPerSecond);
        if (state.Level == 1) return perTick;
        int scaled = perTick * CreatureLevelRules.Percent(database, CreatureStatId.ExpPerSecond, state.Level) / 100;
        return Math.Clamp(scaled, 0, 0xFFFE);
    }
}
