// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Which tiles DK2 draws in the possession cameras (possession-warp.md §8; <c>0x005737E0</c>,
/// <c>0x00572F60</c>, <c>0x00573470</c>): a walk in square rings outward from the camera's tile that
/// keeps a tile when it lies in the view polygon and some part of its angle, seen from the eye, is
/// not yet hidden by solid tiles met earlier. Everything on a tile that is not listed (terrain, roof,
/// creatures, objects, effects) is left out of the picture. It applies only while a possession
/// camera is settled: the timed moves in and out use the ordinary polygon alone. Rebuilt every frame.
/// The order of the walk is part of the rule.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-warp.md")]
public sealed class PossessionVisibility
{
    private const int Turn = 1 << 24, HalfTurn = 1 << 23, AngleMask = Turn - 1, MapSize = 128, MapCentre = 64;

    private readonly bool[] _inPolygon = new bool[MapSize * MapSize];
    private readonly List<(int Start, int End)> _open = [];
    private int _width, _height, _tileX, _tileY;
    private double _eyeX, _eyeY;
    private bool[] _solid = [];
    private List<TilePos> _tiles = [];

    /// <summary>Fills <paramref name="tiles"/> with the tiles to draw, the camera's own first.
    /// <paramref name="eyeX"/> and <paramref name="eyeY"/> are the eye's place in tiles;
    /// <paramref name="polygonTiles"/> are the tiles of the view polygon
    /// (<see cref="ClassicLightVisibility.EnumerateTiles(ReadOnlySpan{TilePos})"/> of the polygon built
    /// with the view volume scaled); <paramref name="solid"/> marks solid terrain, row by row, and a
    /// tile off the map blocks as a solid one does. Doors, pillars, water and lava do not block.
    /// The list can hold tiles off the map, as DK2's does: a user skips them.</summary>
    public void Walk(double eyeX, double eyeY, int width, int height, bool[] solid,
        ReadOnlySpan<TilePos> polygonTiles, List<TilePos> tiles)
    {
        (_eyeX, _eyeY, _width, _height, _solid, _tiles) = (eyeX, eyeY, width, height, solid, tiles);
        (_tileX, _tileY) = ((int)eyeX, (int)eyeY);
        tiles.Clear();
        Array.Clear(_inPolygon, 0, _inPolygon.Length);
        foreach (TilePos tile in polygonTiles)
        {
            int mx = tile.X - _tileX + MapCentre, my = tile.Y - _tileY + MapCentre;
            if ((uint)mx < MapSize && (uint)my < MapSize)
            {
                _inPolygon[(mx * MapSize) + my] = true;
            }
        }
        _open.Clear();
        _open.Add((0, Turn));

        tiles.Add(new TilePos(_tileX, _tileY));
        bool north = true, south = true, west = true, east = true;
        // The polygon ends the walk; the map of it cannot hold a ring past its own half width.
        for (int r = 1; r < MapCentre && (north || south || west || east); r++)
        {
            if (north)
            {
                north = Row(_tileY - r, r);
            }

            if (south)
            {
                south = Row(_tileY + r, r);
            }

            if (west)
            {
                west = Column(_tileX - r, r);
            }

            if (east)
            {
                east = Column(_tileX + r, r);
            }
        }
    }

    // A side from its middle outward; a ring's corners belong to its rows.
    private bool Row(int y, int r)
    {
        bool any = Test(_tileX, y);
        for (int i = 1; i <= r; i++)
        {
            any |= Test(_tileX - i, y);
            any |= Test(_tileX + i, y);
        }
        return any;
    }

    private bool Column(int x, int r)
    {
        bool any = Test(x, _tileY);
        for (int i = 1; i < r; i++)
        {
            any |= Test(x, _tileY - i);
            any |= Test(x, _tileY + i);
        }
        return any;
    }

    // Returns whether the tile is in the polygon, hidden or not: that is what keeps a side alive.
    private bool Test(int x, int y)
    {
        int mx = x - _tileX + MapCentre, my = y - _tileY + MapCentre;
        bool inPolygon = (uint)mx < MapSize && (uint)my < MapSize && _inPolygon[(mx * MapSize) + my];

        double dx = x + 0.5 - _eyeX, dy = y + 0.5 - _eyeY;
        int centre = Angle(dx, dy);
        // Half a quarter turn over the distance: wider than the tile really is.
        int half = (int)((1 << 21) / Math.Sqrt((dx * dx) + (dy * dy)));
        if (!Open(centre - half, centre + half))
        {
            return inPolygon;
        }

        if (inPolygon)
        {
            _tiles.Add(new TilePos(x, y));
        }

        if (Blocks(x, y))
        {
            if (Blocks(x + 1, y))
            {
                Close(Angle(x + 1 - _eyeX, y + 0.5 - _eyeY), centre);
            }

            if (Blocks(x, y + 1))
            {
                Close(Angle(x + 0.5 - _eyeX, y + 1 - _eyeY), centre);
            }

            if (Blocks(x - 1, y))
            {
                Close(Angle(x - _eyeX, y + 0.5 - _eyeY), centre);
            }

            if (Blocks(x, y - 1))
            {
                Close(Angle(x + 0.5 - _eyeX, y - _eyeY), centre);
            }
        }
        return inPolygon;
    }

    private bool Blocks(int x, int y) =>
        (uint)x >= (uint)_width || (uint)y >= (uint)_height || _solid[(y * _width) + x];

    // 2^24 to a turn, zero along +y, growing toward +x; cut toward zero, so a negative angle wraps.
    private static int Angle(double dx, double dy) => (int)(Math.Atan2(dx, dy) * (Turn / (2 * Math.PI))) & AngleMask;

    private static (int Low, int High, bool Wraps) Arc(int a, int b)
    {
        if (a != Turn)
        {
            a &= AngleMask;
        }

        if (b != Turn)
        {
            b &= AngleMask;
        }

        if (a > b)
        {
            (a, b) = (b, a);
        }

        return (a, b, b - a > HalfTurn);
    }

    private bool Open(int a, int b)
    {
        (int low, int high, bool wraps) = Arc(a, b);
        return wraps ? Overlaps(0, low) || Overlaps(high, Turn) : Overlaps(low, high);
    }

    // Touching does not count.
    private bool Overlaps(int low, int high)
    {
        foreach ((int start, int end) in _open)
        {
            if (low < end && high > start)
            {
                return true;
            }
        }

        return false;
    }

    private void Close(int a, int b)
    {
        (int low, int high, bool wraps) = Arc(a, b);
        if (wraps)
        {
            Remove(0, low);
            Remove(high, Turn);
        }
        else
        {
            Remove(low, high);
        }
    }

    // The removed interval is closed: a node it covers goes, one it clips is trimmed, one it cuts is split.
    private void Remove(int low, int high)
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            (int start, int end) = _open[i];
            if (low > end || high < start)
            {
                continue;
            }

            if (low <= start && high >= end)
            {
                _open.RemoveAt(i);
            }
            else if (low <= start)
            {
                _open[i] = (high, end);
            }
            else if (high >= end)
            {
                _open[i] = (start, low);
            }
            else
            {
                _open[i] = (start, low);
                _open.Insert(i + 1, (high, end));
            }
        }
    }
}
