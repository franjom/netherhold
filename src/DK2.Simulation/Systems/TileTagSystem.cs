// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Applies this tick's <see cref="TagTilesCommand"/>s. A tag records intent and
/// changes nothing about the tile but its marker; the work that follows is
/// <see cref="TileWorkSystem"/>'s, and the decision to go and do it is
/// <see cref="ImpTaskSystem"/>'s.
/// <para>A tag on terrain that is not <see cref="TerrainFlags.Taggable"/> is
/// dropped rather than stored — CONFIRMED at the level of the data: the flag is
/// set on rock, gold, gems, reinforced walls and fog, never on floor. An
/// undiscovered tile is exempt, so a mark into the dark is a bet revalidated at
/// reveal (CONFIRMED, Q193, docs/research/fog-of-war.md); untagging is always
/// allowed, since terrain can change under a marker.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
[Dk2Behavior(Confidence.Confirmed, "docs/research/fog-of-war.md")]
public sealed class TileTagSystem : ISimulationSystem
{
    private const string SystemName = "TileTag";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (TagTilesCommand tag in context.Commands<TagTilesCommand>())
        {
            Apply(context, tag);
        }
    }

    private static void Apply(SimulationContext context, TagTilesCommand command)
    {
        WorldState world = context.World;
        int applied = 0;
        int rejected = 0;

        foreach (TilePos pos in command.Tiles)
        {
            if (!world.Contains(pos))
            {
                rejected++;
                continue;
            }

            ref TileState tile = ref world.Tile(pos);
            TerrainDef terrain = context.Database.TerrainById(tile.TerrainId);

            // Untagging is always allowed: terrain can change under a tag, and a
            // player must be able to clear a marker that is no longer diggable.
            bool hiddenByFog = world.FogOfWarEnabled && !tile.IsDiscoveredBy(command.PlayerId);
            if (command.Tagged && !hiddenByFog && !terrain.IsTaggable)
            {
                rejected++;
                continue;
            }

            if (tile.IsTaggedBy(command.PlayerId) == command.Tagged)
            {
                continue;
            }

            tile.SetTaggedBy(command.PlayerId, command.Tagged);
            if (command.Tagged)
            {
                tile.TagOrder = world.NextTagOrder++;
            }
            else if (!tile.IsTaggedByAnyone)
            {
                tile.TagOrder = 0;
            }

            applied++;
        }

        string span = command.Tiles.Count == 0 ? "none" :
            $"({command.Tiles.Min(t => t.X)},{command.Tiles.Min(t => t.Y)})-" +
            $"({command.Tiles.Max(t => t.X)},{command.Tiles.Max(t => t.Y)})";
        context.Emit(SystemName, command.Tagged ? "tagged" : "untagged",
            $"player={command.PlayerId} applied={applied} rejected={rejected} span={span}");
    }
}
