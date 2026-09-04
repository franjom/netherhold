// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Applies this tick's <see cref="TagTilesCommand"/>s.
///
/// Tagging is the only thing here: it records intent and changes nothing about
/// the tile except its marker. The work that follows a tag is
/// <see cref="TileWorkSystem"/>'s, and the decision to go and do it belongs to
/// a creature that does not exist yet.
///
/// A tag on terrain whose type is not <see cref="TerrainFlags.Taggable"/> is
/// dropped rather than stored. CONFIRMED at the level of the data: the flag
/// exists precisely to say what can be marked, and it is set on rock, gold,
/// gems, reinforced walls and fog — never on floor.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
public sealed class TileTagSystem : ISimulationSystem
{
    private const string SystemName = "TileTag";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is not TagTilesCommand tag) continue;
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
            if (command.Tagged && !terrain.IsTaggable)
            {
                rejected++;
                continue;
            }

            if (tile.IsTaggedBy(command.PlayerId) == command.Tagged) continue;

            tile.SetTaggedBy(command.PlayerId, command.Tagged);
            applied++;
        }

        context.Emit(SystemName, command.Tagged ? "tagged" : "untagged",
            $"player={command.PlayerId} applied={applied} rejected={rejected}");
    }
}
