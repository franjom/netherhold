// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Steering;
using static DK2.Oracle.Tests.OriginalCpu;

namespace DK2.Oracle.Tests;

/// <summary>
/// Our C# against the original function itself, run on the emulator, value by value. Each
/// test sweeps the inputs where a port goes wrong: every small value, every power of two and
/// its neighbours, the signed and unsigned extremes, and a fixed pseudo-random spread.
/// </summary>
public class OriginalFunctionTests
{
    /// <summary>Math_Isqrt, <c>0x0065FAE0</c>: cdecl, one unsigned argument, root in EAX.</summary>
    [OracleFact]
    public void SquareRoot_MatchesTheOriginal()
    {
        uint[] inputs = [.. UnsignedSweep()];
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(v => new Request(0x0065FAE0, [v]))]);

        var mismatches = new List<string>();
        for (int i = 0; i < inputs.Length; i++)
        {
            int ours = IntegerMath.SquareRoot(inputs[i]);
            if (original[i].Fault is not null || original[i].Eax != (uint)ours)
                mismatches.Add($"isqrt({inputs[i]}): original {Describe(original[i])}, ours {ours}");
        }
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    /// <summary>Math_Atan2, <c>0x0065FB54</c>: cdecl (dx, dy); only AX is the answer, masked to
    /// 0x7FF, while EAX's upper half keeps what the quotient left there.</summary>
    [OracleFact]
    public void Bearing_MatchesTheOriginal()
    {
        (int Dx, int Dy)[] inputs = [.. PairSweep()];
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(p => new Request(0x0065FB54, [(uint)p.Dx, (uint)p.Dy]))]);

        var mismatches = new List<string>();
        for (int i = 0; i < inputs.Length; i++)
        {
            // Both sides divide by zero, and nowhere else, when one difference is int.MinValue
            // and the other 0: the negation leaves int.MinValue negative.
            string ours;
            try { ours = Bearing.Of(inputs[i].Dx, inputs[i].Dy).ToString(); }
            catch (DivideByZeroException) { ours = "divide fault"; }
            string theirs = original[i].Fault switch
            {
                null => (original[i].Eax & 0xFFFF).ToString(),
                string f when f.StartsWith("divide", StringComparison.Ordinal) => "divide fault",
                string f => $"fault ({f})",
            };
            if (theirs != ours)
                mismatches.Add($"atan2({inputs[i].Dx}, {inputs[i].Dy}): original {theirs}, ours {ours}");
        }
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    /// <summary>Random_Next, <c>0x005558D0</c>: cdecl (range, seed*, file, line). The draw is
    /// EAX and the advanced seed is written back through the pointer. A zero range takes the
    /// logging arm, which needs the engine's log, so it is not called here.</summary>
    [OracleFact]
    public void RandomDraw_MatchesTheOriginal()
    {
        uint[] ranges = [1, 2, 3, 4, 7, 10, 100, 1000, 65536, 0x7FFFFFFF];
        var spread = new Random(20261015);
        var seeds = new List<uint> { 0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF };
        for (int i = 0; i < 1000; i++) seeds.Add((uint)spread.NextInt64(0, 1L << 32));
        (uint Range, uint Seed)[] inputs = [.. ranges.SelectMany(r => seeds.Select(s => (r, s)))];

        IReadOnlyList<Outcome> original = Run([.. inputs.Select(x =>
            new Request(0x005558D0, [x.Range, Scratch, 0, 0], [(Scratch, x.Seed)], [Scratch]))]);

        var mismatches = new List<string>();
        for (int i = 0; i < inputs.Length; i++)
        {
            var ours = new Dk2Lcg(inputs[i].Seed);
            int draw = ours.NextInt((int)inputs[i].Range);
            Outcome o = original[i];
            if (o.Fault is not null || o.Eax != (uint)draw || o.Reads[0] != ours.Seed)
                mismatches.Add($"random({inputs[i].Range}) from seed 0x{inputs[i].Seed:X8}: original {Describe(o)} " +
                               $"seed 0x{(o.Reads.Length > 0 ? o.Reads[0] : 0):X8}, ours {draw} seed 0x{ours.Seed:X8}");
        }
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    private static IEnumerable<uint> UnsignedSweep()
    {
        for (uint v = 0; v <= 20000; v++) yield return v;
        for (int bit = 0; bit < 32; bit++)
        {
            uint p = 1u << bit;
            yield return p - 1;
            yield return p;
            yield return p + 1;
        }
        yield return uint.MaxValue;
        yield return int.MaxValue;
        var spread = new Random(20261015);
        for (int i = 0; i < 10000; i++) yield return (uint)spread.NextInt64(0, 1L << 32);
    }

    private static IEnumerable<(int, int)> PairSweep()
    {
        for (int dx = -100; dx <= 100; dx++)
        for (int dy = -100; dy <= 100; dy++)
            yield return (dx, dy);

        int[] edges = [int.MinValue, int.MinValue + 1, -(1 << 24) - 1, -(1 << 24), -(1 << 23), -65536, -1,
            0, 1, 65536, 1 << 23, (1 << 24) - 1, 1 << 24, int.MaxValue];
        foreach (int dx in edges)
        foreach (int dy in edges)
            yield return (dx, dy);

        var spread = new Random(20261015);
        for (int i = 0; i < 10000; i++)
        {
            int scale = 1 << spread.Next(0, 31);
            yield return (spread.Next(-scale, scale), spread.Next(-scale, scale));
        }
    }

    private static string Describe(Outcome outcome) =>
        outcome.Fault is { } fault ? $"fault ({fault})" : $"{outcome.Eax}";

    private static string Report(List<string> mismatches) =>
        $"{mismatches.Count} mismatches; first:\n" + string.Join("\n", mismatches.Take(15));
}
