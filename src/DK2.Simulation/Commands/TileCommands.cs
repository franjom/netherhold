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

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Marks tiles for digging.
///
/// The player-facing mechanic is CONFIRMED: DK2 lets a keeper paint a rectangle
/// of rock with dig markers, imps then go and dig what is marked, and the
/// terrain table carries a per-type <c>Taggable</c> flag saying what may be
/// marked at all. What is <b>ours, not DK2's</b>, is the shape of this command:
/// an explicit list of tiles with an explicit tag/untag intent. The original's
/// interface is a drag-toggle, whose exact rule (does a drag that starts on a
/// tagged tile untag everything it touches?) is UNKNOWN and belongs to the UI
/// layer, not to the simulation.
/// </summary>
public sealed record TagTilesCommand(byte PlayerId, IReadOnlyList<TilePos> Tiles, bool Tagged)
    : PlayerCommand(PlayerId)
{
    public static TagTilesCommand Tag(byte playerId, params TilePos[] tiles) =>
        new(playerId, tiles, true);

    public static TagTilesCommand Untag(byte playerId, params TilePos[] tiles) =>
        new(playerId, tiles, false);

    /// <summary>Every tile in a rectangle, in row-major order.</summary>
    public static TagTilesCommand Rect(
        byte playerId, int x0, int y0, int x1, int y1, bool tagged = true)
    {
        var tiles = new List<TilePos>();
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                tiles.Add(new TilePos(x, y));
        return new TagTilesCommand(playerId, tiles, tagged);
    }

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tagged);
        hash.Add(Tiles.Count);
        foreach (TilePos t in Tiles)
        {
            hash.Add(t.X);
            hash.Add(t.Y);
        }
    }
}
