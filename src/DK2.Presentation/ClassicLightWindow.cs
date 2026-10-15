// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Snapshot of native list heads and build stamp; backing memory stays stable until Reset.</summary>
public readonly record struct ClassicLightCacheCell(
    ReadOnlyMemory<ClassicPointLight> StaticSources,
    ReadOnlyMemory<ClassicPointLight> DynamicSources,
    ClassicLightFrame BuildFrame = default);

/// <summary>
/// Prepared entries including map borders; absent cells return default.
/// A rebuilt empty cell retains its build stamp so it can invalidate previous sources.
/// </summary>
public interface IClassicLightCache
{
    ClassicLightCacheCell GetCell(TilePos tile);
}

/// <summary>Native rolling candidate order and stamp invalidation; caller supplies a prepared visible enumeration.</summary>
public sealed class ClassicLightWindow
{
    private const int Side = 7, Border = 3;
    private readonly int[] _columns = new int[Side];
    private readonly ClassicLightCacheCell[] _cells = new ClassicLightCacheCell[Side * Side];
    private TilePos _previous;
    private bool _initialized;
    private int _cursor;
    private int _dirtyColumns;

    /// <summary>Uses native unsigned build stamps for static refresh; dynamic slots always rebuild.</summary>
    public void Gather(ClassicLightTable table, IClassicLightCache cache, ClassicLightFrame frame)
    {
        PrepareCells(table, cache);
        bool refreshStatic = _dirtyColumns != 0;
        Rebuild(table, refreshStatic);
        if (refreshStatic) table.CompleteStatic(frame);
    }

    /// <summary>Call before each visible-tile enumeration, even if its first tile follows the previous one.</summary>
    public void Reset()
    {
        _initialized = false;
        _dirtyColumns = 0;
        Array.Clear(_cells, 0, _cells.Length);
    }

    private void PrepareCells(ClassicLightTable table, IClassicLightCache cache)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        TilePos target = table.Tile;
        if (_initialized && target.Y == _previous.Y && target.X == _previous.X + 1)
        {
            _columns[_cursor] = target.X + Border;
            _dirtyColumns &= ~(1 << _cursor);
            for (int row = 0; row < Side; row++) ReadCell(table, cache, row, _cursor);
            _cursor = (_cursor + 1) % Side;
        }
        else
        {
            for (int column = 0; column < Side; column++)
                _columns[column] = target.X - Border + column;
            _cursor = 0;
            _dirtyColumns = 0;
            for (int row = 0; row < Side; row++)
            for (int column = 0; column < Side; column++) ReadCell(table, cache, row, column);
        }
        _previous = target;
        _initialized = true;
    }

    private void ReadCell(ClassicLightTable table, IClassicLightCache cache, int row, int column)
    {
        ClassicLightCacheCell cell = cache.GetCell(new TilePos(_columns[column], table.Tile.Y - Border + row));
        _cells[row * Side + column] = cell;
        if (cell.BuildFrame.Value > table.StaticFrame.Value) _dirtyColumns |= 1 << column;
    }

    private void Rebuild(ClassicLightTable table, bool refreshStatic)
    {
        // Keep physical column order: sorting world X changes equal-distance overflow winners.
        if (refreshStatic)
        {
            table.BeginStatic();
            for (int row = 0; row < Side; row++)
            for (int column = 0; column < Side; column++)
                table.AppendStatic(_cells[row * Side + column].StaticSources.Span);
        }
        table.BeginDynamic();
        for (int column = 0; column < Side; column++)
        for (int row = 0; row < Side; row++)
            table.AppendDynamic(_cells[row * Side + column].DynamicSources.Span);
    }
}
