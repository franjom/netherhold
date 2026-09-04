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

namespace DK2.Core;

/// <summary>
/// A tile coordinate on the dungeon grid.
///
/// DK2 addresses tiles with small integer coordinates (CONFIRMED: Flame's `pos2ub` /
/// `pos2us` structs used by `CRoom.f2D_firstSlab` etc. are byte/ushort pairs).
/// </summary>
public readonly record struct TilePos(int X, int Y)
{
    public static TilePos operator +(TilePos a, TilePos b) => new(a.X + b.X, a.Y + b.Y);
    public static TilePos operator -(TilePos a, TilePos b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Chebyshev distance — adjacency including diagonals.</summary>
    public int ChebyshevDistance(TilePos other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public int ManhattanDistance(TilePos other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public override string ToString() => $"({X},{Y})";
}
