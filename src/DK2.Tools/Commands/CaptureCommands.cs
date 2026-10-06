// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Tools.Commands;

internal static class CaptureCommands
{
    internal static int CaptureDiff(string[] arguments)
    {
        string? anchor = null;
        if (arguments.Length == 5)
        {
            if (arguments[3] != "--anchor")
            {
                Console.Error.WriteLine("usage: dk2tools capture-diff <ours.csv> <capture.csv> [--anchor tN]");
                return 2;
            }
            anchor = arguments[4];
        }

        try
        {
            TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
                arguments[1], arguments[2], anchor);

            static string Ids(IReadOnlyList<string> ids) =>
                ids.Count == 0 ? "(none)" : string.Join(", ", ids);
            Console.WriteLine("event set:");
            Console.WriteLine($"  in both ({result.InBoth.Count}): {Ids(result.InBoth)}");
            Console.WriteLine($"  capture only ({result.CaptureOnly.Count}): {Ids(result.CaptureOnly)}");
            Console.WriteLine($"  ours only ({result.OursOnly.Count}): {Ids(result.OursOnly)}");
            if (result.OursBeyondCapture.Count > 0)
                Console.WriteLine("  ours, due after the capture stopped -- untested, not a "
                    + $"divergence ({result.OursBeyondCapture.Count}): {Ids(result.OursBeyondCapture)}");
            if (result.CaptureBeyondOurs.Count > 0)
                Console.WriteLine("  capture, due after our run ended -- untested, not a "
                    + $"divergence ({result.CaptureBeyondOurs.Count}): {Ids(result.CaptureBeyondOurs)}");

            if (result.AnchorId is null)
            {
                Console.WriteLine($"stamp alignment unavailable: {result.AlignmentRefusal}.");
            }
            else
            {
                Console.WriteLine($"anchor {result.AnchorId}: ours stamp {result.OursOrigin}, "
                    + $"capture stamp {result.CaptureOrigin}");
                Console.WriteLine("stamp-aligned turns (capture residual = capture - ours):");
                Console.WriteLine("  id       ours  capture  residual");
                foreach (TriggerAlignedEvent e in result.AlignedEvents)
                {
                    string ours = e.OursTurnIsCeiling ? $"<={e.OursTurn}" : e.OursTurn.ToString();
                    string capture = e.CaptureTurnIsCeiling
                        ? $"<={e.CaptureTurn}"
                        : e.CaptureTurn.ToString();
                    string residualText = e.Residual.HasValue ? $"{e.Residual.Value:+#;-#;0}" : "unknown";
                    Console.WriteLine($"  {e.Id,-6} {ours,6} {capture,8} {residualText,9}");
                }

                if (result.AlignedEvents.Any(e => e.Residual is not null and not 0))
                    Console.WriteLine("  note: a capture turn is when the probe SAW the change, "
                        + "measured as one tick after\n        the game acted (run 8: timer 1 "
                        + "stored 1724, first seen at 1725). On a flag,\n        a +1 residual is "
                        + "agreement; the stamps below are the unsampled check.");
            }

            if (result.StampSeries.Count > 0)
            {
                Console.WriteLine("timer stamps, stored and origin-relative (no sampling in the path):");
                Console.WriteLine("  id       ours                 capture");
                foreach (TriggerStampSeries s in result.StampSeries)
                {
                    string verdict = s.Agrees ? "same" : "DIFFER";
                    if (s.Truncated) verdict += ", to the shorter run's window";
                    Console.WriteLine($"  {s.Id,-6} {string.Join(", ", s.Ours),-20} "
                        + $"{string.Join(", ", s.Capture),-20} {verdict}");
                }
            }

            string match = result.EventSetsMatch ? "matched" : "differed";
            string largest = result.LargestAbsoluteResidual is long residual
                ? $"{residual} tick{(residual == 1 ? string.Empty : "s")}"
                : "unavailable";
            string stamps = result.StampSeries.Count == 0
                ? "no timer stamped on both sides"
                : result.StampsAgree ? "stamps agree exactly" : "STAMPS DIFFER";
            Console.WriteLine($"verdict: event set {match}; {stamps}; "
                + $"largest absolute sampled residual {largest}");
            return result.EventSetsMatch && result.StampsAgree ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    internal static int DiffTriggerCsv(string leftPath, string rightPath)
    {
        string[][] ReadRows(string path)
        {
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0] != TriggerTurnCsvEmitter.Header)
                throw new InvalidDataException($"'{path}' does not have the live trigger-probe header.");
            return lines.Skip(1).Where(l => l.Length != 0).Select(l => l.Split(',')).ToArray();
        }

        try
        {
            string[] columns = TriggerTurnCsvEmitter.Header.Split(',');

            // Rows pair by TURN, never by position. A live capture starts at
            // whatever turn the probe attached on and may sample one turn twice, so
            // pairing by row silently compares different turns -- an answer that
            // looks like a divergence report and is noise.
            Dictionary<int, string[]> Index(string[][] rows, string path)
            {
                var byTurn = new Dictionary<int, string[]>();
                foreach (string[] row in rows)
                {
                    if (row.Length != columns.Length)
                        throw new InvalidDataException(
                            $"'{path}' has a row of {row.Length} columns; expected {columns.Length}.");
                    if (int.TryParse(row[1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out int t))
                        byTurn[t] = row;
                }
                return byTurn;
            }

            Dictionary<int, string[]> left = Index(ReadRows(leftPath), leftPath);
            Dictionary<int, string[]> right = Index(ReadRows(rightPath), rightPath);
            int[] turns = left.Keys.Intersect(right.Keys).OrderBy(t => t).ToArray();
            if (turns.Length == 0)
            {
                Console.Error.WriteLine("the two files share no turn number; nothing to compare.");
                return 1;
            }

            var compared = new int[columns.Length];
            var unknown = new int[columns.Length];
            var different = new int[columns.Length];
            (string Turn, string Column, string Left, string Right)? first = null;

            Console.WriteLine($"turns {turns[0]}..{turns[^1]} ({turns.Length} shared; "
                + $"{left.Count} left rows, {right.Count} right rows)");

            foreach (int turn in turns)
            {
                string[] a = left[turn];
                string[] b = right[turn];
                for (int col = 0; col < columns.Length; col++)
                {
                    if (a[col].Length == 0 || b[col].Length == 0)
                    {
                        unknown[col]++;
                        continue;
                    }

                    compared[col]++;
                    if (a[col] == b[col]) continue;
                    different[col]++;
                    first ??= (turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        columns[col], a[col], b[col]);
                }
            }

            if (first is { } f)
                Console.WriteLine($"first divergence: turn {f.Turn}, column {f.Column}: left='{f.Left}' right='{f.Right}'");
            else
                Console.WriteLine("no comparable values diverged");

            Console.WriteLine("per-column summary (different/compared, unknown):");
            for (int col = 0; col < columns.Length; col++)
                Console.WriteLine($"  {columns[col],-16} {different[col]}/{compared[col]}, {unknown[col]} unknown");
            return first is null ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
}
