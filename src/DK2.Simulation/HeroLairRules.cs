// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The level start's sweep over Hero Lair ground (<c>Level_InitTriggersAndStart</c>, <c>0x0050C4B5</c>
/// to <c>0x0050C643</c>, ADR-0673): after the torches, an interior tile of terrain 35 with no object 0x71 beside it
/// draws <c>random(4)</c> (World.cpp line 0x969) and, on a zero, gets one, then draws its heading,
/// <c>random(0x800)</c> (line 0x971). The draw sites and order are STRONGLY INFERRED; the interior rule (tile byte
/// <c>+0x0C</c> zero) is a HYPOTHESIS, so the class carries the lower label.</summary>
[Dk2Behavior(Confidence.Hypothesis, "docs/research/hero-lair-trestles.md")]
public static class HeroLairRules
{
    /// <summary>Terrain 35, <c>Hero Lair</c> (the tile byte <c>+0x0A</c> the sweep tests against 0x23).</summary>
    public const byte HeroLairTerrainId = 35;

    /// <summary>Object 0x71, <c>Hero Armoury Trestle</c> in the shipped OBJECTS.</summary>
    public const byte TrestleObjectId = 0x71;

    /// <summary>The sweep: row by row, x inside y, over every tile. DK2 also runs it for a restored save, with world
    /// <c>+0x7239</c> bit 0x40 set; ours does not (ADR-0673). The object belongs to the hero player
    /// (<c>[0x006F254C]</c>) and DK2 places it at the tile centre; ours keeps the tile and the heading. A missing
    /// object definition makes <c>Object_Create</c> fail after the first draw, so only the heading draw is lost.</summary>
    internal static void InitializeLevelStart(WorldState world, GameDatabase database, SimRandom rng)
    {
        if (!database.Terrain.TryGetValue(HeroLairTerrainId, out TerrainDef? terrain) || !HasPlainMask(terrain)) return;

        HashSet<int>? trestles = null;
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                int index = (y * world.Width) + x;
                if (world.TileAtIndex(index).TerrainId != HeroLairTerrainId || !IsInterior(world, x, y)) continue;
                trestles ??= TrestleTiles(world);
                if (TrestleBeside(world, trestles, index)) continue;
                if (rng.NextInt(4) != 0 || database.ObjectById(TrestleObjectId) is null) continue;
                ObjectState trestle = world.SpawnObject(database, TrestleObjectId, new PlayerId(PlayerIds.Good), index);
                trestles.Add(index);
                trestle.Heading = rng.NextInt(0x800) & 0x7FF;
            }
        }
    }

    /// <summary>Tile byte <c>+0x0C</c> is zero (<c>Map_TileNeighbourCodes</c>, <c>0x00450470</c>, run after every
    /// terrain write): for a terrain that is not Solid and not ConstructionTypeWater, and a tile with no room and no
    /// door, bit k is set for a neighbour off the map or of another terrain, so zero means all eight neighbours are
    /// on the map and the same terrain. Terrain 35 carries neither flag.</summary>
    private static bool IsInterior(WorldState world, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && world.Tile(x + dx, y + dy).TerrainId != HeroLairTerrainId) return false;
        return true;
    }

    private static bool HasPlainMask(TerrainDef terrain) =>
        (terrain.Flags & (TerrainFlags.Solid | TerrainFlags.ConstructionTypeWater)) == 0;

    private static HashSet<int> TrestleTiles(WorldState world)
    {
        var tiles = new HashSet<int>();
        foreach (ObjectState thing in world.Objects)
            if (thing.ObjectTypeId == TrestleObjectId) tiles.Add(thing.TileIndex);
        return tiles;
    }

    /// <summary>Any of the eight neighbours, from the table at <c>0x006BC370</c>, holds an object 0x71
    /// (<c>Cell_FindObjectOfType</c>, <c>0x004537E0</c>); the tile's own cell is not asked.</summary>
    private static bool TrestleBeside(WorldState world, HashSet<int> trestles, int index)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && trestles.Contains(index + (dy * world.Width) + dx)) return true;
        return false;
    }
}
