// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Persistent prepared light cells and draw tables for one renderer view.</summary>
public sealed class ClassicWorldLightCache : IClassicLightCache
{
    private readonly int _width, _height;
    private readonly ClassicPointLight?[] _terrain, _torches;
    private readonly ClassicPointLight[]?[] _sourceBuffers;
    private readonly ClassicLightCacheCell[] _cells;
    private readonly bool[] _dirty, _prepared;
    private readonly List<int> _preparedIndices = new();
    private readonly HashSet<TilePos> _visible = new();
    private readonly Dictionary<TilePos, ClassicLightTable> _tables = new();
    private readonly ClassicLightWindow _window = new();
    private ClassicLightTable[] _visibleTables = Array.Empty<ClassicLightTable>();
    private int _visibleCount;

    public ClassicWorldLightCache(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _width = width; _height = height;
        int count = checked(width * height);
        _terrain = new ClassicPointLight?[count];
        _torches = new ClassicPointLight?[count];
        _sourceBuffers = new ClassicPointLight[count][];
        _cells = new ClassicLightCacheCell[count];
        _dirty = new bool[count];
        _prepared = new bool[count];
        Array.Fill(_dirty, true);
    }

    public ClassicLightFrame Frame { get; private set; }
    public ReadOnlySpan<ClassicLightTable> VisibleTables => _visibleTables.AsSpan(0, _visibleCount);
    public void SetTerrain(TilePos tile, ClassicPointLight? light) => SetSource(_terrain, tile, light);
    public void SetTorch(TilePos tile, ClassicPointLight? light) => SetSource(_torches, tile, light);

    public void PrepareFrame(ReadOnlySpan<TilePos> visible)
    {
        _visible.Clear();
        foreach (TilePos tile in visible)
            if (!_visible.Add(tile)) throw new ArgumentException("Visible enumeration contains a duplicate tile.", nameof(visible));
        _window.Reset();
        Frame = new ClassicLightFrame(unchecked(Frame.Value + 1));
        foreach (int index in _preparedIndices) _prepared[index] = false;
        _preparedIndices.Clear();
        foreach (TilePos tile in visible)
        {
            PrepareCell(tile);
            int left = _visible.Contains(new TilePos(tile.X - 1, tile.Y)) ? tile.X : tile.X - 3;
            int right = _visible.Contains(new TilePos(tile.X + 1, tile.Y)) ? tile.X : tile.X + 3;
            int top = _visible.Contains(new TilePos(tile.X, tile.Y - 1)) ? tile.Y : tile.Y - 3;
            int bottom = _visible.Contains(new TilePos(tile.X, tile.Y + 1)) ? tile.Y : tile.Y + 3;
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++) PrepareCell(new TilePos(x, y));
        }
        if (_visibleTables.Length < visible.Length)
            Array.Resize(ref _visibleTables, Math.Max(visible.Length, _visibleTables.Length * 2 + 16));
        if (_visibleCount > visible.Length) Array.Clear(_visibleTables, visible.Length, _visibleCount - visible.Length);
        _visibleCount = visible.Length;
        for (int index = 0; index < visible.Length; index++)
        {
            TilePos tile = visible[index];
            if (!_tables.TryGetValue(tile, out ClassicLightTable? table))
                _tables.Add(tile, table = new ClassicLightTable(tile));
            _window.Gather(table, this, Frame);
            _visibleTables[index] = table;
        }
    }

    public ClassicLightCacheCell GetCell(TilePos tile) => Contains(tile) ? _cells[tile.Y * _width + tile.X] : default;
    public bool IsVisible(TilePos tile) => _visible.Contains(tile);

    private void SetSource(ClassicPointLight?[] inputs, TilePos tile, ClassicPointLight? light)
    {
        if (!Contains(tile)) throw new ArgumentOutOfRangeException(nameof(tile));
        int index = tile.Y * _width + tile.X;
        if (Nullable.Equals(inputs[index], light)) return;
        inputs[index] = light;
        _dirty[index] = true;
    }

    private bool Contains(TilePos tile) => (uint)tile.X < (uint)_width && (uint)tile.Y < (uint)_height;

    private void PrepareCell(TilePos tile)
    {
        if (!Contains(tile)) return;
        int index = tile.Y * _width + tile.X;
        if (_prepared[index]) return;
        _prepared[index] = true;
        _preparedIndices.Add(index);
        if (!_dirty[index]) return;
        int count = (_terrain[index].HasValue ? 1 : 0) + (_torches[index].HasValue ? 1 : 0);
        ClassicPointLight[]? buffer = _sourceBuffers[index];
        if (count > 0)
        {
            buffer ??= _sourceBuffers[index] = new ClassicPointLight[2];
            int slot = 0;
            if (_terrain[index] is ClassicPointLight terrain) buffer[slot++] = terrain;
            if (_torches[index] is ClassicPointLight torch) buffer[slot] = torch;
        }
        _cells[index] = new(buffer is null ? default : buffer.AsMemory(0, count), default, Frame);
        _dirty[index] = false;
    }

    public void Clear()
    {
        _window.Reset();
        Array.Clear(_terrain, 0, _terrain.Length);
        Array.Clear(_torches, 0, _torches.Length);
        Array.Clear(_cells, 0, _cells.Length);
        Array.Clear(_prepared, 0, _prepared.Length);
        Array.Clear(_visibleTables, 0, _visibleTables.Length);
        Array.Fill(_dirty, true);
        _preparedIndices.Clear(); _visible.Clear(); _tables.Clear();
        _visibleCount = 0;
        Frame = default;
    }
}
