// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Where a walk to a room ends (<c>0x00471A70</c>, travel-states.md §2.2).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/temple-states.md")]
public static class RoomWalkRules
{
    /// <summary><c>0x004E4C10</c>: <c>random(size)</c> (simulation stream, line <c>0x5E5</c>) steps along
    /// the slab chain from its first slab, then from there round the chain, the first tile that is no
    /// floor slot (<c>0x00454110</c>), no wall slot (<c>0x00454230</c>), of nav class 0 and, outside a
    /// Lair, holding no Obstacle (<c>0x00453600</c>); null with none. One draw a call.</summary>
    public static int? ArrivalTile(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int[] chain = SlabChain.Order(world, room);
        if (chain.Length == 0) return null;
        int start = context.Rng.NextInt(chain.Length);
        var slots = new HashSet<int>();
        AutoFurnitureRules.FurnitureSlots(world, database, room, slots);
        for (int k = 0; k <= chain.Length; k++)
        {
            int tile = chain[(start + k) % chain.Length];
            if (slots.Contains(tile) || CasinoStep.NavClass(database, world.TileAtIndex(tile)) != 0) continue;
            if (room.Definition.TypeId != RoomTypeId.Lair && CasinoStep.HasObstacle(world, database, tile)) continue;
            return tile;
        }

        return null;
    }

    /// <summary><c>0x004E8430</c>: a uniform draw from the simulation stream among the room's tiles in
    /// slab-chain order that are neither a floor slot (<c>0x00454110</c>) nor a wall slot
    /// (<c>0x00454230</c>); null with none, and then no draw.</summary>
    public static int? RoomPoint(WorldState world, GameDatabase database, RoomInstance room, SimRandom rng)
    {
        var slots = new HashSet<int>();
        AutoFurnitureRules.FurnitureSlots(world, database, room, slots);
        var open = new List<int>();
        foreach (int tile in SlabChain.Order(world, room))
            if (!slots.Contains(tile)) open.Add(tile);
        return open.Count == 0 ? null : open[rng.NextInt(open.Count)];
    }
}
