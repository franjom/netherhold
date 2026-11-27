// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>One tile's terrain as <see cref="TerrainTileBuilder"/> last left it, in buffers the tile keeps and the
/// builder refills: the vertices in the renderers' frame and, per key slot, the triangles numbered from the tile's first
/// vertex. A chunk copies these; a rebuild of an unchanged tile need not run again.</summary>
public sealed class TerrainTileGeometry
{
    internal readonly List<TerrainTileBuilder.PlacedPiece> Pieces = new List<TerrainTileBuilder.PlacedPiece>();
    internal readonly List<TerrainTileBuilder.TriangleGroup> Groups = new List<TerrainTileBuilder.TriangleGroup>();
    internal int[] PieceStarts = new int[8];
    internal int X, Y;
    internal bool Red;
    internal bool StateTexture;
    internal Vector2 DefaultStateSource;

    private TerrainMaterialKey[] _keys = new TerrainMaterialKey[4];
    private TerrainIndexList[] _triangles = new TerrainIndexList[4];
    private Vector3[] _positions = new Vector3[32], _normals = new Vector3[32];
    private Vector2[] _uvs = new Vector2[32], _stateSources = new Vector2[32];

    /// <summary>The key of each slot, in the tile's first-use order; <see cref="KeyCount"/> are in use.</summary>
    public TerrainMaterialKey[] Keys
    {
        get
        {
            return _keys;
        }
    }

    public int KeyCount { get; private set; }

    /// <summary>Each slot's triangles, three indices a triangle.</summary>
    public TerrainIndexList[] Triangles
    {
        get
        {
            return _triangles;
        }
    }

    /// <summary><see cref="VertexCount"/> of each are in use.</summary>
    public Vector3[] Positions
    {
        get
        {
            return _positions;
        }
    }

    public Vector3[] Normals
    {
        get
        {
            return _normals;
        }
    }

    public Vector2[] Uvs
    {
        get
        {
            return _uvs;
        }
    }

    /// <summary>Each vertex's tag source tile and tint source tile; -1 disables, -2-tile selects quarter tint.</summary>
    public Vector2[] StateSources
    {
        get
        {
            return _stateSources;
        }
    }

    public int VertexCount { get; internal set; }

    /// <summary>The light table every vertex of the tile reads: the tile's index and 1, the terrain kind.</summary>
    public Vector2 LightAnchor { get; private set; }

    internal void Reset(int x, int y, int width, bool red)
    {
        Pieces.Clear();
        Groups.Clear();
        KeyCount = 0;
        X = x;
        Y = y;
        Red = red;
        LightAnchor = new Vector2((y * width) + x, 1);
    }

    internal int SlotOf(in TerrainMaterialKey key)
    {
        for (int i = 0; i < KeyCount; i++)
        {
            if (_keys[i].Equals(key))
            {
                return i;
            }
        }

        if (KeyCount == _keys.Length)
        {
            Array.Resize(ref _keys, KeyCount * 2);
            Array.Resize(ref _triangles, KeyCount * 2);
        }
        _keys[KeyCount] = key;
        _triangles[KeyCount] ??= new TerrainIndexList();
        return KeyCount++;
    }

    internal void EnsureVertices(int needed)
    {
        if (_positions.Length >= needed)
        {
            return;
        }

        int size = Math.Max(needed, _positions.Length * 2);
        Array.Resize(ref _positions, size);
        Array.Resize(ref _normals, size);
        Array.Resize(ref _uvs, size);
        Array.Resize(ref _stateSources, size);
    }
}

/// <summary>A growing list of vertex indices that keeps its array across rebuilds.</summary>
public sealed class TerrainIndexList
{
    private int[] _items = new int[16];

    /// <summary><see cref="Count"/> of them are in use.</summary>
    public int[] Items
    {
        get
        {
            return _items;
        }
    }

    public int Count { get; internal set; }

    public ReadOnlySpan<int> AsSpan()
    {
        return new ReadOnlySpan<int>(_items, 0, Count);
    }

    internal void Add(int value)
    {
        if (Count == _items.Length)
        {
            Array.Resize(ref _items, Count * 2);
        }

        _items[Count++] = value;
    }
}
