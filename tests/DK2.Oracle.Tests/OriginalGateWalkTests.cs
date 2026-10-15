// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;
using Xunit.Abstractions;
using static DK2.Oracle.Tests.OriginalCpu;

namespace DK2.Oracle.Tests;

/// <summary>
/// The gate walk's two record edits against the original's, on the emulator. Both are cdecl
/// (record*, cap) and edit the record in place: seven dwords, A then B then the crossing, then
/// the selector at +0x18, read back whole after the call. Points are in 1/256 tiles over a map
/// of up to 256 tiles, caps from a fraction of a tile to 32 tiles; a divide fault in the
/// original is outside its domain (DK2 would crash), so it is counted, not compared.
/// </summary>
public class OriginalGateWalkTests(ITestOutputHelper log)
{
    private const uint Record = Scratch;

    /// <summary><c>0x004CFC50</c>, <see cref="GateWalk.Collapse"/>.</summary>
    [OracleFact]
    public void Collapse_MatchesTheOriginal() =>
        Compare("collapse", 0x004CFC50, (ref GateRecord r, int cap) => GateWalk.Collapse(ref r, cap),
            (r, cap, after) => GateWalk.Octile(r.A, r.B) < cap ? "short" : r.Selector == 1 ? "keep B" : "keep A",
            ["short", "keep A", "keep B"]);

    /// <summary><c>0x004CFDA0</c>, <see cref="GateWalk.AdjustAroundCrossing"/>.</summary>
    [OracleFact]
    public void AdjustAroundCrossing_MatchesTheOriginal() =>
        Compare("adjust", 0x004CFDA0, (ref GateRecord r, int cap) => GateWalk.AdjustAroundCrossing(ref r, cap),
            (r, cap, after) =>
            {
                int dA = GateWalk.Octile(r.A, r.Crossing), dB = GateWalk.Octile(r.B, r.Crossing);
                if (dA < cap >> 1 && dB < cap >> 1) return "both near";
                if (dA < (cap + 2) >> 1) return "A near";
                if (dB < (cap + 2) >> 1) return "B near";
                bool movedA = after[0] != r.A.X || after[1] != r.A.Y, movedB = after[2] != r.B.X || after[3] != r.B.Y;
                return movedA ? movedB ? "move both" : "move A only" : movedB ? "move B only" : "move neither";
            },
            ["both near", "A near", "B near", "move both", "move A only", "move B only", "move neither"]);

    private delegate void Edit(ref GateRecord record, int cap);

    /// <summary>Compares, and counts the arm each gate reaches, judged from the input and the
    /// original's result; every arm must be reached, or the sweep proves nothing about it.</summary>
    private void Compare(string name, uint function, Edit ours, Func<GateRecord, int, int[], string> arm, string[] arms)
    {
        (GateRecord Record, int Cap)[] inputs = [.. Cases()];
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(x => new Request(function, [Record, (uint)x.Cap],
            [.. Words(x.Record).Select((w, i) => (Record + 4 * (uint)i, (uint)w))],
            [.. Enumerable.Range(0, 7).Select(i => Record + 4 * (uint)i)]))]);

        var mismatches = new List<string>();
        var reached = arms.ToDictionary(a => a, _ => 0);
        int faults = 0;
        for (int i = 0; i < inputs.Length; i++)
        {
            string at = $"{name}({Show(Words(inputs[i].Record))}, cap {inputs[i].Cap})";
            if (original[i].Fault is { } fault)
            {
                if (fault.StartsWith("divide", StringComparison.Ordinal)) faults++;
                else mismatches.Add($"{at}: original fault ({fault})");
                continue;
            }

            int[] after = [.. original[i].Reads.Select(w => (int)w)];
            reached[arm(inputs[i].Record, inputs[i].Cap, after)]++;
            string theirs = Show(after);
            string mine;
            try
            {
                GateRecord r = inputs[i].Record;
                ours(ref r, inputs[i].Cap);
                mine = Show(Words(r));
            }
            catch (DivideByZeroException) { mine = "divide fault"; }
            if (theirs != mine) mismatches.Add($"{at}: original {theirs}, ours {mine}");
        }
        log.WriteLine($"{name}: {inputs.Length} inputs, {faults} outside the original's domain (divide fault); arms: " +
                      string.Join(", ", reached.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {inputs.Length} differ; first:\n" + string.Join("\n", mismatches.Take(15)));
        Assert.True(reached.All(kv => kv.Value > 0), "an arm no swept gate reaches: " +
            string.Join(", ", reached.Where(kv => kv.Value == 0).Select(kv => kv.Key)));
    }

    private static int[] Words(GateRecord r) =>
        [r.A.X, r.A.Y, r.B.X, r.B.Y, r.Crossing.X, r.Crossing.Y, r.Selector];

    private static string Show(int[] words) => "[" + string.Join(", ", words) + "]";

    /// <summary>Gates of every length from a point to the map's diagonal, both selectors, the
    /// crossing on the gate, at an endpoint or off it, and caps on and between tile multiples.</summary>
    private static IEnumerable<(GateRecord, int)> Cases()
    {
        const int Map = 256 * NavMesh.UnitsPerTile;
        var spread = new Random(20261015);
        int[] caps = [0, 1, 255, 256, 257, 511, 512, 768, 1024, 1536, 2048, 4096, 8192];
        for (int i = 0; i < 12000; i++)
        {
            int reach = 1 << spread.Next(0, 17);
            var a = new NavPoint(spread.Next(0, Map), spread.Next(0, Map));
            var b = new NavPoint(Math.Clamp(a.X + spread.Next(-reach, reach + 1), 0, Map),
                Math.Clamp(a.Y + spread.Next(-reach, reach + 1), 0, Map));
            int t = spread.Next(-2, 0x4003);
            NavPoint crossing = spread.Next(4) switch
            {
                0 => a,
                1 => b,
                2 => new NavPoint(spread.Next(0, Map), spread.Next(0, Map)),
                _ => new NavPoint(a.X + (int)((long)(b.X - a.X) * t >> 14), a.Y + (int)((long)(b.Y - a.Y) * t >> 14)),
            };
            int cap = spread.Next(3) == 0 ? spread.Next(0, 8193) : caps[spread.Next(caps.Length)];
            var record = new GateRecord(a, b, spread.Next(0, 3)) { Crossing = crossing };
            yield return (record, cap);
        }
    }
}
