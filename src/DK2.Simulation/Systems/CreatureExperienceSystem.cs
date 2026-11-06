// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>Combat category experience before the creature's handler (<c>0047A870</c>),
/// and the shared work-hit award. Training pays through its job handler;
/// see creature-levels.md and combat-experience-order.md.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
public sealed class CreatureExperienceSystem : ICreatureUpdateStep
{
    private const string SystemName = "CreatureExperience";

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState state)
    {
        // 0047A870 pays each original update, before the state handler can leave combat.
        if (!SimulationTiming.OnOriginalTick(context.Tick.Value)) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        if (state.Possessed) return;
        CreatureDef creature = database.CreatureById(state.CreatureTypeId);

        // Preserve the inactive-creature gates used by the standalone host too.
        if (CreatureEligibility.IsExcluded(state, CreatureGuards.Held | CreatureGuards.Imprisoned | CreatureGuards.Down)) return;
        if (state.Activity == CreatureActivity.Leaving) return;

        int gained = CombatExperiencePerOriginalTick(world, database, creature, state);
        if (gained == 0) return;
        Award(context, creature, state, gained);
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
            $"health={state.Health}/{CreatureHealthRules.MaxHealth(database, creature, state)}");
    }

    /// <summary>The Combat category's update <c>0x0047A870</c>: in state 199 or 200, a creature that is
    /// not a worker, or is Good, and whose fight target is a live creature gains its record's
    /// <c>ExpPerSecond</c> made per-tick by the loader; above level 1 scaled by stat 15 and kept
    /// within 0 to 0xFFFE. So the hero Dwarf earns in a fight (owner, 2026-10-05) and the Imp never.
    /// The walk to the target is state 8, which fails the 199/200 test, so it earns nothing (ADR-0366
    /// amendment).</summary>
    public static int CombatExperiencePerOriginalTick(
        WorldState world, GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (state.Dk2StateId is not (Combat.CombatStates.Engaging or 200)) return 0;
        if (((creature.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker) && !((creature.Flags & CreatureFlags.IsGood) == CreatureFlags.IsGood)) return 0;
        if (state.FightTargetKind != Combat.FightTargetKind.Creature) return 0;
        CreatureState? target = world.CreatureById(state.FightTargetId);
        if (target is null || target.Dk2StateId == CreatureStateMachine.NoState) return 0;

        int perTick = SimulationTiming.LoaderRatePerTick(creature.ExpPerSecond);
        if (state.Level == 1) return perTick;
        int scaled = perTick * CreatureLevelRules.Percent(database, CreatureStatId.ExpPerSecond, state.Level) / 100;
        return Math.Clamp(scaled, 0, 0xFFFE);
    }
}
