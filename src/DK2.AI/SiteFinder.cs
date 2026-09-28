// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.AI;

/// <summary>A rectangle of tiles the site finder chose, by its top-left corner.</summary>
public readonly record struct RoomSite(TilePos Origin, int Width, int Height)
{
    public IEnumerable<TilePos> Tiles()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new TilePos(Origin.X + x, Origin.Y + y);
    }

    public bool Contains(TilePos p) =>
        p.X >= Origin.X && p.X < Origin.X + Width && p.Y >= Origin.Y && p.Y < Origin.Y + Height;
}

/// <summary>The keeper's build area in tiles, inclusive on both ends.</summary>
public readonly record struct BuildArea(int MinX, int MinY, int MaxX, int MaxY)
{
    public static BuildArea WholeMap(int width, int height) => new(0, 0, width - 1, height - 1);

    public bool Contains(TilePos p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
}

/// <summary>
/// Where to dig the next room: the original's site search (docs/KEEPER_AI.md §5.5),
/// STRONGLY INFERRED from the binary. From an origin, walk in a direction past
/// anything already tagged or already a room, skip <c>TilesLeftBetweenRooms</c>
/// more tiles, then lay a side×side square centred on the approach line and
/// extending forward. Every tile of the square must be diggable rock or the
/// keeper's own empty floor; the square is rejected when more than
/// <c>Openness</c>% of its perimeter is already open, or when it leaves the
/// keeper's build area. The task machine retries another direction on its next
/// allowance unit when this one fails.
/// </summary>
public static class SiteFinder
{
    public static RoomSite? Find(
        KeeperView view, TilePos origin, int startDirection, int roomSize,
        int tilesBetweenRooms, int opennessPercent, BuildArea area)
    {
        int side = Math.Max(1, (int)Math.Sqrt(roomSize));

        return TryDirection(view, origin, startDirection & 3, side, roomSize,
            tilesBetweenRooms, opennessPercent, area);
    }

    private static RoomSite? TryDirection(
        KeeperView view, TilePos origin, int direction, int side, int roomSize,
        int tilesBetweenRooms, int opennessPercent, BuildArea area)
    {
        (int dx, int dy, int sx, int sy) = KeeperDirections.Search[direction];
        TilePos pos = origin;

        // Walk past tagged or room tiles, at most 2×side of them.
        int steps = side * 2;
        while (view.InBounds(pos) && steps > 0 && (view.IsTaggedByAnyone(pos) || view.HasRoom(pos)))
        {
            steps--;
            pos = new TilePos(pos.X + dx, pos.Y + dy);
        }
        if (steps == 0 || !view.InBounds(pos) || IsUndiggableRock(view, pos)) return null;

        // Leave the configured gap.
        for (int i = 0; i < tilesBetweenRooms; i++)
        {
            pos = new TilePos(pos.X + dx, pos.Y + dy);
            if (!view.InBounds(pos) || IsUndiggableRock(view, pos)) return null;
        }

        // Centre the square on the approach line: the sideways offset uses the
        // other axis's step, exactly as the original computes it. The square is
        // then laid out from `start` stepping `sx` along x and `sy` along y.
        var start = new TilePos(pos.X + (dy * (side / 2)), pos.Y + (dx * (side / 2)));

        int w = side, h = side;
        if (!Acceptable(view, start, w, h, sx, sy)) return null;

        // side² short of the requested size: one more strip forward if it fits.
        if (side * side < roomSize)
        {
            bool vertical = dy != 0;
            int w2 = vertical ? w : w + 1, h2 = vertical ? h + 1 : h;
            if (Acceptable(view, start, w2, h2, sx, sy))
            {
                w = w2;
                h = h2;
            }
        }

        // The perimeter test only applies when the keeper leaves a gap between
        // rooms (the original gates it on TilesLeftBetweenRooms != 0): a keeper
        // that builds rooms touching is expected to have open ground around them.
        if (tilesBetweenRooms > 0 && !PerimeterClosedEnough(view, start, w, h, sx, sy, opennessPercent)) return null;

        RoomSite site = Normalise(start, w, h, sx, sy);
        foreach (TilePos t in site.Tiles())
            if (!area.Contains(t)) return null;
        return site;
    }

    /// <summary>Solid but not taggable: rock nobody can dig, which ends a search.</summary>
    private static bool IsUndiggableRock(KeeperView view, TilePos p)
    {
        TerrainDef t = view.Terrain(p);
        return t.IsSolid && !t.IsTaggable;
    }

    /// <summary>Diggable rock, or the keeper's own floor with no room on it.</summary>
    private static bool TileAcceptable(KeeperView view, TilePos p)
    {
        if (!view.InBounds(p)) return false;
        TerrainDef t = view.Terrain(p);
        if (t.IsTaggable && !t.IsImpenetrable) return true;
        return view.IsMine(p) && !t.IsRoom && !t.IsSolid;
    }

    private static TilePos Lay(TilePos start, int i, int j, int sx, int sy) =>
        new(start.X + (i * sx), start.Y + (j * sy));

    private static bool Acceptable(KeeperView view, TilePos start, int w, int h, int sx, int sy)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                if (!TileAcceptable(view, Lay(start, i, j, sx, sy))) return false;
        return true;
    }

    private static bool PerimeterClosedEnough(
        KeeperView view, TilePos start, int w, int h, int sx, int sy, int opennessPercent)
    {
        int open = 0, total = 0;
        for (int j = 0; j < h; j++)
        {
            Count(view, Lay(start, -1, j, sx, sy), ref open, ref total);
            Count(view, Lay(start, w, j, sx, sy), ref open, ref total);
        }
        for (int i = -1; i <= w; i++)
        {
            Count(view, Lay(start, i, -1, sx, sy), ref open, ref total);
            Count(view, Lay(start, i, h, sx, sy), ref open, ref total);
        }
        return total == 0 || open * 100 / total <= opennessPercent;
    }

    private static void Count(KeeperView view, TilePos p, ref int open, ref int total)
    {
        total++;
        if (!view.InBounds(p)) return;
        if (view.IsTaggedByAnyone(p) || !view.IsSolid(p)) open++;
    }

    private static RoomSite Normalise(TilePos start, int w, int h, int sx, int sy)
    {
        TilePos far = Lay(start, w - 1, h - 1, sx, sy);
        int minX = Math.Min(start.X, far.X), maxX = Math.Max(start.X, far.X);
        int minY = Math.Min(start.Y, far.Y), maxY = Math.Max(start.Y, far.Y);
        return new RoomSite(new TilePos(minX, minY), maxX - minX + 1, maxY - minY + 1);
    }
}
