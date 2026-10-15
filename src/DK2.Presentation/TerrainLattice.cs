// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// DK2's terrain lattice (terrain-lattice.md, Q72): the runtime bend of walls and floors. Each tile
/// owns 20 points, 2 × 2 half-tile columns × 5 levels, moved off their rest positions by a lean
/// into closed ground and a wibble seeded from the position; a piece's vertices are then blended
/// from the points around them (<c>0x0058F030</c>). A pure function of the map, so cells are built
/// on demand and kept until <see cref="Invalidate"/>.
/// </summary>
/// <remarks>
/// Points are in DK2's frame: X east, Y south, Z down, one unit a tile. Cells cover the tiles one
/// beyond each edge of the map, which DK2's window reaches. The tile flag <c>0x8000</c> that zeroes
/// a tile's wibble is unread and not modelled.
/// </remarks>
public sealed class TerrainLattice
{
    private const int Points = 20;
    private const float K17 = 1f / 131072f;

    private readonly GameDatabase _database;
    private readonly WorldState _world;
    private readonly PlayerId _viewer;
    private readonly int _width, _height;
    private readonly Vector3[] _points;
    private readonly bool[] _built;
    private readonly int _defaultWibbleH, _defaultWibbleV;

    public TerrainLattice(GameDatabase database, WorldState world, PlayerId viewer)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewer = viewer;
        _width = world.Width + 2;
        _height = world.Height + 2;
        _points = new Vector3[_width * _height * Points];
        _built = new bool[_width * _height];
        _defaultWibbleH = database.Variables.TryGet(VariableTypeId.DefaultHorizontalTerrainWibble, out int h) ? h : 0;
        _defaultWibbleV = database.Variables.TryGet(VariableTypeId.DefaultVerticalTerrainWibble, out int v) ? v : 0;
    }

    /// <summary>
    /// DK2's "HighWalls" display option, on unless the config turns it off (<c>0x00564B00</c>): the
    /// levels rest at their full spacing. Off, outside possession, they rest at half (step ¼, offset
    /// ¾) and a wall stands about half a tile above the floor (<c>0x005A3220</c>). Changing it
    /// forgets every cell.
    /// </summary>
    public bool HighWalls
    {
        get => _highWalls;
        set
        {
            if (_highWalls == value) return;
            _highWalls = value;
            InvalidateAll();
        }
    }

    private bool _highWalls = true;

    /// <summary>Forgets the cells a change at (x, y) reaches: its own and its eight neighbours'.</summary>
    public void Invalidate(int x, int y)
    {
        for (int ty = y - 1; ty <= y + 1; ty++)
            for (int tx = x - 1; tx <= x + 1; tx++)
                if (tx >= -1 && ty >= -1 && tx <= _width - 2 && ty <= _height - 2) _built[Cell(tx, ty)] = false;
    }

    /// <summary>Forgets every cell, as a change of <see cref="FullHeight"/> requires.</summary>
    public void InvalidateAll() => Array.Clear(_built, 0, _built.Length);

    /// <summary>A point of a piece drawn for tile (x0, y0), bent by the 3×3 tiles around it; a point
    /// outside that window's 5 × 5 × 4 cells is returned as it is.</summary>
    public Vector3 Deform(Vector3 p, int x0, int y0)
    {
        float u = 2f * (p.X - (x0 - 1)), v = 2f * (p.Y - (y0 - 1)), w = 2f * (p.Z + 2.5f);
        int iu = (int)MathF.Floor(u), iv = (int)MathF.Floor(v), iw = (int)MathF.Floor(w);
        if (iu < 0 || iu >= 5 || iv < 0 || iv >= 5 || iw < 0 || iw >= 4) return p;
        float fu = u - iu, fv = v - iv, fw = w - iw;

        Vector3 result = Vector3.Zero;
        for (int du = 0; du < 2; du++)
            for (int dv = 0; dv < 2; dv++)
            {
                int column = iu + du, row = iv + dv;
                int cell = Cell(x0 - 1 + (column >> 1), y0 - 1 + (row >> 1));
                int at = (cell * Points) + ((column & 1) * 10) + ((row & 1) * 5) + iw;
                float weight = (du == 0 ? 1f - fu : fu) * (dv == 0 ? 1f - fv : fv);
                result += weight * (((1f - fw) * _points[at]) + (fw * _points[at + 1]));
            }
        return result;
    }

    private int Cell(int x, int y)
    {
        int index = ((y + 1) * _width) + x + 1;
        if (!_built[index]) Build(x, y, index);
        return index;
    }

    // Tile (x, y)'s 20 points (0x005A3590): rest, then lean and wibble on levels 1 to 3.
    private void Build(int x, int y, int index)
    {
        Span<Vector3> cell = _points.AsSpan(index * Points, Points);
        (float s, float o) = _highWalls ? (0.5f, 0f) : (0.25f, 0.75f);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 5; k++)
                    cell[(i * 10) + (j * 5) + k] = new Vector3(x + (i * 0.5f), y + (j * 0.5f), -2.5f + (k * s) + o);

        // The 3×3 closed tests, n[dy + 1, dx + 1].
        Span<bool> n = stackalloc bool[9];
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                n[((dy + 1) * 3) + dx + 1] = Closed(x + dx, y + dy);

        Span<Vector3> q = stackalloc Vector3[9];
        for (int layer = 0; layer < 3; layer++)
        {
            Lean(x, y, layer, n, q);
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                {
                    Vector3 offset = q[(r * 3) + c];
                    if (layer == 0) offset.Z += -0.15f - (0.125f * MathF.Sin(((2 * x) + c + (2 * y) + r) * 0.5f));
                    offset += Wibble(x, y, layer, r, c, n);
                    cell[(c * 10) + (r * 5) + layer + 1] += offset;
                }
        }
        _built[index] = true;
    }

    // Closed unless in bounds, explored, not solid and without a door (0x0059ECF0).
    private bool Closed(int x, int y)
    {
        if (!_world.Contains(x, y)) return true;
        TileState tile = _world.Tile(x, y);
        if (_world.FogOfWarEnabled && !tile.IsExploredBy(_viewer)) return true;
        return _database.TerrainById(tile.TerrainId).IsSolid || _world.DoorAt(x, y) is not null;
    }

    private (int Hw, int Vw, float Hl, float Vl) TileParams(int x, int y, int layer)
    {
        if (!_world.Contains(x, y)) return (_defaultWibbleH, _defaultWibbleV, 0f, 0f);
        TileState tile = _world.Tile(x, y);
        if (_world.FogOfWarEnabled && !tile.IsExploredBy(_viewer)) return (_defaultWibbleH, _defaultWibbleV, 0f, 0f);
        // A definition not read from a file has no bytes; DK2 would read zeros.
        TerrainWibble t = _database.TerrainById(tile.TerrainId).Wibble ?? default;
        return (t.WibbleH << 7, t.WibbleV << 7, t.LeanH(layer) / 512f, t.LeanV(layer) / 512f);
    }

    // Sub-point (r, c) of tile (x, y) at (x + c/2, y + r/2): each field the least of the four
    // tiles touching it (0x0059E480).
    private (int Hw, int Vw, float Hl, float Vl) SubPoint(int x, int y, int layer, int r, int c)
    {
        int x1 = ((4 * x) + (2 * c) - 1) >> 2, x2 = ((4 * x) + (2 * c) + 1) >> 2;
        int y1 = ((4 * y) + (2 * r) - 1) >> 2, y2 = ((4 * y) + (2 * r) + 1) >> 2;
        var a = TileParams(x1, y1, layer);
        var b = TileParams(x2, y1, layer);
        var d = TileParams(x1, y2, layer);
        var e = TileParams(x2, y2, layer);
        return (Math.Min(Math.Min(a.Hw, b.Hw), Math.Min(d.Hw, e.Hw)),
            Math.Min(Math.Min(a.Vw, b.Vw), Math.Min(d.Vw, e.Vw)),
            MathF.Min(MathF.Min(a.Hl, b.Hl), MathF.Min(d.Hl, e.Hl)),
            MathF.Min(MathF.Min(a.Vl, b.Vl), MathF.Min(d.Vl, e.Vl)));
    }

    // The lean (0x005A37F0): boundary points move into the closed side, then the smoothing.
    private void Lean(int x, int y, int layer, ReadOnlySpan<bool> n, Span<Vector3> q)
    {
        q.Clear();
        bool nw = n[0], north = n[1], ne = n[2], west = n[3], centre = n[4], east = n[5], sw = n[6], south = n[7], se = n[8];
        Corner(x, y, layer, 0, 0, nw, north, west, centre, q);
        Corner(x, y, layer, 0, 2, north, ne, centre, east, q);
        Corner(x, y, layer, 2, 0, west, centre, sw, south, q);
        Corner(x, y, layer, 2, 2, centre, east, south, se, q);
        Edge(x, y, layer, 0, 1, north, centre, vertical: true, q);
        Edge(x, y, layer, 2, 1, centre, south, vertical: true, q);
        Edge(x, y, layer, 1, 0, west, centre, vertical: false, q);
        Edge(x, y, layer, 1, 2, centre, east, vertical: false, q);

        q[1].X += (q[0].X + q[2].X) / 2f;
        q[3].Y += (q[0].Y + q[6].Y) / 2f;
        q[5].Y += (q[2].Y + q[8].Y) / 2f;
        q[7].X += (q[6].X + q[8].X) / 2f;
        q[4].X = (q[5].X + q[3].X + q[1].X + q[7].X) / 4f;
        q[4].Y = (q[7].Y + q[1].Y + q[5].Y + q[3].Y) / 4f;
    }

    private void Corner(int x, int y, int layer, int r, int c, bool tnw, bool tne, bool tsw, bool tse, Span<Vector3> q)
    {
        var p = SubPoint(x, y, layer, r, c);
        float a = p.Hl, z0 = -p.Vl;
        bool northPair = tnw | tne, southPair = tsw | tse, westPair = tnw | tsw, eastPair = tne | tse;
        ref Vector3 point = ref q[(r * 3) + c];
        bool spent = false;
        if (eastPair != westPair)
        {
            if (northPair != southPair) a *= 1.4f;
            point.X += westPair ? -a : a;
            point.Z += z0;
            spent = true;
        }
        if (northPair != southPair)
        {
            point.Y += northPair ? -a : a;
            if (!spent) point.Z += z0;
        }
    }

    // Between two tiles, first then second along x (vertical: false) or y (vertical: true).
    private void Edge(int x, int y, int layer, int r, int c, bool first, bool second, bool vertical, Span<Vector3> q)
    {
        if (first == second) return;
        var p = SubPoint(x, y, layer, r, c);
        ref Vector3 point = ref q[(r * 3) + c];
        float move = first ? -p.Hl : p.Hl;
        if (vertical) point.Y += move;
        else point.X += move;
        point.Z += -p.Vl;
    }

    // The wibble (0x0059E750): a position-seeded jitter, then pushes into closed ground.
    private Vector3 Wibble(int x, int y, int layer, int r, int c, ReadOnlySpan<bool> n)
    {
        var p = SubPoint(x, y, layer, r, c);
        uint seed = unchecked((uint)((2 * x) + c + (16 * layer) + (0x10000 * ((2 * y) + r))));
        int dz = p.Vw != 0 ? (int)Rand(ref seed, (uint)p.Vw) : 0;
        int dy = p.Hw != 0 ? (int)Rand(ref seed, (uint)p.Hw) - (p.Hw / 2) : 0;
        int dx = p.Hw != 0 ? (int)Rand(ref seed, (uint)p.Hw) - (p.Hw / 2) : 0;
        var d = new Vector3(dx, dy, dz) * K17;

        float push = p.Hw * K17 / 2f;
        bool nw = n[0], north = n[1], west = n[3], centre = n[4];
        if (r == 0 && c == 0)
        {
            bool northPair = nw | north, southPair = west | centre, westPair = nw | west, eastPair = north | centre;
            if (eastPair != westPair)
            {
                if (northPair != southPair) push *= 1.4f;
                d.X += westPair ? -push : push;
            }
            if (northPair != southPair) d.Y += northPair ? -push : push;
        }
        else if (r == 0 && c == 1 && north != centre) d.Y += north ? -push : push;
        else if (r == 1 && c == 0 && west != centre) d.X += west ? -push : push;
        return d;
    }

    // The game's LCG (0x005558D0) on a local seed.
    private static uint Rand(ref uint seed, uint range)
    {
        seed = unchecked((seed * 0x19660D) + 0x3C6EF35F);
        return ((seed >> 13) | (seed << 19)) % range;
    }
}
