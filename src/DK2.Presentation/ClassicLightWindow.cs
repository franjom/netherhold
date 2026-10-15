// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Prepared source lists in native head-to-next order; memory must remain stable during Gather.</summary>
public readonly record struct ClassicLightCacheCell(
    ReadOnlyMemory<ClassicPointLight> StaticSources,
    ReadOnlyMemory<ClassicPointLight> DynamicSources);

/// <summary>Prepared cache entries, including any border cells outside the map; absent cells return default.</summary>
public interface IClassicLightCache
{
    ClassicLightCacheCell GetCell(TilePos tile);
}

/// <summary>Native rolling candidate order; caller owns visibility enumeration and static refresh stamps.</summary>
public sealed class ClassicLightWindow
{
    private const int Side = 7, Border = 3;
    private readonly int[] _columns = new int[Side];
    private readonly ClassicLightCacheCell[] _cells = new ClassicLightCacheCell[Side * Side];
    private TilePos _previous;
    private bool _initialized;
    private int _cursor;

    /// <summary>Call before each visible-tile enumeration, even if its first tile follows the previous one.</summary>
    public void Reset()
    {
        _initialized = false;
        Array.Clear(_cells, 0, _cells.Length);
    }

    /// <summary>Rebuilds dynamic slots every call, retaining static slots unless refreshStatic is true.</summary>
    public void Gather(ClassicLightTable table, IClassicLightCache cache, bool refreshStatic)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        TilePos target = table.Tile;
        if (_initialized && target.Y == _previous.Y && target.X == _previous.X + 1)
        {
            _columns[_cursor] = target.X + Border;
            _cursor = (_cursor + 1) % Side;
        }
        else
        {
            for (int column = 0; column < Side; column++)
                _columns[column] = target.X - Border + column;
            _cursor = 0;
        }
        _previous = target;
        _initialized = true;

        // Keep physical column order: sorting world X changes equal-distance overflow winners.
        for (int row = 0; row < Side; row++)
        for (int column = 0; column < Side; column++)
            _cells[row * Side + column] = cache.GetCell(new TilePos(_columns[column], target.Y - Border + row));

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
