// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;
using Xunit.Abstractions;
using static DK2.Oracle.Tests.OriginalCpu;

namespace DK2.Oracle.Tests;

/// <summary>
/// The steering layer's 16.16 vector helpers against the original's, on the emulator. Every
/// helper writes its vector through an out pointer; the input vector sits at
/// <see cref="Scratch"/> and the result at <see cref="Scratch"/> + 0x10.
///
/// <para>Where the original's 64/32 <c>IDIV</c> faults (a zero divisor, or a quotient past 32
/// bits) DK2 itself would crash, so there is nothing to match: those inputs are counted and
/// logged, not compared. Our C# wraps there instead (<see cref="Fixed16.Div"/>).</para>
/// </summary>
public class OriginalFixed16Tests(ITestOutputHelper log)
{
    private const uint In = Scratch, Out = Scratch + 0x10;

    /// <summary><c>0x004D1F10</c>: cdecl (out*, in*).</summary>
    [OracleFact]
    public void Normalise_MatchesTheOriginal() =>
        Compare("normalise", [.. Vectors().Select(v => (v, 0))],
            x => new Request(0x004D1F10, [Out, In], Input(x.V), [Out, Out + 4]),
            x => Fixed16.Normalise(x.V));

    /// <summary><c>0x004D4F60</c>: cdecl (out*, in*, max).</summary>
    [OracleFact]
    public void Truncate_MatchesTheOriginal() =>
        Compare("truncate", Pairs(),
            x => new Request(0x004D4F60, [Out, In, (uint)x.K], Input(x.V), [Out, Out + 4]),
            x => Fixed16.Truncate(x.V, x.K));

    /// <summary><c>0x004D1E70</c>: thiscall, ECX the vector, then (out*, k).</summary>
    [OracleFact]
    public void Scale_MatchesTheOriginal() =>
        Compare("scale", Pairs(),
            x => new Request(0x004D1E70, [Out, (uint)x.K], Input(x.V), [Out, Out + 4], Ecx: In),
            x => x.V.Scale(x.K));

    /// <summary><c>0x004D1EC0</c>: thiscall, ECX the vector, then (out*, k).</summary>
    [OracleFact]
    public void Over_MatchesTheOriginal() =>
        Compare("over", Pairs(),
            x => new Request(0x004D1EC0, [Out, (uint)x.K], Input(x.V), [Out, Out + 4], Ecx: In),
            x => x.V.Over(x.K));

    private void Compare(string name, (Fixed16Vector V, int K)[] inputs,
        Func<(Fixed16Vector V, int K), Request> request, Func<(Fixed16Vector V, int K), Fixed16Vector> ours)
    {
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(request)]);
        var mismatches = new List<string>();
        int faults = 0;
        for (int i = 0; i < inputs.Length; i++)
        {
            string at = $"{name}(({inputs[i].V.X}, {inputs[i].V.Y}), {inputs[i].K})";
            if (original[i].Fault is { } fault)
            {
                if (fault.StartsWith("divide", StringComparison.Ordinal)) faults++;
                else mismatches.Add($"{at}: original fault ({fault})");
                continue;
            }

            string theirs = $"({(int)original[i].Reads[0]}, {(int)original[i].Reads[1]})";
            string mine;
            try { Fixed16Vector v = ours(inputs[i]); mine = $"({v.X}, {v.Y})"; }
            catch (DivideByZeroException) { mine = "divide fault"; }
            if (theirs != mine) mismatches.Add($"{at}: original {theirs}, ours {mine}");
        }
        log.WriteLine($"{name}: {inputs.Length} inputs, {faults} outside the original's domain (divide fault).");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {inputs.Length} differ; first:\n" + string.Join("\n", mismatches.Take(15)));
    }

    private static (uint, uint)[] Input(Fixed16Vector v) => [(In, (uint)v.X), (In + 4, (uint)v.Y)];

    /// <summary>Components at every band edge of the three-band length, the unit, the signed
    /// extremes, and a spread of magnitudes from 1 to 2^31.</summary>
    private static readonly int[] Edges =
    [
        0, 1, -1, 0xFFF, -0xFFF, 0x1000, -0x1000, 0x10000, -0x10000, 0x3FFFFF, -0x3FFFFF, 0x400000, -0x400000,
        1 << 24, -(1 << 24), 0x7FFFFFFF, -0x7FFFFFFF, int.MinValue,
    ];

    private static IEnumerable<Fixed16Vector> Vectors()
    {
        foreach (int x in Edges)
        foreach (int y in Edges)
            yield return new Fixed16Vector(x, y);
        var spread = new Random(20261015);
        for (int i = 0; i < 5000; i++)
        {
            int scale = 1 << spread.Next(0, 31);
            yield return new Fixed16Vector(spread.Next(-scale, scale), spread.Next(-scale, scale));
        }
    }

    private static (Fixed16Vector, int)[] Pairs()
    {
        var spread = new Random(20261016);
        int[] scalars = [.. Edges, 0x8000, 0x18000, 0x7590];
        return [.. Vectors().SelectMany(v => scalars.Take(4).Append(scalars[spread.Next(scalars.Length)])
            .Append(spread.Next(-(1 << spread.Next(0, 31)), 1 << spread.Next(0, 31))).Select(k => (v, k)))];
    }
}
