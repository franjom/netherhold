// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Simulation.Commands;

namespace DK2.Simulation;

/// <summary>
/// A recording of what the player told DK2 to do, replayable as input frames.
///
/// <para>The probe cannot see mouse clicks, so it diffs the per-player task
/// pool instead (docs/research/player-task-pool.md). That pool is a <b>work
/// pipeline, not a set of marks</b>: a tile sits in several kind lists at once
/// and migrates between them as an imp takes it up, so a row is not an event.
/// What is an event is a tile entering the pool or leaving it altogether, and
/// that is what this class reads out.</para>
/// </summary>
public sealed class InputLog
{
    public const string Header =
        "wall,tick,player,event,kind,x,y,type,data,id,workers";

    private readonly Dictionary<long, InputFrame> _frames;

    private InputLog(
        Dictionary<long, InputFrame> frames, IReadOnlyList<long> turns, InputLogTally tally)
    {
        _frames = frames;
        Turns = turns;
        Tally = tally;
    }

    /// <summary>Every turn that carries at least one command, ascending.</summary>
    public IReadOnlyList<long> Turns { get; }

    public InputLogTally Tally { get; }

    /// <summary>What was issued on <paramref name="turn"/>, in DK2 turn numbers.</summary>
    public InputFrame At(long turn) =>
        _frames.TryGetValue(turn, out InputFrame frame) ? frame : InputFrame.Empty;

    /// <summary>
    /// The kinds a player's mark enters the pool as: <b>1 is rock and 2 is
    /// gold</b>, CONFIRMED (owner, 2026-09-10) — four marks placed as two rock
    /// and two gold arrived as exactly two of each. A tile entering under any
    /// other kind is DK2 giving one of its imps a job, not a command.
    /// </summary>
    public static readonly HashSet<byte> TagKinds = [1, 2];

    public static InputLog Read(
        string path, long lag = 1, long origin = 1, HashSet<byte>? kinds = null)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        using var reader = File.OpenText(path);
        return Read(reader, path, lag, origin, kinds);
    }

    /// <summary>
    /// Reads a <c>--tags</c> log. <paramref name="lag"/> is how many turns late
    /// the probe's stamp is (one, as every sampled column is — ADR-0119);
    /// <paramref name="origin"/> is the DK2 turn our turn 1 corresponds to, so
    /// a capture that did not start at the level's first turn can still be
    /// lined up. Neither is inferred from the data: both are the caller's claim
    /// about the capture, and getting them wrong shifts every command.
    /// </summary>
    public static InputLog Read(
        TextReader reader, string name, long lag = 1, long origin = 1,
        HashSet<byte>? kinds = null)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        kinds ??= TagKinds;

        string? line = reader.ReadLine();
        if (line != Header)
            throw new InvalidDataException(
                $"'{name}' does not have the task-pool probe's header.");

        var rows = new List<Row>();
        for (int number = 2; (line = reader.ReadLine()) is not null; number++)
        {
            if (line.Length == 0) continue;
            rows.Add(Row.Parse(line, name, number));
        }

        return Build(rows, kinds, lag, origin);
    }

    private static InputLog Build(List<Row> rows, HashSet<byte> kinds, long lag, long origin)
    {
        var pool = new Dictionary<(byte Player, TilePos Tile), Occupancy>();
        var pending = new SortedDictionary<long, SortedDictionary<byte, TurnMarks>>();
        var tally = new Counters { Rows = rows.Count };

        foreach (IGrouping<long, Row> sample in rows.GroupBy(r => r.Stamp).OrderBy(g => g.Key))
        {
            // Tags before untags inside one sample. The probe reports net state,
            // so a tile changing kind lists shows as both; adding first makes
            // that a migration rather than a spurious leave and re-enter.
            foreach (Row row in sample.OrderByDescending(r => r.Tagged))
                Apply(row, pool, kinds, pending, tally, lag, origin);
        }

        return Emit(pending, tally, rows);
    }

    private static void Apply(
        in Row row, Dictionary<(byte, TilePos), Occupancy> pool, HashSet<byte> kinds,
        SortedDictionary<long, SortedDictionary<byte, TurnMarks>> pending,
        Counters tally, long lag, long origin)
    {
        (byte, TilePos) key = (row.Player, row.Tile);
        if (!pool.TryGetValue(key, out Occupancy? held)) pool[key] = held = new Occupancy();

        bool was = held.Kinds.Count > 0;
        if (row.Tagged)
        {
            held.Kinds.Add(row.Kind);
        }
        else
        {
            held.Kinds.Remove(row.Kind);
        }

        // The task's own Worker slots, not the kind it sat in. A dig that an
        // imp finished and a mark the player dropped both leave the pool, and
        // the kind transition between them is often not sampled at all -- gold
        // dug on the first live capture showed none. A worker is recorded.
        if (row.Workers > 0 || !kinds.Contains(row.Kind)) held.Worked = true;

        bool now = held.Kinds.Count > 0;
        if (was == now)
        {
            tally.Migrations++;
            return;
        }

        // Entering under a work kind is an imp taking a job, and leaving a tile
        // an imp had taken is that job finishing. Neither is the player, and
        // replaying either would put marks in our world DK2 never got told about.
        bool player = now ? kinds.Contains(row.Kind) : !held.Worked;
        if (!player)
        {
            tally.Worked++;
            if (!now) pool.Remove(key);
            return;
        }

        long turn = row.Stamp - lag - origin + 1;
        if (!pending.TryGetValue(turn, out SortedDictionary<byte, TurnMarks>? byPlayer))
            pending[turn] = byPlayer = [];
        if (!byPlayer.TryGetValue(row.Player, out TurnMarks marks))
            byPlayer[row.Player] = marks = new TurnMarks();
        (now ? marks.Tagged : marks.Untagged).Add(row.Tile);
        if (!now) pool.Remove(key);
    }

    private static InputLog Emit(
        SortedDictionary<long, SortedDictionary<byte, TurnMarks>> pending,
        Counters tally, List<Row> rows)
    {
        var frames = new Dictionary<long, InputFrame>();
        var turns = new List<long>();

        foreach ((long turn, SortedDictionary<byte, TurnMarks> byPlayer) in pending)
        {
            var issued = new List<PlayerCommand>();
            foreach ((byte player, TurnMarks marks) in byPlayer)
            {
                if (marks.Untagged.Count > 0)
                    issued.Add(new TagTilesCommand(player, Sorted(marks.Untagged), false));
                if (marks.Tagged.Count > 0)
                    issued.Add(new TagTilesCommand(player, Sorted(marks.Tagged), true));
            }

            if (issued.Count == 0) continue;
            if (turn < 1)
            {
                tally.Before += issued.Count;
                continue;
            }

            tally.Commands += issued.Count;
            frames[turn] = new InputFrame(issued);
            turns.Add(turn);
        }

        return new InputLog(frames, turns, tally.Freeze(
            rows.Count == 0 ? 0 : rows.Min(r => r.Stamp),
            rows.Count == 0 ? 0 : rows.Max(r => r.Stamp)));
    }

    /// <summary>
    /// Row-major, so a replay does not depend on the probe's emission order.
    /// The order within a frame is ours and DK2's is UNKNOWN, but a tag is
    /// per-tile and independent, so no rule here can depend on it.
    /// </summary>
    private static TilePos[] Sorted(HashSet<TilePos> tiles) =>
        [.. tiles.OrderBy(t => t.Y).ThenBy(t => t.X)];

    /// <summary>Which kind lists hold one tile, and whether an imp ever took it.</summary>
    private sealed class Occupancy
    {
        public HashSet<byte> Kinds { get; } = [];

        public bool Worked { get; set; }
    }

    private sealed class TurnMarks
    {
        public HashSet<TilePos> Tagged { get; } = [];

        public HashSet<TilePos> Untagged { get; } = [];
    }

    private sealed class Counters
    {
        public int Rows;
        public int Commands;
        public int Migrations;
        public int Worked;
        public int Before;

        public InputLogTally Freeze(long first, long last) =>
            new(Rows, Commands, Migrations, Worked, Before, first, last);
    }

    private readonly record struct Row(
        long Stamp, byte Player, bool Tagged, byte Kind, TilePos Tile, int Workers)
    {
        public static Row Parse(string line, string name, int number)
        {
            string[] c = line.Split(',');
            if (c.Length != 11)
                throw new InvalidDataException(
                    $"'{name}' line {number} has {c.Length} columns; expected 11.");

            long N(int i) =>
                long.TryParse(c[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)
                    ? v
                    : throw new InvalidDataException(
                        $"'{name}' line {number} has '{c[i]}' where a number was expected.");

            bool tagged = c[3] switch
            {
                "tag" => true,
                "untag" => false,
                _ => throw new InvalidDataException(
                    $"'{name}' line {number} has event '{c[3]}'; expected tag or untag."),
            };

            return new Row(
                N(1), (byte)N(2), tagged, (byte)N(4),
                new TilePos((int)N(5), (int)N(6)), (int)N(10));
        }
    }
}

/// <summary>
/// What a log contained, so a replay can report on its own input rather than
/// only on its output. <paramref name="Migrations"/> counts rows that moved a
/// tile between kind lists without adding or removing it, and
/// <paramref name="Worked"/> counts pool changes attributable to DK2's own
/// imps rather than to the player — taking a job or finishing one.
/// </summary>
public readonly record struct InputLogTally(
    int Rows, int Commands, int Migrations, int Worked, int Before,
    long FirstStamp, long LastStamp);
