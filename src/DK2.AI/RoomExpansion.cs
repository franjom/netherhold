// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

/// <summary>The room planner's expansion of an existing room (<c>0x004FBE00</c>): the rectangle task 2
/// digs and task 3 builds, or null when no side of the room can grow
/// (computer-keeper-management.md section 10).</summary>
/// <remarks>The corners are the chain's tiles with the smallest and the largest x + y, the first of
/// each kept on a tie. The one-tile strips outside them are tested north, south, west, then east;
/// west and east run between the grown north and south limits. Each strip scan stops short of its
/// high end, and a side whose scan is empty is accepted untested. Every accepted side is kept.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public static class RoomExpansion
{
    public static RoomSite? Find(KeeperView view, KeeperRoomView room)
    {
        int minX = 0x3FFFFFFF, minY = 0x3FFFFFFF, maxX = 0, maxY = 0;
        foreach (int index in room.ChainOrder)
        {
            TilePos t = view.FromIndex(index);
            if (t.X + t.Y < minX + minY) (minX, minY) = (t.X, t.Y);
            if (maxX + maxY < t.X + t.Y) (maxX, maxY) = (t.X, t.Y);
        }

        int top = Grow(view, minX, maxX, x => new TilePos(x, minY - 1), x => new TilePos(x, minY - 2)) ? minY - 1 : minY;
        int bottom = Grow(view, minX, maxX, x => new TilePos(x, maxY + 1), x => new TilePos(x, maxY + 2)) ? maxY + 1 : maxY;
        int left = Grow(view, top, bottom, y => new TilePos(minX - 1, y), y => new TilePos(minX - 2, y)) ? minX - 1 : minX;
        int right = Grow(view, top, bottom, y => new TilePos(maxX + 1, y), y => new TilePos(maxX + 2, y)) ? maxX + 1 : maxX;

        if (top == minY && bottom == maxY && left == minX && right == maxX) return null;
        return new RoomSite(new TilePos(left, top), right - left + 1, bottom - top + 1);
    }

    /// <summary>One side: from <paramref name="from"/> while below <paramref name="to"/>, every strip tile
    /// in the map and acceptable, its gap tile two out in the map, and at most one gap tile open or
    /// already tagged by this keeper.</summary>
    private static bool Grow(KeeperView view, int from, int to, Func<int, TilePos> strip, Func<int, TilePos> gapLine)
    {
        bool gapSeen = false;
        for (int i = from; i < to; i++)
        {
            TilePos tile = strip(i), gap = gapLine(i);
            if (!view.InBounds(tile) || !Acceptable(view, tile) || !view.InBounds(gap)) return false;
            if (view.IsTaggedByMe(gap) || !view.Terrain(gap).IsTaggable)
            {
                if (gapSeen) return false;
                gapSeen = true;
            }
        }
        return true;
    }

    /// <summary>A strip tile: diggable rock (taggable, not impenetrable), a neutral type that heals into
    /// another (terrain <c>+0x1CB</c> bit 0), or this keeper's own tile with no room on it.</summary>
    public static bool Acceptable(KeeperView view, TilePos tile)
    {
        TerrainDef terrain = view.Terrain(tile);
        if (terrain.IsTaggable && !terrain.IsImpenetrable) return true;
        if (HealsIntoAnother(terrain)) return true;
        return view.IsMine(tile) && !terrain.IsRoom;
    }

    /// <summary>Terrain byte <c>+0x1CB</c> bit 0, set by the loader (<c>0x004F7759</c>) for a type that
    /// is not Ownable, starts below its maximum health and names another type for full health: the
    /// neutral paths that heal into an owned floor (imp-claim-path.md).</summary>
    public static bool HealsIntoAnother(TerrainDef terrain) =>
        !terrain.IsOwnable && terrain.StartingHealth < terrain.MaxHealth
        && terrain.TerrainId != terrain.MaxHealthTypeTerrainId;
}
