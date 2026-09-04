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

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Reads a level's tile grid from the MAP(100) record into a <see cref="LevelMap"/>.
///
/// Layout CONFIRMED: the MAP chunk header carries u32 width, u32 height, and
/// the content is exactly width * height * 4 bytes — one 4-byte tile per cell
/// in row-major order (terrainId, playerId, flag, unknown).
/// See docs/research/kwd-terrain-rooms.md.
/// </summary>
public static class MapGrid
{
    public static LevelMap? Parse(IEnumerable<KwdChunk> chunks)
    {
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Map) continue;

            int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk.HeaderBlock);
            int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk.HeaderBlock.AsSpan(4));
            int expected = width * height * 4;

            if (width <= 0 || height <= 0 || chunk.Content.Length < expected)
                throw new InvalidDataException(
                    $"MAP chunk claims {width}x{height} ({expected} bytes) but content is {chunk.Content.Length}.");

            var tiles = new MapTile[width * height];
            for (int i = 0; i < tiles.Length; i++)
            {
                int o = i * 4;
                tiles[i] = new MapTile(
                    chunk.Content[o], chunk.Content[o + 1],
                    chunk.Content[o + 2], chunk.Content[o + 3]);
            }
            return new LevelMap(width, height, tiles);
        }
        return null;
    }
}
