// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
