// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>One roof chunk's vertices and triangles as an adapter uploads them, reused across
/// rebuilds. Points are the renderers' frame (<see cref="RenderWorld"/>): X east, Y up, Z north.</summary>
public sealed class TerrainCeilingGeometry
{
    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector2> Uvs { get; } = new();
    /// <summary>Each vertex's tile index and <see cref="TerrainCeilingScheduler.RoofAnchor"/>.</summary>
    public List<Vector2> LightAnchors { get; } = new();
    /// <summary>DK2's triangle order: clockwise seen from below in the renderers' frame.</summary>
    public List<int> Triangles { get; } = new();

    public void Clear()
    {
        Positions.Clear();
        Normals.Clear();
        Uvs.Clear();
        LightAnchors.Clear();
        Triangles.Clear();
    }
}

/// <summary>The possession view's roof in chunks of <see cref="ChunkSize"/> tiles a side, as an adapter
/// draws it: <see cref="TerrainCeilingField"/>'s pieces, which chunks a change reaches, and each
/// chunk's geometry. The first read leaves every chunk to be built. An adapter shows the chunks
/// exactly while a possession camera holds the view (<see cref="PossessionScene.RoofShown"/>).</summary>
public sealed class TerrainCeilingScheduler
{
    public const int ChunkSize = 16;

    /// <summary>The lit shaders' tile anchor kind for a roof vertex: lit from its tile's light table,
    /// and given none of an adapter's fill light, so it is black where no DK2 light reaches.</summary>
    public const float RoofAnchor = 2f;

    private readonly TerrainLattice? _lattice;
    private readonly TerrainCeilingField _field;
    private readonly int _width, _height, _chunksX;
    private readonly bool[] _dirty;
    private readonly List<int> _changed = new();
    private readonly Vector3[] _piecePositions = new Vector3[TerrainCeiling.Vertices];
    private readonly Vector3[] _pieceNormals = new Vector3[TerrainCeiling.Vertices];
    private long _readTick = long.MinValue;
    private int _dirtyCount;

    public TerrainCeilingScheduler(int width, int height, TerrainLattice? lattice = null)
    {
        _lattice = lattice;
        _field = new TerrainCeilingField(width, height);
        _width = width;
        _height = height;
        _chunksX = (width + ChunkSize - 1) / ChunkSize;
        _dirty = new bool[_chunksX * ((height + ChunkSize - 1) / ChunkSize)];
        Clear();
    }

    public int ChunkCount
    {
        get
        {
            return _dirty.Length;
        }
    }

    /// <summary>True while a chunk waits for <see cref="TryBuild"/>.</summary>
    public bool AnyDirty
    {
        get
        {
            return _dirtyCount > 0;
        }
    }

    /// <summary>A presented tile changed, which may have changed the lattice around it.</summary>
    public void TileChanged(int x, int y)
    {
        MarkDirty(x, y, TerrainCeiling.BendReach);
    }

    /// <summary>Reads the world's closed tiles once a turn and marks the chunks a tile that opened or
    /// closed reaches. Call where the world may be read; true when a chunk is to be built.</summary>
    public bool Refresh(WorldState world, GameDatabase database)
    {
        long tick = world.CurrentTick.Value;
        if (tick != _readTick)
        {
            _readTick = tick;
            _changed.Clear();
            _field.Refresh(world, database, _changed);
            MarkChanged();
        }
        return AnyDirty;
    }

    /// <summary><see cref="Refresh(WorldState, GameDatabase)"/> from a closed map, whatever the turn.</summary>
    public bool Refresh(ReadOnlySpan<bool> closed)
    {
        _changed.Clear();
        _field.Refresh(closed, _changed);
        MarkChanged();
        return AnyDirty;
    }

    private void MarkChanged()
    {
        for (int i = 0; i < _changed.Count; i++)
        {
            MarkDirty(_changed[i] % _width, _changed[i] / _width, TerrainCeiling.ChangeReach);
        }
    }

    /// <summary>Fills <paramref name="geometry"/> with a chunk that waits to be built and returns true;
    /// false leaves it as it was. An empty geometry is a chunk with no open tile.</summary>
    public bool TryBuild(int chunk, TerrainCeilingGeometry geometry)
    {
        if (!_dirty[chunk])
        {
            return false;
        }

        _dirty[chunk] = false;
        _dirtyCount--;
        geometry.Clear();

        ReadOnlySpan<byte> triangles = TerrainCeiling.Triangles;
        int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
        for (int y = y0; y < Math.Min(y0 + ChunkSize, _height); y++)
        {
            for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
            {
                if (!_field.Has(x, y))
                {
                    continue;
                }

                _field.Piece(x, y, _lattice, _piecePositions, _pieceNormals);
                int start = geometry.Positions.Count;
                var anchor = new Vector2((y * _width) + x, RoofAnchor);
                for (int i = 0; i < TerrainCeiling.Vertices; i++)
                {
                    // DK2's frame (X east, Y south, Z down) is (x, -z, -y) of the renderers'.
                    Vector3 p = _piecePositions[i], n = _pieceNormals[i];
                    geometry.Positions.Add(new Vector3(p.X, -p.Z, -p.Y));
                    geometry.Normals.Add(new Vector3(n.X, -n.Z, -n.Y));
                    (float u, float v) = TerrainCeiling.Uv(i / 3, i % 3);
                    geometry.Uvs.Add(new Vector2(u, v));
                    geometry.LightAnchors.Add(anchor);
                }
                for (int i = 0; i < triangles.Length; i++)
                {
                    geometry.Triangles.Add(start + triangles[i]);
                }
            }
        }

        return true;
    }

    /// <summary>Every chunk waits again, and the next <see cref="Refresh"/> reads the world whatever its turn.</summary>
    public void Clear()
    {
        Array.Fill(_dirty, true);
        _dirtyCount = _dirty.Length;
        _readTick = long.MinValue;
    }

    private void MarkDirty(int x, int y, int reach)
    {
        int x0 = Math.Max(0, x - reach) / ChunkSize, x1 = Math.Min(_width - 1, x + reach) / ChunkSize;
        int y0 = Math.Max(0, y - reach) / ChunkSize, y1 = Math.Min(_height - 1, y + reach) / ChunkSize;
        for (int cy = y0; cy <= y1; cy++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                int chunk = (cy * _chunksX) + cx;
                if (_dirty[chunk])
                {
                    continue;
                }

                _dirty[chunk] = true;
                _dirtyCount++;
            }
        }
    }
}
