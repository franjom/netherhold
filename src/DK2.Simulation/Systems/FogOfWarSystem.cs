// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Accumulates the unoccluded scanned circle, with native guards and per-term
/// fixed-point rounding. The separate explored sight pass remains unbuilt.
/// This system's per-tick cadence differs from DK2's event-driven requests;
/// see fog-reveal-sources.md section3.1.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/fog-reveal-sources.md")]
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
                creature.PrisonOwnerPlayerId != PrisonRules.NoPrison ||
                creature.Activity is CreatureActivity.Dying or CreatureActivity.Dead)
                continue;

            TilePos sourceTile = creature.Tile;
            if (world.DoorAt(sourceTile.X, sourceTile.Y) is { Open: false }) continue;

            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            Scan(world, context.Database, creature.OwnerPlayerId,
                new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ)),
                definition.PerceptionRange);
        }
    }

    private static void Scan(
        WorldState world,
        GameDatabase database,
        PlayerId playerId,
        FpPosition3 position,
        Fp32 range)
    {
        int centreX = position.X.Raw, centreY = position.Y.Raw, radius = range.Raw;
        if (radius <= 0 || playerId > TileState.MaxTaggingPlayerId) return;

        int minX = Math.Max(0, (centreX - radius) / Fp32.One);
        int minY = Math.Max(0, (centreY - radius) / Fp32.One);
        int maxX = Math.Min(world.Width - 1, (centreX + radius) / Fp32.One);
        int maxY = Math.Min(world.Height - 1, (centreY + radius) / Fp32.One);
        long radiusSquared = ((long)radius * radius) >> 12;

        for (int y = minY; y <= maxY; y++)
        {
            long dy = CreatureState.CentreOf(y) - (long)centreY;
            for (int x = minX; x <= maxX; x++)
            {
                long dx = CreatureState.CentreOf(x) - (long)centreX;
                if (((dx * dx) >> 12) + ((dy * dy) >> 12) >= radiusSquared)
                    continue;

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
