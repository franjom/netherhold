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
