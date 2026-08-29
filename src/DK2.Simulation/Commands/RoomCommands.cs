using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Builds a room on a set of tiles, paying for it.
///
/// The player-facing mechanic is CONFIRMED: a keeper picks a room, paints tiles
/// with it, and gold leaves the treasury as the tiles change. What is <b>ours,
/// not DK2's</b>, is the same thing as in <see cref="TagTilesCommand"/> — the
/// shape of the command. The original is a drag over the map; whether a drag
/// that crosses one unbuildable tile abandons the whole rectangle or skips that
/// tile and carries on is a question about the interface, and this command
/// answers it the second way because the simulation has to answer it somehow.
/// Tiles are considered in the order given, which matters when the keeper can
/// afford some of them and not all.
/// </summary>
public sealed record BuildRoomCommand(byte PlayerId, byte RoomId, IReadOnlyList<TilePos> Tiles)
    : PlayerCommand(PlayerId)
{
    public static BuildRoomCommand At(byte playerId, byte roomId, params TilePos[] tiles) =>
        new(playerId, roomId, tiles);

    /// <summary>Every tile in a rectangle, in row-major order.</summary>
    public static BuildRoomCommand Rect(byte playerId, byte roomId, int x0, int y0, int x1, int y1)
    {
        var tiles = new List<TilePos>();
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
            for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                tiles.Add(new TilePos(x, y));
        return new BuildRoomCommand(playerId, roomId, tiles);
    }

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(RoomId);
        hash.Add(Tiles.Count);
        foreach (TilePos t in Tiles)
        {
            hash.Add(t.X);
            hash.Add(t.Y);
        }
    }
}
