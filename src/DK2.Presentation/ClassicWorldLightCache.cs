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
    private readonly ClassicPointLight[]?[] _sourceBuffers, _dynamicBuffers;
    private readonly List<ObjectSource>?[] _objectsByTile;
    private readonly Dictionary<long, ObjectSource> _objects = new();
    /// <summary>The sources of each kind (objects, effects), so a kind's update scans only its own.</summary>
    private readonly List<ObjectSource>[] _entries = { new(), new() };

    /// <summary>A dynamic source: an object's light or an effect instance's. Both are engine lights
    /// in one list (<c>+0x33A</c>), so they share each tile's insertion order.</summary>
    private sealed class ObjectSource(long key, int tile, ClassicPointLight light)
    {
        public readonly long Key = key;
        public int Tile = tile;
        public ClassicPointLight Light = light;
        public bool Seen;
    }

    private const int ObjectKind = 0, EffectKind = 1;
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
        _dynamicBuffers = new ClassicPointLight[count][];
        _objectsByTile = new List<ObjectSource>[count];
        _cells = new ClassicLightCacheCell[count];
        _dirty = new bool[count];
        _prepared = new bool[count];
        Array.Fill(_dirty, true);
    }

    public ClassicLightFrame Frame { get; private set; }
    public ReadOnlySpan<ClassicLightTable> VisibleTables => _visibleTables.AsSpan(0, _visibleCount);
    public void SetTerrain(TilePos tile, ClassicPointLight? light) => SetSource(_terrain, tile, light);
    public void SetTorch(TilePos tile, ClassicPointLight? light) => SetSource(_torches, tile, light);

    public void UpdateObjects(IReadOnlyList<ObjectView> objects, float fraction, bool cheapLighting = false)
    {
        List<ObjectSource> entries = _entries[ObjectKind];
        foreach (ObjectSource source in entries) source.Seen = false;
        for (int i = 0; i < objects.Count; i++)
        {
            ObjectView thing = objects[i];
            // The tile follows the current position; the drawn light, the interpolated one.
            if (ClassicPointLight.FromObject(thing, cheapLighting) is not ClassicPointLight current) continue;
            Place(ObjectKind, thing.Id, current, ClassicPointLight.FromObject(thing, cheapLighting, fraction)!.Value);
        }
        RemoveUnseen(entries);
    }

    /// <summary>The lights of the view's effect instances this update (<c>0x004347E0</c>).</summary>
    public void UpdateEffects(IReadOnlyList<EffectLightView> effects, bool cheapLighting = false)
    {
        List<ObjectSource> entries = _entries[EffectKind];
        foreach (ObjectSource source in entries) source.Seen = false;
        for (int i = 0; i < effects.Count; i++)
        {
            if (ClassicPointLight.FromEffect(effects[i], cheapLighting) is not ClassicPointLight light) continue;
            Place(EffectKind, effects[i].Id, light, light);
        }
        RemoveUnseen(entries);
    }

    private void Place(int kind, int id, ClassicPointLight current, ClassicPointLight drawn)
    {
        var tile = new TilePos((int)Math.Floor(current.Position.X), (int)Math.Floor(-current.Position.Z));
        if (!Contains(tile)) return;
        int index = tile.Y * _width + tile.X;
        // An object and an effect may share an id; the kind keeps their sources apart.
        long key = ((long)kind << 32) | (uint)id;
        if (!_objects.TryGetValue(key, out ObjectSource? source))
        {
            source = new ObjectSource(key, index, drawn);
            _objects.Add(key, source);
            _entries[kind].Add(source);
            (_objectsByTile[index] ??= new List<ObjectSource>()).Add(source);
        }
        else if (source.Tile != index)
        {
            _objectsByTile[source.Tile]!.Remove(source);
            (_objectsByTile[index] ??= new List<ObjectSource>()).Add(source);
            source.Tile = index;
        }
        source.Light = drawn;
        source.Seen = true;
    }

    private void RemoveUnseen(List<ObjectSource> entries)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            ObjectSource source = entries[i];
            if (source.Seen) continue;
            _objectsByTile[source.Tile]!.Remove(source);
            _objects.Remove(source.Key);
            entries.RemoveAt(i);
        }
    }

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

    /// <summary>The light table a visible tile was given by the last <see cref="PrepareFrame"/>; false
    /// for a tile not drawn this frame, whose table may be stale.</summary>
    public bool TryGetVisibleTable(TilePos tile, out ClassicLightTable table)
    {
        if (_visible.Contains(tile) && _tables.TryGetValue(tile, out ClassicLightTable? found))
        {
            table = found;
            return true;
        }
        table = null!;
        return false;
    }

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
        ClassicLightCacheCell cell = _cells[index];
        if (_dirty[index])
        {
            int count = (_terrain[index].HasValue ? 1 : 0) + (_torches[index].HasValue ? 1 : 0);
            ClassicPointLight[]? buffer = _sourceBuffers[index];
            if (count > 0)
            {
                buffer ??= _sourceBuffers[index] = new ClassicPointLight[2];
                int slot = 0;
                if (_terrain[index] is ClassicPointLight terrain) buffer[slot++] = terrain;
                if (_torches[index] is ClassicPointLight torch) buffer[slot] = torch;
            }
            cell = new(buffer is null ? default : buffer.AsMemory(0, count), default, Frame);
            _dirty[index] = false;
        }
        int dynamicCount = _objectsByTile[index]?.Count ?? 0;
        ClassicPointLight[]? dynamicBuffer = _dynamicBuffers[index];
        if (dynamicCount > 0)
        {
            if (dynamicBuffer is null || dynamicBuffer.Length < dynamicCount)
                dynamicBuffer = _dynamicBuffers[index] = new ClassicPointLight[dynamicCount];
            for (int i = 0; i < dynamicCount; i++) dynamicBuffer[i] = _objectsByTile[index]![i].Light;
        }
        _cells[index] = cell with { DynamicSources = dynamicBuffer is null ? default : dynamicBuffer.AsMemory(0, dynamicCount) };
    }

    public void Clear()
    {
        _window.Reset();
        _objects.Clear();
        foreach (List<ObjectSource> entries in _entries) entries.Clear();
        foreach (var sources in _objectsByTile) sources?.Clear();
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
