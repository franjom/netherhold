// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
