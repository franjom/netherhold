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
/// <para><b>Zero health is not death</b> — CONFIRMED by the owner (Q99), and
/// this system used to have it wrong. A creature that runs out of health
/// <i>collapses</i> and lies there for <c>CreatureDyingStateDurationSeconds</c>
/// (60), rescuable by an imp that carries it back to its bed, and only then
/// dies. See <see cref="CreatureDeathRules"/> for the whole process and for
/// which half of it is still missing.</para>
///
/// <para><b>A dying creature is skipped entirely</b>, and that guard is not
/// cosmetic. An Imp regenerates 67 health a second on its own land, so a dying
/// imp that still regenerated would heal itself off the floor and become
/// immortal on friendly ground — the one creature in the game that could never
/// be killed at home. Recovery is the rescue, not self-healing.</para>
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

            // A dying creature does nothing but die. No hunger clock, and above
            // all no regeneration — see the type summary for the immortal imp.
            if (CreatureDeathRules.IsDying(state))
            {
                Dying(context, creature, state);
                continue;
            }

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

            // Last, and deliberately after the healing above: a creature
            // rescued into its bed at zero health is healed to 100 on this same
            // second and so is never seen collapsed. CreatureDeathRules.Rescue
            // explains why reversing these two would undo every rescue.
            if (CreatureDeathRules.HasCollapsed(state)) Collapse(context, creature, state);
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

    /// <summary>Counts the rescue window down, and kills what it runs out on.</summary>
    private static void Dying(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        state.DyingTicks += SimulationTiming.TicksPerSecond;

        if (!CreatureDeathRules.DyingWindowElapsed(context.Database, state)) return;

        Die(context, creature, state);
    }

    /// <summary>
    /// Drops a creature to the floor. It is still alive, still its keeper's, and
    /// still holds its bed and its gold.
    /// </summary>
    private static void Collapse(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (!CreatureDeathRules.Collapse(state)) return;

        context.Emit(SystemName, "creature_collapsed",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"rescue_window={CreatureDeathRules.DyingWindowTicks(context.Database)
                / SimulationTiming.TicksPerSecond}s " +
            $"bed={state.LairTileIndex}");
    }

    private static void Die(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        // A dead creature gives up its bed, or a lair leaks a tile forever.
        // Not on collapse — the rescue path carries it back to that bed.
        LairRules.ReleaseBed(state);

        // The body is owed and cannot be delivered: a corpse is an object, and
        // OBJECTS is unparsed (M6). Reported so the gap is in the log rather
        // than only in the docs, and so a graveyard has something to grep for.
        bool leavesBody = CreatureDeathRules.LeavesBody(creature);

        context.Emit(SystemName, "creature_died",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"gold_lost={state.GoldHeld} dying_for={state.DyingTicks} " +
            $"leaves_body={leavesBody}");

        context.World.RemoveCreature(state.Id);
    }
}
