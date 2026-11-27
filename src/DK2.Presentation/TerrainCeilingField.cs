// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A level's roof as an adapter draws it (<see cref="TerrainCeiling"/>): the closed tiles
/// and height nibbles kept between frames, what changed since the last read, and each piece's
/// vertices ready to draw. Points are DK2's frame: X east, Y south, Z down, one unit a tile.</summary>
public sealed class TerrainCeilingField
{
    private readonly int _width, _height;
    private readonly bool[] _closed, _read;
    private readonly byte[] _nibbles;
    private bool _filled;

    public TerrainCeilingField(int width, int height)
    {
        _width = width;
        _height = height;
        _closed = new bool[width * height];
        _read = new bool[width * height];
        _nibbles = new byte[width * height];
    }

    /// <summary>Reads the world's closed tiles (<see cref="TerrainCeiling.MarkClosed"/>) and adds
    /// the index of each tile that opened or closed since the last read to <paramref name="changed"/>;
    /// the pieces within <see cref="TerrainCeiling.ChangeReach"/> of one are to be redrawn. The first
    /// read adds nothing: every piece is new then. True when anything changed.</summary>
    public bool Refresh(WorldState world, GameDatabase database, List<int> changed)
    {
        TerrainCeiling.MarkClosed(world, database, _read);
        return Refresh(_read, changed);
    }

    /// <summary><see cref="Refresh(WorldState, GameDatabase, List{int})"/> from a closed map.</summary>
    public bool Refresh(ReadOnlySpan<bool> closed, List<int> changed)
    {
        if (!_filled)
        {
            closed.CopyTo(_closed);
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    _nibbles[(y * _width) + x] = (byte)TerrainCeiling.Nibble(_width, _height, _closed, x, y);
                }
            }

            _filled = true;
            return true;
        }

        int first = changed.Count;
        for (int i = 0; i < _closed.Length; i++)
        {
            if (_closed[i] != closed[i])
            {
                _closed[i] = closed[i];
                changed.Add(i);
            }
        }

        // A tile's nibble reads the closed tiles from 4 before it to 3 after it.
        for (int i = first; i < changed.Count; i++)
        {
            int x = changed[i] % _width, y = changed[i] / _width;
            for (int b = Math.Max(0, y - 3); b <= Math.Min(_height - 1, y + 4); b++)
            {
                for (int a = Math.Max(0, x - 3); a <= Math.Min(_width - 1, x + 4); a++)
                {
                    _nibbles[(b * _width) + a] = (byte)TerrainCeiling.Nibble(_width, _height, _closed, a, b);
                }
            }
        }
        return changed.Count > first;
    }

    /// <summary>True when (x, y) gets a roof piece.</summary>
    public bool Has(int x, int y)
    {
        return TerrainCeiling.Has(_width, _height, _closed, x, y);
    }

    /// <summary>The roof's height at half-tile grid point (gx, gy), the plane point (gx/2, gy/2):
    /// what <see cref="TerrainCeiling.Heights"/> gives every piece that has the point, and the same
    /// blend beyond them (-2 over closed ground).</summary>
    public float Height(int gx, int gy)
    {
        int x = gx >> 1, y = gy >> 1, c = gx & 1, r = gy & 1;
        int s = ((2 - c) * (2 - r) * Nibble(x, y)) + ((2 - c) * r * Nibble(x, y + 1))
            + (c * (2 - r) * Nibble(x + 1, y)) + (c * r * Nibble(x + 1, y + 1));
        float z = ((s * s) - (128 * s) - 4096) / 2048f;
        if (z <= TerrainCeiling.OpenLimit || (r | c) == 0 || !Has(x, y))
        {
            return z;
        }
        // A centre, or an edge's midpoint with an open tile on its other side too.
        bool held = (r & c) == 1 || (r == 0 ? Has(x, y - 1) : Has(x - 1, y));
        return held ? TerrainCeiling.OpenLimit : z;
    }

    /// <summary>Tile (x, y)'s nine vertices in <see cref="TerrainCeiling"/>'s order: positions bent
    /// through <paramref name="lattice"/> when there is one, and unit normals facing down (+z).
    /// The normals are the unbent height field's slope across a tile, so two pieces agree where
    /// they meet; DK2's own pairing of height differences is unread (kwd-terrain-rooms.md).</summary>
    public void Piece(int x, int y, TerrainLattice? lattice, Span<Vector3> positions, Span<Vector3> normals)
    {
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                int gx = (2 * x) + c, gy = (2 * y) + r, at = (3 * r) + c;
                var point = new Vector3(gx * 0.5f, gy * 0.5f, Height(gx, gy));
                positions[at] = lattice != null ? lattice.Deform(point, x, y) : point;
                normals[at] = Vector3.Normalize(new Vector3(
                    Height(gx - 1, gy) - Height(gx + 1, gy), Height(gx, gy - 1) - Height(gx, gy + 1), 1f));
            }
        }
    }

    private int Nibble(int x, int y)
    {
        return (uint)x < (uint)_width && (uint)y < (uint)_height ? _nibbles[(y * _width) + x] : 0;
    }
}
