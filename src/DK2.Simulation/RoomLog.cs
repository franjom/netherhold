// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation;

/// <summary>
/// A recording of the rooms a player placed and sold, replayable as input.
///
/// <para>DK2 keeps each keeper's rooms as a list from <c>player+0x36</c>, and
/// the probe diffs it (docs/research/room-list.md). A room carries its type,
/// its slab count and its bounding slabs, but <b>not its tile set</b>, and
/// while imps are still building it the two disagree. So a row is replayed
/// only where the bounds enclose exactly the slab count, and what is placed is
/// the difference from what this log has already placed — a room that settles
/// after six unsettled samples still arrives whole.</para>
/// </summary>
public sealed class RoomLog
{
    public const string Header =
        "wall,tick,player,event,room,type,x,y,size,x0,y0,x1,y1,owner";

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
        var placed = new Dictionary<int, HashSet<TilePos>>();
        var tally = new Counters();

        for (int number = 2; (line = reader.ReadLine()) is not null; number++)
        {
            if (line.Length == 0) continue;
            string[] cells = line.Split(',');
            if (cells.Length != 14)
                throw new InvalidDataException(
                    $"'{name}' line {number} has {cells.Length} columns; expected 14.");

            var row = Row.Parse(cells, name, number);
            tally.Rows++;
            if (row.Disowned) tally.Disowned++;
            long turn = row.Stamp - lag - origin + 1;
            PlayerCommand? command = Translate(row, placed, tally);
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
    /// One row into one command, or nothing. A row whose bounds do not enclose
    /// exactly its slab count says nothing about which slabs those are, so it
    /// is skipped; the next settled row for that room then carries whatever it
    /// gained in the meantime, because what is placed is measured against this
    /// log's own record rather than against the previous row.
    /// </summary>
    private static PlayerCommand? Translate(
        in Row row, Dictionary<int, HashSet<TilePos>> placed, Counters tally)
    {
        if (!placed.TryGetValue(row.Room, out HashSet<TilePos>? have)) have = [];

        if (row.Event == "gone")
        {
            placed.Remove(row.Room);
            if (have.Count > 0) return new SellRoomCommand(row.Player, Sorted(have));
            tally.Unsettled++;
            return null;
        }

        if (row.Bounds.Area != row.Size)
        {
            // Mid-construction: the slab count runs ahead of the bounds, so the
            // rectangle would be a guess. Placing it would put tiles in our
            // world DK2 has not put in its own.
            tally.Unsettled++;
            return null;
        }

        HashSet<TilePos> want = [.. row.Bounds.Tiles()];
        TilePos[] gained = [.. want.Where(t => !have.Contains(t))];
        TilePos[] lost = [.. have.Where(t => !want.Contains(t))];
        placed[row.Room] = want;

        if (gained.Length > 0) return new BuildRoomCommand(row.Player, (RoomTypeId)row.Type, Sorted(gained));
        if (lost.Length > 0) return new SellRoomCommand(row.Player, Sorted(lost));
        tally.Unsettled++;
        return null;
    }

    private static TilePos[] Sorted(IEnumerable<TilePos> tiles) =>
        [.. tiles.OrderBy(t => t.Y).ThenBy(t => t.X)];

    /// <summary><paramref name="Disowned"/>: the room's own PlayerId disagrees
    /// with the player whose list the walk found it in, so one of the two is
    /// wrong and the walk is the likelier.</summary>
    private readonly record struct Row(
        long Stamp, byte Player, string Event, int Room, byte Type, int Size, Rect Bounds,
        bool Disowned)
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
                N(1), (byte)N(13), c[3], (int)N(4), (byte)N(5), (int)N(8),
                new Rect((int)N(9), (int)N(10), (int)N(11), (int)N(12), (int)N(8)),
                (byte)N(2) != (byte)N(13));
        }
    }

    /// <summary>
    /// The box spanned by the room's first and last slab. They are the ends of
    /// a list, not a min and a max — run 10's settled lair spans (8,30) to
    /// (10,26), descending in y — so the corners are ordered here. For a
    /// rectangle filled in raster order the two ends are opposite corners,
    /// which is exactly the case the area-equals-slabs test admits.
    /// </summary>
    private readonly record struct Rect(int X0, int Y0, int X1, int Y1, int Size)
    {
        public int Area => (Math.Abs(X1 - X0) + 1) * (Math.Abs(Y1 - Y0) + 1);

        public TilePos[] Tiles()
        {
            var tiles = new List<TilePos>();
            for (int y = Math.Min(Y0, Y1); y <= Math.Max(Y0, Y1); y++)
                for (int x = Math.Min(X0, X1); x <= Math.Max(X0, X1); x++)
                    tiles.Add(new TilePos(x, y));
            return [.. tiles];
        }
    }

    private sealed class Counters
    {
        public int Rows;
        public int Commands;
        public int Unsettled;
        public int Before;
        public int Disowned;

        public RoomLogTally Freeze() =>
            new(Rows, Commands, Unsettled, Before, Disowned);
    }
}

/// <summary>
/// What a room log contained. <paramref name="Unsettled"/> counts rows whose
/// bounds did not enclose exactly their slab count — a room still being built
/// — which are skipped rather than approximated; a later settled row for the
/// same room carries what they would have placed.
/// </summary>
public readonly record struct RoomLogTally(
    int Rows, int Commands, int Unsettled, int Before, int Disowned);
