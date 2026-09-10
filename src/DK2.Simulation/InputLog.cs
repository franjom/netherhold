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
/// pool instead and writes one row per tile that became or stopped being
/// marked (docs/research/player-task-pool.md). That is already a command log
/// at turn resolution, which is the level <see cref="InputFrame"/> works at.
/// This class is the other half: rows in, frames out.</para>
/// </summary>
public sealed class InputLog
{
    public const string Header = "wall,tick,player,event,kind,x,y";

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

    public static InputLog Read(string path, long lag = 1, long origin = 1)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        using var reader = File.OpenText(path);
        return Read(reader, path, lag, origin);
    }

    /// <summary>
    /// Reads a <c>--tags</c> log. <paramref name="lag"/> is how many turns late
    /// the probe's stamp is (one, as every sampled column is — ADR-0119);
    /// <paramref name="origin"/> is the DK2 turn our turn 1 corresponds to, so
    /// a capture that did not start at the level's first turn can still be
    /// lined up. Neither is inferred from the data: both are the caller's claim
    /// about the capture, and getting them wrong shifts every command.
    /// </summary>
    public static InputLog Read(TextReader reader, string name, long lag = 1, long origin = 1)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        var pending = new SortedDictionary<long, SortedDictionary<byte, TurnMarks>>();
        int rows = 0;
        long firstTurn = long.MaxValue;
        long lastTurn = long.MinValue;

        string? line = reader.ReadLine();
        if (line != Header)
            throw new InvalidDataException(
                $"'{name}' does not have the task-pool probe's header.");

        for (int number = 2; (line = reader.ReadLine()) is not null; number++)
        {
            if (line.Length == 0) continue;
            string[] cells = line.Split(',');
            if (cells.Length != 7)
                throw new InvalidDataException(
                    $"'{name}' line {number} has {cells.Length} columns; expected 7.");

            long stamp = Long(cells[1], name, number);
            byte player = (byte)Long(cells[2], name, number);
            bool tagged = cells[3] switch
            {
                "tag" => true,
                "untag" => false,
                _ => throw new InvalidDataException(
                    $"'{name}' line {number} has event '{cells[3]}'; expected tag or untag."),
            };
            var tile = new TilePos(
                (int)Long(cells[5], name, number), (int)Long(cells[6], name, number));

            rows++;
            if (stamp < firstTurn) firstTurn = stamp;
            if (stamp > lastTurn) lastTurn = stamp;

            long turn = stamp - lag - origin + 1;
            if (!pending.TryGetValue(turn, out SortedDictionary<byte, TurnMarks>? byPlayer))
                pending[turn] = byPlayer = [];
            if (!byPlayer.TryGetValue(player, out TurnMarks marks))
                byPlayer[player] = marks = new TurnMarks();
            (tagged ? marks.Tagged : marks.Untagged).Add(tile);
        }

        return Build(pending, rows, firstTurn, lastTurn);
    }

    private static InputLog Build(
        SortedDictionary<long, SortedDictionary<byte, TurnMarks>> pending,
        int rows, long firstTurn, long lastTurn)
    {
        var frames = new Dictionary<long, InputFrame>();
        var turns = new List<long>();
        int cancelled = 0;
        int before = 0;
        int commands = 0;

        foreach ((long turn, SortedDictionary<byte, TurnMarks> byPlayer) in pending)
        {
            var issued = new List<PlayerCommand>();
            foreach ((byte player, TurnMarks marks) in byPlayer)
            {
                // A tile that is tagged and untagged in one sample moved between
                // the pool's two tag lists, which are split by whether the terrain
                // holds gold. The player did nothing: an untag followed by a retag
                // inside one sample nets to no change and emits no rows at all.
                cancelled += marks.Cancel();
                if (marks.Untagged.Count > 0)
                    issued.Add(new TagTilesCommand(player, Sorted(marks.Untagged), false));
                if (marks.Tagged.Count > 0)
                    issued.Add(new TagTilesCommand(player, Sorted(marks.Tagged), true));
            }

            if (issued.Count == 0) continue;
            if (turn < 1)
            {
                before += issued.Count;
                continue;
            }

            commands += issued.Count;
            frames[turn] = new InputFrame(issued);
            turns.Add(turn);
        }

        return new InputLog(frames, turns, new InputLogTally(
            rows, commands, cancelled, before,
            rows == 0 ? 0 : firstTurn, rows == 0 ? 0 : lastTurn));
    }

    /// <summary>
    /// Row-major, so a replay does not depend on the probe's emission order.
    /// The order within a frame is ours and DK2's is UNKNOWN, but a tag is
    /// per-tile and independent, so no rule here can depend on it.
    /// </summary>
    private static TilePos[] Sorted(HashSet<TilePos> tiles) =>
        [.. tiles.OrderBy(t => t.Y).ThenBy(t => t.X)];

    private static long Long(string cell, string name, int number) =>
        long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new InvalidDataException(
                $"'{name}' line {number} has '{cell}' where a number was expected.");

    private sealed class TurnMarks
    {
        public HashSet<TilePos> Tagged { get; } = [];

        public HashSet<TilePos> Untagged { get; } = [];

        public int Cancel()
        {
            if (Tagged.Count == 0 || Untagged.Count == 0) return 0;
            TilePos[] both = [.. Tagged.Where(Untagged.Contains)];
            foreach (TilePos tile in both)
            {
                Tagged.Remove(tile);
                Untagged.Remove(tile);
            }
            return both.Length;
        }
    }
}

/// <summary>
/// What a log contained, so a replay can report on its own input rather than
/// only on its output. <paramref name="Before"/> counts commands dated before
/// our turn 1 — the probe attaching mid-level, or the wrong origin.
/// </summary>
public readonly record struct InputLogTally(
    int Rows, int Commands, int Cancelled, int Before, long FirstStamp, long LastStamp);
