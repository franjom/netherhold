// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>One tile of a level's MAP grid — 4 bytes in the file.</summary>
public readonly record struct MapTile(byte TerrainId, byte PlayerId, byte Flags, byte Unknown);

/// <summary>
/// A level's tile grid as it exists in the KWD data: terrain id, owning player
/// and flags per cell, row-major.
///
/// This is the file's view of the map, not the simulation's. The simulation
/// derives its own mutable <c>WorldState</c> from one of these (tile health,
/// ownership changes, and so on live there, not here).
///
/// Layout CONFIRMED — see docs/research/kwd-terrain-rooms.md.
/// </summary>
public sealed class LevelMap
{
    private readonly MapTile[] _tiles;

    public int Width { get; }
    public int Height { get; }

    public LevelMap(int width, int height, MapTile[] tiles)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (tiles.Length != width * height)
            throw new ArgumentException(
                $"tile array is {tiles.Length} long, expected {width * height}.", nameof(tiles));

        Width = width;
        Height = height;
        _tiles = tiles;
    }

    public MapTile this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
            return _tiles[y * Width + x];
        }
    }

    public IReadOnlyList<MapTile> Tiles => _tiles;

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
}
