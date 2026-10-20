// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The wet/blood counters and requests in the creature update, before action and movement.
/// See docs/research/creature-footprints.md for the native gates and supported flight subset.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-footprints.md")]
public sealed class FootprintSystem : ISimulationSystem
{
    private readonly HashSet<TilePos> _corpseTiles = [];
    public string Name => "footprints";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        _corpseTiles.Clear();
        foreach (CreatureState corpse in world.Creatures)
            if (CreatureDeathRules.IsCorpse(corpse) && !HandRules.IsHeld(corpse)
                && corpse.CarriedByCreatureId < 0 && world.Contains(corpse.Tile))
                _corpseTiles.Add(corpse.Tile);

        foreach (CreatureState creature in world.Creatures)
        {
            if (HandRules.IsHeld(creature) || CreatureDeathRules.IsCorpse(creature)
                || !world.Contains(creature.Tile)) continue;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            bool grounded = creature.PosZ <= 0 && !WaterSpeedRules.IsFlying(definition, creature);
            if (!grounded)
            {
                creature.BloodFootprintTicks = creature.WaterFootprintTicks = 0;
                continue;
            }
            // The native terrain water flag is tested even for a room over water; use the
            // current terrain definition rather than a room's underlying bridge liquid.
            if (context.Database.TerrainById(world.Tile(creature.Tile).TerrainId).IsWater)
            {
                creature.BloodFootprintTicks = 0;
                creature.WaterFootprintTicks = TrailTicks;
                continue;
            }
            if (_corpseTiles.Contains(creature.Tile))
            {
                creature.BloodFootprintTicks = TrailTicks;
                creature.WaterFootprintTicks = 0;
            }
            ushort effect;
            if (creature.BloodFootprintTicks > 0)
            {
                effect = 99;
                creature.BloodFootprintTicks--;
            }
            else if (creature.WaterFootprintTicks > 0)
            {
                effect = 100;
                creature.WaterFootprintTicks--;
            }
            else continue;
            context.RequestEffect(Name, new ViewEffectRequest(new EffectId(effect),
                new PlayerId(creature.OwnerPlayerId),
                new FpPosition3(new(creature.PosX), new(creature.PosY), new(creature.PosZ)),
                new Dk2Angle((ushort)creature.Heading)));
        }
    }

    private const byte TrailTicks = 10 * SimulationTiming.OriginalTicksPerGameSecond;
}
