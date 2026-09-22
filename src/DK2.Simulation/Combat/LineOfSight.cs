// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;

namespace DK2.Simulation.Combat;

/// <summary>What a tile does to a line of sight: the per-tile question DK2's line walker asks
/// (<c>0x004511A0</c>). Separate from the walker so the walk can be tested on a grid of bits.</summary>
public interface ISightBlocker
{
    /// <summary>True when the tile at (<paramref name="x"/>, <paramref name="y"/>) stops sight.</summary>
    bool Blocks(int x, int y);
}

/// <summary>DK2's line-of-sight test between two things (<c>0x004512F0</c>, called by every target
/// scan with its two flags at 0 and 1): a fixed-point walk along the straight line from one point
/// to the other, one tile boundary at a time, asking each tile entered whether it blocks. The
/// tile the walk starts in is never asked; the tile the target stands in is, when entered.
/// (docs/research/combat-acquisition.md §5)</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-acquisition.md")]
public static class LineOfSight
{
    private const int TileMask = unchecked((int)0xFFFF0000);

    /// <summary>Whether <paramref name="grid"/> leaves the line from one position to the other
    /// clear. Positions are 1/4096-tile world units; the walk runs in 16.16 tile units, as the
    /// original shifts both points left by four before it starts.</summary>
    public static bool Clear(ISightBlocker grid, int fromX, int fromY, int toX, int toY)
    {
        int x = fromX << 4, y = fromY << 4;
        int dx = (toX << 4) - x, dy = (toY << 4) - y;

        while (true)
        {
            if ((((x + dx) ^ x) & TileMask) == 0 && (((y + dy) ^ y) & TileMask) == 0) return true;

            // Distance to the next boundary on each axis, and the parameter (2.14) at which the
            // line reaches it. Going backwards the boundary is the one just below the tile.
            int stepToX = 0x10000 - (x & 0xFFFF);
            int stepToY = 0x10000 - (y & 0xFFFF);
            int tx = int.MaxValue, ty = int.MaxValue;
            if (dx != 0)
            {
                if (dx < 0) stepToX = -(x & 0xFFFF) - 1;
                tx = (stepToX << 14) / dx;
            }
            if (dy != 0)
            {
                if (dy < 0) stepToY = -(y & 0xFFFF) - 1;
                ty = (stepToY << 14) / dy;
            }

            bool crossedX = false, crossedY = false;
            if (ty > tx)
            {
                int stepY = unchecked(tx * dy) >> 14;
                crossedX = true;
                y += stepY;
                dy -= stepY;
                x += stepToX;
                dx -= stepToX;
                if (stepY != 0 && stepY == stepToY) crossedY = true;
            }
            else if (ty < tx)
            {
                int stepX = unchecked(ty * dx) >> 14;
                crossedY = true;
                x += stepX;
                dx -= stepX;
                y += stepToY;
                dy -= stepToY;
                if (stepX != 0 && stepX == stepToX) crossedX = true;
            }
            else
            {
                x += stepToX;
                y += stepToY;
                dx -= stepToX;
                dy -= stepToY;
                crossedX = crossedY = true;
            }

            bool blocked;
            if (crossedX && crossedY)
            {
                // A corner: the two tiles the line brushes past decide first, and only when both
                // are clear is the tile entered diagonally asked itself.
                int sideX = dx > 0 ? x - 1 : x + 1;
                int sideY = dy > 0 ? y - 1 : y + 1;
                blocked = grid.Blocks(x >> 16, sideY >> 16)
                    || grid.Blocks(sideX >> 16, y >> 16)
                    || grid.Blocks(x >> 16, y >> 16);
            }
            else
            {
                blocked = grid.Blocks(x >> 16, y >> 16);
            }

            if (blocked) return false;
        }
    }
}

/// <summary>The per-tile rule as the running game has it: a solid navigation byte stops sight,
/// and so does a door standing on the tile, unless the door's definition is a barricade, which
/// the combat scans (flag 1) look over. Nothing else on a tile is consulted.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-acquisition.md")]
public sealed class WorldSight : ISightBlocker
{
    private readonly WorldSteering _navigation;
    private readonly WorldState _world;
    private readonly GameDatabase _database;

    public WorldSight(WorldSteering navigation, WorldState world, GameDatabase database)
    {
        _navigation = navigation;
        _world = world;
        _database = database;
    }

    public bool Blocks(int x, int y)
    {
        byte nav = _navigation.NavByte(x, y);
        if (NavBytes.IsSolid(nav)) return true;
        if (!NavBytes.IsDoor(nav)) return false;

        IReadOnlyList<DoorState> doors = _world.Doors;
        for (int i = 0; i < doors.Count; i++)
        {
            DoorState door = doors[i];
            if (door.X != x || door.Y != y) continue;
            return !_database.Doors.TryGetValue(door.DoorId, out DoorDef? definition)
                || (definition.Flags & DoorFlags.Barricade) == 0;
        }
        return false;
    }
}
