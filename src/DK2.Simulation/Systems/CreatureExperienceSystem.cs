// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Experience, and the levels it buys — once a game second.
///
/// <para><b>CONFIRMED by the owner</b> (2026-09-04, Q124), who answered the
/// question this system existed to be blocked on:</para>
///
/// <para><i>"imps get experience by working, while they do their usual things
/// around the dungeon, digging, reinforcing walls, mining gold, etc. they get
/// exp and passively their level raises. creatures also gain levels through
/// experience, mostly training and fighting. creatures can get up to level 4
/// using the training room, up to level 8 using the combat pit, and to get to
/// level 10 they need to fight actual fights."</i></para>
///
/// <para><b>Two of those caps are shipped variables, to the number</b> —
/// <c>TrainingRoomMaxExperienceLevel</c> is <b>4</b> and
/// <c>CombatPitMaxExperienceLevel</c> is <b>8</b>. That is the third time an
/// owner statement and a variable name have met outright, and it is why the
/// third clause (level 10 needs real fights) is taken as read: the two numbers
/// that could be checked both checked out.</para>
///
/// <para><b>Only the imp half is implemented</b>, and that is not a shortcut —
/// it is the only half that has anything to act on. Working exists;
/// <see cref="CreatureActivity.Working"/> is what an imp is doing while it digs,
/// claims, reinforces and mines. Training and fighting do not exist at all, so a
/// Goblin here earns nothing and stays at level 1 for ever, which is honest and
/// is asserted by a test rather than papered over.</para>
///
/// <para><b>The rate is DK2's own and it is per second in its own name:</b>
/// <c>ImpExperienceGainPerSecond</c> = <b>20</b>. So an imp needs its record's
/// <c>ExpForNextLevel</c> of 3000 divided by 20 — <b>150 seconds of work</b> — to
/// reach level 2, and about 114 minutes of continuous work to reach level 10.
/// The owner's dungeon had level-2 imps early, which fits.</para>
///
/// <para><b>The 1200 mystery is resolved by that number, and it resolves the
/// other way too.</b> The creature record carries a field this project calls
/// <c>ExpPerSecond</c> reading <b>1200</b> for an Imp, which is absurd per
/// second — level 2 in two and a half seconds. But <b>1200 / 60 = 20</b>, which
/// is the global variable exactly. So the record's field is almost certainly
/// <b>per minute</b>, and two independent fields agreeing on the imp's rate is
/// what promotes that from a coincidence to a reading (STRONGLY INFERRED). On
/// that reading a Goblin's 200 is 3.33 a second, or fifteen minutes a level —
/// plausible for a fighter, and unused here because nothing makes a Goblin
/// fight.</para>
///
/// <para>Registered <b>after</b> <see cref="TileWorkSystem"/> so a creature is
/// paid for a second it actually worked, and after
/// <see cref="CreatureHealthSystem"/> so nothing that died this second is
/// promoted on its way out.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-levels.md")]
public sealed class CreatureExperienceSystem : ISimulationSystem
{
    private const string SystemName = "CreatureExperience";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        foreach (CreatureState state in world.Creatures)
        {
            CreatureDef creature = database.CreatureById(state.CreatureTypeId);

            // Nothing on the floor earns anything (ADR-0051), and nothing
            // walking out of the dungeon does either.
            if (CreatureDeathRules.IsDown(state)) continue;
            if (state.Activity == CreatureActivity.Leaving) continue;

            int gained = CreatureLevelRules.ExperiencePerSecond(database, creature, state);
            if (gained == 0) continue;

            state.ExperiencePoints += gained;

            int before = state.Level;
            if (!CreatureLevelRules.TryLevelUp(database, creature, state)) continue;

            context.Emit(SystemName, "creature_level_up",
                $"creature={state.Id} type={creature.Name} " +
                $"player={state.OwnerPlayerId} {before}->{state.Level} " +
                $"spare_xp={state.ExperiencePoints} " +
                $"next_at={CreatureLevelRules.ExperienceForNextLevel(database, creature, state.Level)} " +
                $"health={state.Health}/{CreatureHealthRules.MaxHealth(database, creature, state)} " +
                $"speed={CreatureLevelRules.SpeedRaw(database, creature, state.Level)}");
        }
    }
}
