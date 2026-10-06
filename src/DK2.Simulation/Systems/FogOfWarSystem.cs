// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Accumulates the tiles each keeper's creatures have explored.
/// <para>DKII.EXE compares squared fixed-point distances from the creature to
/// tile centres and reveals only when the distance is strictly less than the
/// definition's range squared (STRONGLY INFERRED). The shipped table holds
/// <c>0x6000</c> — six tiles — in that field for all 46 creatures; the range is
/// read from data here rather than frozen as a constant.</para>
/// <para>Implements the campaign's observed <em>Stay Revealed</em> behaviour.
/// <em>Re-shroud</em> and <em>Re-fog</em> are real setup strings, but their
/// selectors and transition rules are UNKNOWN, so this system does not invent
/// them (Q192).</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/fog-of-war.md")]
public sealed class FogOfWarSystem : ISimulationSystem
{
    public string Name => "FogOfWar";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (!world.FogOfWarEnabled) return;

        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.InHand ||
                creature.Activity is CreatureActivity.Dying or CreatureActivity.Dead)
                continue;

            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            Reveal(world, context.Database, creature.OwnerPlayerId,
                creature.PosX, creature.PosY, definition.PerceptionRange.Raw);
        }
    }

    private static void Reveal(
        WorldState world,
        GameDatabase database,
        PlayerId playerId,
        int centreX,
        int centreY,
        int radius)
    {
        if (radius <= 0 || playerId > TileState.MaxTaggingPlayerId) return;

        int minX = Math.Max(0, (centreX - radius) / Fp32.One);
        int minY = Math.Max(0, (centreY - radius) / Fp32.One);
        int maxX = Math.Min(world.Width - 1, (centreX + radius) / Fp32.One);
        int maxY = Math.Min(world.Height - 1, (centreY + radius) / Fp32.One);
        long radiusSquared = (long)radius * radius;

        for (int y = minY; y <= maxY; y++)
        {
            long dy = CreatureState.CentreOf(y) - (long)centreY;
            for (int x = minX; x <= maxX; x++)
            {
                long dx = CreatureState.CentreOf(x) - (long)centreX;
                if ((dx * dx) + (dy * dy) >= radiusSquared) continue;

                ref TileState tile = ref world.Tile(x, y);
                if (tile.IsDiscoveredBy(playerId)) continue;

                tile.SetDiscoveredBy(playerId);

                // A speculative fog mark survives only if the revealed terrain
                // can itself be tagged. This covers both outcomes observed in
                // DK2: room ground and impenetrable rock discard the mark.
                if (!database.TerrainById(tile.TerrainId).IsTaggable)
                    tile.SetTaggedBy(playerId, false);
            }
        }
    }
}
