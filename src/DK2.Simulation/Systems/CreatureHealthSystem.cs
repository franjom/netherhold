// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Regeneration, hunger's clock, and death — once a game second.
///
/// <para>Runs on the same cadence and the same phase as <see cref="ManaSystem"/>:
/// the last tick of each game second, so a level does not begin by paying out a
/// second that has not elapsed. Per-second rather than per-tick because every
/// number it applies is written <i>per second</i> in the data, and dividing them
/// by twenty would invent precision the files do not have — and would depend on
/// <see cref="SimulationTiming.TicksPerSecond"/>, which is still a placeholder
/// (Q1).</para>
///
/// <para><b>Healing sources add.</b> A creature on its own land in a lair gets
/// both, because nothing in the data or in OpenKeeper suggests they are
/// exclusive — and for 44 of the 46 creatures the own-land term is zero anyway,
/// so the question only arises for an Imp or King Reginald asleep in a lair.
/// Recorded as the reading it is (Q100).</para>
///
/// <para><b>Death is removal, for now.</b> A creature at zero health leaves the
/// world. DK2 is more careful than that — it drops what it carried, lies there
/// for <c>CreatureDyingStateDurationSeconds</c> (60), may be a body that can be
/// dragged to a graveyard, and only some creatures leave a body at all
/// (<see cref="CreatureFlags.GenerateDeadBody"/>) — and none of that can be
/// built before objects exist (M6). So the flag is read and reported in the
/// event, and the branch it will one day take is named there rather than
/// guessed at. Q99 is the owner's part: whether zero health means dead or
/// merely unconscious.</para>
///
/// <para>Registered <b>after</b> <see cref="PortalSystem"/> so a creature that
/// arrives this tick is not regenerated on the tick it arrives, and so its
/// hunger clock starts from a full second.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-needs.md")]
public sealed class CreatureHealthSystem : ISimulationSystem
{
    private const string SystemName = "CreatureHealth";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        // Descending, so removing a dead creature cannot skip its neighbour.
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = world.Creatures[i];
            CreatureDef creature = database.CreatureById(state.CreatureTypeId);

            Hunger(context, creature, state);

            int regen = CreatureHealthRules.OwnLandRegenPerSecond(world, creature, state)
                        + CreatureHealthRules.LairHealPerSecond(world, database, state);

            if (regen != 0)
            {
                int landed = CreatureHealthRules.Apply(creature, state, regen);
                if (landed != 0)
                {
                    context.Emit(SystemName, "creature_health",
                        $"creature={state.Id} type={creature.Name} " +
                        $"player={state.OwnerPlayerId} delta={landed} " +
                        $"health={state.Health}/{CreatureHealthRules.MaxHealth(creature)}");
                }
            }

            if (state.Health <= 0) Die(context, creature, state);
        }
    }

    private static void Hunger(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (!CreatureHealthRules.CanGetHungry(creature)) return;

        state.HungerTicks += SimulationTiming.TicksPerSecond;

        if (!CreatureHealthRules.HungerHasArrived(creature, state)) return;
        if (!CreatureHealthRules.MakeHungry(creature, state)) return;

        context.Emit(SystemName, "creature_hungry",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} chickens={state.ChickensNeeded} " +
            $"after={state.HungerTicks / SimulationTiming.TicksPerSecond}s");
    }

    private static void Die(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        bool leavesBody = creature.Flags.HasFlag(CreatureFlags.GenerateDeadBody);

        context.Emit(SystemName, "creature_died",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"gold_lost={state.GoldHeld} leaves_body={leavesBody}");

        context.World.RemoveCreature(state.Id);
    }
}
