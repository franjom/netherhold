// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>
/// Accumulates the occluded explored grid and the unoccluded scanned circle,
/// with native guards and per-term fixed-point rounding.
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
        context.Navigation.Refresh();
        var blockers = new WorldSight(context.Navigation, world, context.Database);
        Span<bool> visible = stackalloc bool[225];

        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.InHand ||
                creature.PrisonOwnerPlayerId != PrisonRules.NoPrison ||
                creature.Activity is CreatureActivity.Dying or CreatureActivity.Dead)
                continue;

            TilePos sourceTile = creature.Tile;
            TortureSightPass(world, context.Database, blockers, creature, visible);
            if (creature.OwnerPlayerId > TileState.MaxTaggingPlayerId) continue;
            if (world.DoorAt(sourceTile.X, sourceTile.Y) is { Open: false }) continue;

            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            int sightRadius = Math.Max(2, Math.Min(7, definition.DistanceCanSee.Raw >> 12));
            DiscoverySight.Fill(blockers, sourceTile, world.Width, world.Height, visible);
            for (int dy = -sightRadius; dy <= sightRadius; dy++)
                for (int dx = -sightRadius; dx <= sightRadius; dx++)
                    if (visible[(dy + 7) * 15 + dx + 7])
                        DiscoveryRules.SetExplored(world, context.Database,
                            new TilePos(sourceTile.X + dx, sourceTile.Y + dy), creature.OwnerPlayerId);
            Scan(world, context.Database, creature.OwnerPlayerId,
                new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ)),
                definition.PerceptionRange);
        }
    }

    /// <summary><c>0x0048CC90</c> for a creature its tagged keeper does not own (torture reward 11):
    /// while passes remain, each one spends a pass, then a closed door on its tile stops it, else the
    /// 3 x 3 round it that the sight grid sees is explored for that keeper. The caller has already
    /// passed over a prisoner, a dying creature and one in the hand.
    /// <para>Ours, stated: a pass is spent when the creature has moved onto another tile, the call
    /// at <c>0x0048A817</c>; DK2's other callers (idle state 1, digging, a hand drop, a breach) also
    /// spend one, and are not modelled.</para></summary>
    private static void TortureSightPass(
        WorldState world, GameDatabase database, WorldSight blockers, CreatureState creature, Span<bool> visible)
    {
        if (creature.TortureSightPasses == 0 || creature.TortureSightPlayerId == creature.OwnerPlayerId) return;
        int here = world.TileIndex(creature.Tile);
        if (here == creature.TortureSightTileIndex) return;
        creature.TortureSightTileIndex = here;
        creature.TortureSightPasses--;
        TilePos source = creature.Tile;
        if (world.DoorAt(source.X, source.Y) is { Open: false }) return;
        DiscoverySight.Fill(blockers, source, world.Width, world.Height, visible);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (visible[(dy + 7) * 15 + dx + 7])
                    DiscoveryRules.SetExplored(world, database,
                        new TilePos(source.X + dx, source.Y + dy), creature.TortureSightPlayerId);
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
