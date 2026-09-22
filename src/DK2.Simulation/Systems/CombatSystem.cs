// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Systems;

/// <summary>DK2's <c>ProcessFight</c> (<c>0x0045E9F0</c>), run for every creature but the possessed
/// one inside its per-tick update, before its state handler and its steering step
/// (docs/research/combat-acquisition.md §3): a neutral creature never looks; one that may look
/// (<see cref="CombatRules.MayLookForAFight"/>) and is due (<see cref="CombatRules.ScanIsDue"/>)
/// runs <c>SelectAVictim</c>. Ours, until <c>spFighting</c> is read: a fighting creature holds its
/// target while it exists and can be attacked, and lets go otherwise.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-acquisition.md")]
public sealed class CombatSystem : ISimulationSystem
{
    private const string SystemName = "combat";

    private readonly List<Fixed16Vector> _scratch = [];
    private WorldSight? _sight;
    private WorldState? _sightWorld;

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        WorldSteering navigation = context.Navigation;
        navigation.Refresh();
        if (!ReferenceEquals(_sightWorld, world))
        {
            _sight = new WorldSight(navigation, world, database);
            _sightWorld = world;
        }

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (!CreatureDeathRules.IsPossessed(world, creature))
            {
                if (CombatRules.IsFighting(creature)) HoldOrLetGo(context, creature);
                else ProcessFight(context, creature, database.CreatureById(creature.CreatureTypeId));
            }

            TilePos tile = creature.Tile;
            creature.LastFightScanTileIndex = (tile.Y * world.Width) + tile.X;
        }
    }

    private void ProcessFight(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.OwnerPlayerId == PlayerIds.Neutral) return;
        if (!CombatRules.MayLookForAFight(context.World, creature, force: false)) return;

        TilePos tile = creature.Tile;
        bool moved = creature.LastFightScanTileIndex != (tile.Y * context.World.Width) + tile.X;
        if (!CombatRules.ScanIsDue(creature, moved)) return;

        CombatTargetRules.SelectAVictim(context, _sight!, creature, definition, _scratch);
    }

    /// <summary>Ours: the target must still be there and attackable, and I must still be able to
    /// fight — not held, not down, not a captive. DK2's <c>spFighting</c> re-acquires instead
    /// (combat-entry.md §5); until it is read, letting go is the whole of it.</summary>
    private static void HoldOrLetGo(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        CreatureState? target = world.CreatureById(creature.FightTargetId);
        bool keep = target != null
            && CombatRules.IsAttackable(target)
            && CombatRules.MayLookForAFight(world, creature, force: false);
        if (keep) return;

        CombatRules.Disengage(world, creature);
        context.Emit(SystemName, "disengaged",
            $"creature={creature.Id} player={creature.OwnerPlayerId} reason={(target is null ? "target_gone" : "cannot_continue")}");
    }
}
