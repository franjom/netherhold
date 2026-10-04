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
/// <para>Only the working half is here: training pays through the Train job
/// (ADR-0083) and nothing fights, so a Goblin earns nothing here. A working imp
/// gains 80 a game second, so its second level costs 3000 / 80 = 37.5 seconds
/// (ADR-0083 item 6, <c>CreatureLevelTests</c>). Trap: the rate variable
/// <c>ImpExperienceGainPerSecond</c> is 20 per <i>original tick</i>.</para>
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
            if (HandRules.IsHeld(state)) continue;
            if (PrisonRules.IsImprisoned(state)) continue;
            if (CreatureDeathRules.IsDown(state)) continue;
            if (state.Activity == CreatureActivity.Leaving) continue;

            int gained = CreatureLevelRules.WorkExperiencePerOriginalTick(
                database, creature, state);
            if (gained == 0) continue;

            int before = state.Level;
            int needed = CreatureLevelRules.ExperienceForNextLevel(database, creature, before);
            int banked = state.ExperiencePoints;
            if (!CreatureLevelRules.Grant(context.Hooks, creature, state, gained)) continue;

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
    }
}
