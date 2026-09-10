// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Simulation.Commands;

namespace DK2.Simulation;

/// <summary>
/// A recording of the rooms a player placed and sold, replayable as input.
///
/// <para>DK2 keeps each keeper's rooms as a list from <c>player+0x36</c>, and
/// the probe diffs it (docs/research/room-list.md). A room object carries its
/// type, its slab count and its bounding slabs, but <b>not its tile set</b> —
/// so a placement replays exactly only when the bounds enclose exactly as many
/// slabs as the room has. Anything else is counted, never guessed at.</para>
/// </summary>
public sealed class RoomLog
{
    public const string Header = "wall,tick,player,event,room,type,x,y,size,x0,y0,x1,y1";

    private readonly Dictionary<long, InputFrame> _frames;

    private RoomLog(
        Dictionary<long, InputFrame> frames, IReadOnlyList<long> turns, RoomLogTally tally)
    {
        _frames = frames;
        Turns = turns;
        Tally = tally;
    }

    public IReadOnlyList<long> Turns { get; }

    public RoomLogTally Tally { get; }

    public InputFrame At(long turn) =>
        _frames.TryGetValue(turn, out InputFrame frame) ? frame : InputFrame.Empty;

    public static RoomLog Read(string path, long lag = 1, long origin = 1)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        using var reader = File.OpenText(path);
        return Read(reader, path, lag, origin);
    }

    /// <summary>
    /// Reads a <c>--rooms</c> log. <paramref name="lag"/> and
    /// <paramref name="origin"/> mean what they do for <see cref="InputLog"/>:
    /// how late the probe's stamp is, and which DK2 turn our turn 1 is.
    /// </summary>
    public static RoomLog Read(TextReader reader, string name, long lag = 1, long origin = 1)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        string? line = reader.ReadLine();
        if (line != Header)
            throw new InvalidDataException($"'{name}' does not have the room probe's header.");

        var pending = new SortedDictionary<long, List<PlayerCommand>>();
        var shape = new Dictionary<int, Rect>();
        var tally = new Counters();

        for (int number = 2; (line = reader.ReadLine()) is not null; number++)
        {
            if (line.Length == 0) continue;
            string[] cells = line.Split(',');
            if (cells.Length != 13)
                throw new InvalidDataException(
                    $"'{name}' line {number} has {cells.Length} columns; expected 13.");

            var row = Row.Parse(cells, name, number);
            tally.Rows++;
            long turn = row.Stamp - lag - origin + 1;
            PlayerCommand? command = Translate(row, shape, tally);
            if (command is null) continue;
            if (turn < 1)
            {
                tally.Before++;
                continue;
            }

            if (!pending.TryGetValue(turn, out List<PlayerCommand>? list))
                pending[turn] = list = [];
            list.Add(command);
        }

        var frames = new Dictionary<long, InputFrame>();
        var turns = new List<long>();
        foreach ((long turn, List<PlayerCommand> list) in pending)
        {
            tally.Commands += list.Count;
            frames[turn] = new InputFrame(list);
            turns.Add(turn);
        }

        return new RoomLog(frames, turns, tally.Freeze());
    }

    /// <summary>
    /// One row into one command, or nothing. A room's tiles are recoverable
    /// only from its bounds, and only when they enclose exactly its slab count:
    /// a room grown around a corner has the same bounds as one that fills them,
    /// and no reading of the log tells the two apart.
    /// </summary>
    private static PlayerCommand? Translate(
        in Row row, Dictionary<int, Rect> shape, Counters tally)
    {
        shape.TryGetValue(row.Room, out Rect before);
        Rect now = row.Bounds;
        bool exact = now.Area == row.Size;

        switch (row.Event)
        {
            case "build" when exact:
                shape[row.Room] = now;
                return new BuildRoomCommand(row.Player, row.Type, now.Tiles());

            case "grow" when exact && before.Area == before.Size:
                shape[row.Room] = now with { Size = row.Size };
                return new BuildRoomCommand(row.Player, row.Type, now.Except(before));

            case "gone":
                shape.Remove(row.Room);
                return before.Area == before.Size
                    ? new SellRoomCommand(row.Player, before.Tiles())
                    : Unreplayable(tally);

            case "build":
            case "grow":
            case "shrink":
                shape[row.Room] = now with { Size = row.Size };
                return Unreplayable(tally);

            default:
                return Unreplayable(tally);
        }
    }

    private static PlayerCommand? Unreplayable(Counters tally)
    {
        tally.Unreplayable++;
        return null;
    }

    private readonly record struct Row(
        long Stamp, byte Player, string Event, int Room, byte Type, int Size, Rect Bounds)
    {
        public static Row Parse(string[] c, string name, int number)
        {
            long N(int i) =>
                long.TryParse(c[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)
                    ? v
                    : throw new InvalidDataException(
                        $"'{name}' line {number} has '{c[i]}' where a number was expected.");

            if (c[3] is not ("build" or "grow" or "shrink" or "gone"))
                throw new InvalidDataException(
                    $"'{name}' line {number} has event '{c[3]}'; expected build, grow, shrink or gone.");

            return new Row(
                N(1), (byte)N(2), c[3], (int)N(4), (byte)N(5), (int)N(8),
                new Rect((int)N(9), (int)N(10), (int)N(11), (int)N(12), (int)N(8)));
        }
    }

    /// <summary>The room's bounding slabs, with the slab count they are checked against.</summary>
    private readonly record struct Rect(int X0, int Y0, int X1, int Y1, int Size)
    {
        public int Area => X1 < X0 || Y1 < Y0 ? 0 : (X1 - X0 + 1) * (Y1 - Y0 + 1);

        public bool Contains(TilePos t) => t.X >= X0 && t.X <= X1 && t.Y >= Y0 && t.Y <= Y1;

        public TilePos[] Tiles()
        {
            var tiles = new List<TilePos>();
            for (int y = Y0; y <= Y1; y++)
                for (int x = X0; x <= X1; x++)
                    tiles.Add(new TilePos(x, y));
            return [.. tiles];
        }

        public TilePos[] Except(Rect other) => [.. Tiles().Where(t => !other.Contains(t))];
    }

    private sealed class Counters
    {
        public int Rows;
        public int Commands;
        public int Unreplayable;
        public int Before;

        public RoomLogTally Freeze() => new(Rows, Commands, Unreplayable, Before);
    }
}

/// <summary>
/// What a room log contained. <paramref name="Unreplayable"/> counts rooms
/// whose bounds do not enclose exactly their slab count, so the tile set is
/// not recoverable; those are dropped rather than approximated.
/// </summary>
public readonly record struct RoomLogTally(int Rows, int Commands, int Unreplayable, int Before);
