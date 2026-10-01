// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A captive on a torture device: the category 15 update hook <c>0x0047B590</c>, every
/// fifth turn from being strapped on (ADR-0193).</summary>
/// <remarks>Each act adds variable 19 to <see cref="CreatureState.TorturedDuration"/> and applies
/// the definition's <c>TortureHpChange</c> through the health routine. <c>TortureMoodChange</c>
/// goes to bucket 5 only when the chamber is the creature's own keeper's, so never for a
/// captive. Past the level-scaled <see cref="TortureRules.ConversionThreshold"/> the duration
/// resets, and an enemy dies on a <c>TortureChanceOfDyingWhenConverted</c> roll (shipped 0) or
/// else joins the chamber's keeper.
/// <para>Ours, stated: a captive whose health reaches zero is let go of there and then, and the
/// health system takes it; DK2 knocks it out on the device and then kills it, revealing
/// information (<c>0x004F2370</c>, <see cref="TortureInformationRules"/>, ADR-0230). A convert is let go of too, and stands in the
/// chamber idle as its new keeper's, and beds down like any other creature (owner, Q425).</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public sealed class TortureSystem : ISimulationSystem
{
    private const string SystemName = "Torture";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState creature = world.Creatures[i];
            if (!TortureRules.IsBeingTortured(creature)) continue;
            if (HandRules.IsHeld(creature)) continue;

            int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
            byte keeper = TortureRules.KeeperOf(creature);
            if (TortureRules.FreeDeviceAt(world, database, keeper, here, creature.Id) is null)
            {
                // The device is gone: 131 goes to 136, which gets onto another free one within
                // reach (ADR-0200). Failing that, or with the chamber gone, a captive standing
                // outside its keeper's prison is not one any more.
                int next = TortureRules.DeviceWithinReach(world, database, creature, keeper);
                if (next >= 0)
                {
                    TortureRules.Restrap(context.Hooks, creature, next);
                    context.Emit(SystemName, "restrapped",
                        $"creature={creature.Id} at=({next % world.Width},{next / world.Width})");
                    continue;
                }

                Free(context, creature, "device_gone");
                continue;
            }

            if (CreatureStateMachine.UpdatingCategory(creature) != CreatureStateCategory.Torture) continue;
            if (!CreatureStateMachine.HookFires(creature, CreatureStateMachine.HookCountdownSeed)) continue;

            Act(context, creature, keeper);
        }
    }

    private static void Act(SimulationContext context, CreatureState creature, byte keeper)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);

        creature.TorturedDuration += TortureRules.DurationPerAct;
        int hurt = definition.TortureHpChange < 0
            ? Combat.StatusEffectRules.Hurt(world, database, definition, creature, -definition.TortureHpChange)
            : CreatureHealthRules.Apply(database, definition, creature, definition.TortureHpChange);

        // Only a creature tortured in its own keeper's chamber takes the mood: selector 8, bucket 5.
        if (creature.OwnerPlayerId == keeper)
            CreatureAngerRules.Apply(world, definition, creature, AngerBucket.Treatment, definition.TortureMoodChange);

        if (creature.Health <= 0)
        {
            // An enemy that dies here tells its torturer's keeper something (0x004F2370, ADR-0230).
            string information = "none";
            if (creature.OwnerPlayerId != keeper)
            {
                int reward = TortureInformationRules.Give(world, context.Rng, definition, creature.OwnerPlayerId, keeper);
                information = reward.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Emit(SystemName, "information",
                    $"keeper={keeper} about={creature.OwnerPlayerId} reward={reward}" +
                    (reward == TortureInformationRules.Reveal ? " reveal=unbuilt" : ""));
            }
            context.Emit(SystemName, "tortured_to_death",
                $"creature={creature.Id} type={definition.Name} owner={creature.OwnerPlayerId} " +
                $"keeper={keeper} duration={creature.TorturedDuration} information={information}");
            Free(context, creature, "health_gone");
            return;
        }

        int threshold = TortureRules.ConversionThreshold(database, definition, creature.Level);
        context.Emit(SystemName, "tortured",
            $"creature={creature.Id} type={definition.Name} health={creature.Health} " +
            $"hurt={hurt} duration={creature.TorturedDuration}/{threshold}");
        if (creature.TorturedDuration <= threshold) return;

        creature.TorturedDuration = 0;
        if (creature.OwnerPlayerId == keeper) return;

        if (CreatureAngerRules.RollPercent(context.Rng) < database[VariableTypeId.TortureChanceOfDyingWhenConverted])
        {
            creature.Health = 0;
            context.Emit(SystemName, "died_converting",
                $"creature={creature.Id} type={definition.Name} owner={creature.OwnerPlayerId} keeper={keeper}");
            Free(context, creature, "died_converting");
            return;
        }

        byte from = creature.OwnerPlayerId;
        context.World.ChangeCreatureOwner(context.Database, creature, keeper);
        RescuePriorityRules.Set(creature, keeper, false);
        context.Emit(SystemName, "converted",
            $"creature={creature.Id} type={definition.Name} from={from} to={keeper} " +
            $"at=({creature.Tile.X},{creature.Tile.Y})");
        Free(context, creature, "converted");
    }

    /// <summary>Off the device and out of captivity.</summary>
    private static void Free(SimulationContext context, CreatureState creature, string reason)
    {
        TortureRules.Unstrap(context.Hooks, creature);
        PrisonRules.Release(context.Hooks, creature);
        context.Emit(SystemName, "released",
            $"creature={creature.Id} owner={creature.OwnerPlayerId} reason={reason}");
    }
}
