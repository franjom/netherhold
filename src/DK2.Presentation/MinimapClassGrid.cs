// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

public readonly record struct MinimapTileChange(TilePos Position, MinimapColourClass ColourClass);

/// <summary>Renderer-owned class storage and a coalesced batch of changed cells.</summary>
public sealed class MinimapClassGrid
{
    private readonly MinimapColourClass[] _cells;
    private readonly MinimapTileChange[] _changes;
    private readonly int[] _changeSlots;
    private int _changeCount;

    public MinimapClassGrid(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        int count = checked(width * height);
        _cells = new MinimapColourClass[count];
        _changes = new MinimapTileChange[count];
        _changeSlots = new int[count];
        for (int i = 0; i < count; i++)
        {
            _cells[i] = MinimapColourClass.Unknown;
            _changes[i] = new MinimapTileChange(new TilePos(i % width, i / width), MinimapColourClass.Unknown);
            _changeSlots[i] = i;
        }
        _changeCount = count;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlySpan<MinimapColourClass> Cells => _cells;
    public ReadOnlySpan<MinimapTileChange> Changes => _changes.AsSpan(0, _changeCount);
    public MinimapColourClass this[TilePos position] => _cells[IndexOf(position)];

    public bool Set(TilePos position, MinimapColourClass colourClass)
    {
        int index = IndexOf(position);
        if (_cells[index] == colourClass) return false;
        _cells[index] = colourClass;
        int slot = _changeSlots[index];
        if (slot < 0) _changeSlots[index] = slot = _changeCount++;
        _changes[slot] = new MinimapTileChange(position, colourClass);
        return true;
    }

    /// <summary>Queues every cell for upload, for a renderer whose copy of the grid was lost.</summary>
    public void MarkAllChanged()
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            _changes[i] = new MinimapTileChange(new TilePos(i % Width, i / Width), _cells[i]);
            _changeSlots[i] = i;
        }
        _changeCount = _cells.Length;
    }

    public void ClearChanges()
    {
        for (int i = 0; i < _changeCount; i++)
            _changeSlots[IndexOf(_changes[i].Position)] = -1;
        _changeCount = 0;
    }

    private int IndexOf(TilePos position)
    {
        if ((uint)position.X >= (uint)Width || (uint)position.Y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(position));
        return position.Y * Width + position.X;
    }
}
