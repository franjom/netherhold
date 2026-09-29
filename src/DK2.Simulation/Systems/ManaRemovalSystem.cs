// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The removal countdown of the creature update (<c>0x00488930</c>), every tick:
/// a stamped, unpossessed imp whose keeper can afford one imp's upkeep is reprieved; one
/// whose keeper cannot is given zero health five seconds after its stamp, and the death
/// bookkeeping runs at once. Before <see cref="ManaSystem"/>, as DK2 updates its things
/// before its players (<c>0x0050A450</c>), so a stamp is first read on the turn after it is
/// written and a payment cannot reprieve an imp on the turn it expires (ADR-0277).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-mana-removal.md")]
public sealed class ManaRemovalSystem : ISimulationSystem
{
    private const string SystemName = "ManaRemoval";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        uint now = ManaRemovalLifecycle.Turn(context.Tick);
        ushort upkeep = (ushort)ManaRules.ImpUpkeepPerSecond(database);

        // Descending, so a removal cannot skip its neighbour.
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState creature = world.Creatures[i];
            if (creature.ManaRemovalStamp == 0 || !world.HasPlayer(creature.OwnerPlayerId)) continue;

            ManaRemovalAction action = ManaPressureRules.Countdown(
                creature.CreatureTypeId, CreatureDeathRules.IsPossessed(world, creature),
                creature.ManaRemovalStamp, now, world.Player(creature.OwnerPlayerId).Mana, upkeep,
                (uint)SimulationTiming.TicksPerSecond);
            if (action == ManaRemovalAction.None) continue;

            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            if (action == ManaRemovalAction.Cancel)
            {
                context.Emit(SystemName, "mana_removal_cancelled",
                    $"creature={creature.Id} type={definition.Name} player={creature.OwnerPlayerId} " +
                    $"stamped={creature.ManaRemovalStamp} turn={now}");
                creature.ManaRemovalStamp = 0;
                continue;
            }

            // Slot +0x20 writes health 0 and nothing else; the cleanup pass that follows the
            // update takes a creature below 1 health through 0x00487C70 (our Die), never
            // through the collapse window.
            creature.Health = 0;
            context.Emit(SystemName, "mana_removal_expired",
                $"creature={creature.Id} type={definition.Name} player={creature.OwnerPlayerId} " +
                $"stamped={creature.ManaRemovalStamp} turn={now} at=({creature.Tile.X},{creature.Tile.Y})");
            CreatureHealthSystem.Die(context, SystemName, definition, creature);
        }
    }
}
