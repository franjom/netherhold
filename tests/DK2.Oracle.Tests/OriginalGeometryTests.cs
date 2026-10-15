// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;
using DK2.Simulation.Steering;
using Xunit.Abstractions;
using static DK2.Oracle.Tests.OriginalCpu;

namespace DK2.Oracle.Tests;

/// <summary>
/// Angle and distance helpers against the original's, on the emulator: the two heading helpers,
/// the possessed aim cone, the steering ray–circle test, the 3-vector length, the octile and
/// perpendicular vector helpers and the dig handlers' start side. Each sweep
/// counts the arm every input reaches; an arm no input reaches fails the test.
/// </summary>
public class OriginalGeometryTests(ITestOutputHelper log)
{
    private const uint A = Scratch, B = Scratch + 0x10, C = Scratch + 0x20, Out = Scratch + 0x30;

    /// <summary><c>0x005119F0</c>: thiscall, ECX the heading's word, then (out word*, target word*).</summary>
    [OracleFact]
    public void AngleDistance_MatchesTheOriginal()
    {
        (int H, int T)[] inputs = [.. HeadingPairs()];
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(x => new Request(0x005119F0, [Out, B],
            [(A, (uint)x.H), (B, (uint)x.T), (Out, 0)], [Out], Ecx: A))]);
        Check("distance", inputs.Length, i =>
        {
            int d = Math.Abs(inputs[i].H - inputs[i].T);
            string arm = d == 0 ? "equal" : d > Bearing.HalfTurn ? "long way" : "short way";
            return (arm, (original[i].Reads[0] & 0xFFFF).ToString(), Bearing.Distance(inputs[i].H, inputs[i].T).ToString());
        }, original, i => $"distance({inputs[i].H}, {inputs[i].T})", ["equal", "short way", "long way"]);
    }

    /// <summary><c>0x00511A30</c>: cdecl (heading word*, target word*), the answer in AL.</summary>
    [OracleFact]
    public void AngleTurnsUp_MatchesTheOriginal()
    {
        (int H, int T)[] inputs = [.. HeadingPairs()];
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(x => new Request(0x00511A30, [A, B],
            [(A, (uint)x.H), (B, (uint)x.T)]))]);
        Check("turns up", inputs.Length, i =>
        {
            int d = inputs[i].T - inputs[i].H;
            string arm = d == 0 ? "equal" : Math.Abs(d) > Bearing.HalfTurn ? "beyond half" : "within half";
            return (arm, ((original[i].Eax & 0xFF) != 0).ToString(), Bearing.TurnsUp(inputs[i].H, inputs[i].T).ToString());
        }, original, i => $"turnsUp({inputs[i].H}, {inputs[i].T})", ["equal", "within half", "beyond half"]);
    }

    /// <summary><c>0x00516950</c>: stdcall (angle word*, pose*, point*); only the yaw is read.</summary>
    [OracleFact]
    public void AimCone_MatchesTheOriginal()
    {
        var spread = new Random(20261017);
        var inputs = new List<(int Yaw, PossessionRules.Vec3 Pose, PossessionRules.Vec3 Point)>();
        for (int i = 0; i < 20000; i++)
        {
            int yaw = spread.Next(4) == 0 ? spread.Next(0, 0x10000) : spread.Next(0, 0x800);
            var pose = new PossessionRules.Vec3(spread.Next(0, 0x40000), spread.Next(0, 0x40000), 0);
            int reach = 1 << spread.Next(0, 18);
            var point = new PossessionRules.Vec3(pose.X + spread.Next(-reach, reach + 1), pose.Y + spread.Next(-reach, reach + 1), 0);
            inputs.Add((yaw, pose, point));
            // The cone's edges themselves: a point dead on yaw ± 0x155, from a long way off.
            int edge = (yaw & 0x7FF) + (spread.Next(2) == 0 ? 0x155 : -0x155) + spread.Next(-2, 3);
            double turn = edge * Math.PI / 1024;
            inputs.Add((yaw & 0x7FF, pose, new PossessionRules.Vec3(pose.X + (int)(Math.Sin(turn) * 0x10000),
                pose.Y - (int)(Math.Cos(turn) * 0x10000), 0)));
        }

        IReadOnlyList<Outcome> original = Run([.. inputs.Select(x => new Request(0x00516950, [A, B, C],
            [(A, (uint)x.Yaw), (B, (uint)x.Pose.X), (B + 4, (uint)x.Pose.Y), (C, (uint)x.Point.X), (C + 4, (uint)x.Point.Y)]))]);
        Check("aim cone", inputs.Count, i =>
        {
            (int yaw, PossessionRules.Vec3 pose, PossessionRules.Vec3 point) = inputs[i];
            bool theirs = original[i].Eax != 0;
            bool ours = PossessionRules.InAimCone(yaw, pose, point);
            // The original's two unwrap steps in its order: a turn added, then one taken away.
            int bearing = Bearing.Of(point.X - pose.X, point.Y - pose.Y) & Bearing.Mask;
            string unwrap = "as read";
            if (yaw - bearing > Bearing.HalfTurn) { bearing += Bearing.Turn; unwrap = "turn added"; }
            if (bearing - yaw > Bearing.HalfTurn) unwrap = unwrap == "as read" ? "turn taken" : "added, then taken";
            return ($"{(theirs ? "inside" : "outside")}, {unwrap}", theirs.ToString(), ours.ToString());
        }, original, i => $"aimCone({inputs[i].Yaw}, {inputs[i].Pose}, {inputs[i].Point})",
            ["inside, as read", "inside, turn added", "inside, turn taken", "outside, as read", "outside, turn added", "outside, turn taken"]);
    }

    /// <summary><c>0x004D20F0</c>, <see cref="RayCircle.Distance"/>: cdecl (origin*, dir*, centre*, radius).</summary>
    [OracleFact]
    public void RayCircle_MatchesTheOriginal()
    {
        var spread = new Random(20261018);
        var inputs = new List<(Fixed16Vector O, Fixed16Vector D, Fixed16Vector C, int R)>();
        for (int i = 0; i < 20000; i++)
        {
            // Tile units in 16.16 over a 128-tile map, a unit-ish or scaled direction, radii to 4 tiles.
            int map = 128 << 16, reach = 1 << spread.Next(10, 21);
            var o = new Fixed16Vector(spread.Next(0, map), spread.Next(0, map));
            var c = new Fixed16Vector(o.X + spread.Next(-reach, reach + 1), o.Y + spread.Next(-reach, reach + 1));
            int scale = 1 << spread.Next(8, 20);
            var d = new Fixed16Vector(spread.Next(-scale, scale + 1), spread.Next(-scale, scale + 1));
            int r = spread.Next(4) == 0 ? spread.Next(0, 4 << 16) : spread.Next(0, 1 << 16);
            inputs.Add((o, d, c, r));
        }

        IReadOnlyList<Outcome> original = Run([.. inputs.Select(x => new Request(0x004D20F0, [A, B, C, (uint)x.R],
            [(A, (uint)x.O.X), (A + 4, (uint)x.O.Y), (B, (uint)x.D.X), (B + 4, (uint)x.D.Y), (C, (uint)x.C.X), (C + 4, (uint)x.C.Y)]))]);
        Check("ray-circle", inputs.Count, i =>
        {
            (Fixed16Vector o, Fixed16Vector d, Fixed16Vector c, int r) = inputs[i];
            int theirs = (int)original[i].Eax, ours = RayCircle.Distance(o, d, c, r);
            string arm = theirs == RayCircle.Miss ? "miss" : theirs < 0 ? "hit behind" : "hit ahead";
            return (arm, theirs.ToString(), ours.ToString());
        }, original, i => $"rayCircle({inputs[i].O.X},{inputs[i].O.Y} / {inputs[i].D.X},{inputs[i].D.Y} / {inputs[i].C.X},{inputs[i].C.Y} / {inputs[i].R})",
            ["miss", "hit behind", "hit ahead"]);
    }

    /// <summary><c>0x00555990</c>, <see cref="IntegerMath.Length(int, int, int)"/>: thiscall, ECX the vector.</summary>
    [OracleFact]
    public void VectorLength_MatchesTheOriginal()
    {
        int[] edges = [0, 1, -1, 2047, -2047, 2048, -2048, 0x1FFFFF, -0x1FFFFF, 0x200000, -0x200000, 0x7FFFFFFF, -0x7FFFFFFF, int.MinValue];
        var inputs = new List<(int X, int Y, int Z)>();
        foreach (int x in edges)
        foreach (int y in edges)
        foreach (int z in edges)
            inputs.Add((x, y, z));
        var spread = new Random(20261019);
        for (int i = 0; i < 20000; i++)
        {
            int Component() { int s = 1 << spread.Next(0, 31); return spread.Next(-s, s); }
            inputs.Add((Component(), Component(), Component()));
        }

        IReadOnlyList<Outcome> original = Run([.. inputs.Select(v => new Request(0x00555990, [],
            [(A, (uint)v.X), (A + 4, (uint)v.Y), (A + 8, (uint)v.Z)], Ecx: A))]);
        Check("length", inputs.Count, i =>
        {
            (int x, int y, int z) = inputs[i];
            uint or = (uint)Math.Abs((long)x) | (uint)Math.Abs((long)y) | (uint)Math.Abs((long)z);
            string arm = or < 2048 ? "small" : or < 0x200000 ? "shift 8" : "shift 18";
            return (arm, ((int)original[i].Eax).ToString(), IntegerMath.Length(x, y, z).ToString());
        }, original, i => $"length({inputs[i].X}, {inputs[i].Y}, {inputs[i].Z})", ["small", "shift 8", "shift 18"]);
    }

    /// <summary><c>0x004D4570</c>, <see cref="Fixed16.Octile"/>: thiscall, ECX the vector.</summary>
    [OracleFact]
    public void Octile_MatchesTheOriginal()
    {
        (int X, int Y)[] inputs = [.. Pairs(20261021)];
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(v => new Request(0x004D4570, [], [(A, (uint)v.X), (A + 4, (uint)v.Y)], Ecx: A))]);
        Check("octile", inputs.Length, i =>
        {
            (int x, int y) = inputs[i];
            int ax = Fixed16.Abs(x), ay = Fixed16.Abs(y);
            string arm = ax < ay ? "x shorter" : ax > ay ? "y shorter" : "equal";
            return (arm, ((int)original[i].Eax).ToString(), Fixed16.Octile(x, y).ToString());
        }, original, i => $"octile({inputs[i].X}, {inputs[i].Y})", ["x shorter", "y shorter", "equal"]);
    }

    /// <summary><c>0x004D5060</c>, <see cref="Fixed16Vector.Perpendicular"/>: cdecl (out*, in*).</summary>
    [OracleFact]
    public void Perpendicular_MatchesTheOriginal()
    {
        (int X, int Y)[] inputs = [.. Pairs(20261022)];
        IReadOnlyList<Outcome> original = Run([.. inputs.Select(v => new Request(0x004D5060, [Out, A],
            [(A, (uint)v.X), (A + 4, (uint)v.Y)], [Out, Out + 4]))]);
        Check("perpendicular", inputs.Length, i =>
        {
            Fixed16Vector ours = new Fixed16Vector(inputs[i].X, inputs[i].Y).Perpendicular();
            return ("any", $"({(int)original[i].Reads[0]}, {(int)original[i].Reads[1]})", $"({ours.X}, {ours.Y})");
        }, original, i => $"perpendicular({inputs[i].X}, {inputs[i].Y})", ["any"]);
    }

    /// <summary><c>0x004805F0</c>, <see cref="MiningSlots.StartSide"/>: stdcall (dx, dy, step dword*,
    /// side word*). Arms are the original's thirteen exits, named by the signs of (dx, dy) and the
    /// answer.</summary>
    [OracleFact]
    public void StartSide_MatchesTheOriginal()
    {
        var inputs = new List<(int Dx, int Dy)>();
        for (int dx = -4; dx <= 4; dx++)
        for (int dy = -4; dy <= 4; dy++)
            inputs.Add((dx, dy));
        var spread = new Random(20261023);
        for (int i = 0; i < 5000; i++)
        {
            int dx = spread.Next(-300, 301);
            inputs.Add(spread.Next(4) == 0 ? (dx, spread.Next(2) == 0 ? dx : -dx) : (dx, spread.Next(-300, 301)));
        }

        IReadOnlyList<Outcome> original = Run([.. inputs.Select(v => new Request(0x004805F0, [(uint)v.Dx, (uint)v.Dy, Out, Out + 4],
            [(Out, 0x55555555), (Out + 4, 0x5555)], [Out, Out + 4]))]);
        static string Sign(int v) => v > 0 ? "+" : v < 0 ? "-" : "0";
        Check("start side", inputs.Count, i =>
        {
            (int dx, int dy) = inputs[i];
            int side = (int)(original[i].Reads[1] & 0xFFFF), step = (int)original[i].Reads[0];
            (int s, int t) = MiningSlots.StartSide(dx, dy);
            return ($"{Sign(dx)}{Sign(dy)} ({side},{step})", $"({side},{step})", $"({s},{t})");
        }, original, i => $"startSide({inputs[i].Dx}, {inputs[i].Dy})",
            ["00 (0,1)", "0+ (2,1)", "0- (0,1)", "+0 (1,1)", "++ (2,-1)", "++ (1,1)", "+- (0,1)", "+- (1,-1)",
             "-0 (3,1)", "-+ (2,1)", "-+ (3,-1)", "-- (0,-1)", "-- (3,1)"]);
    }

    /// <summary>Components at the sign and band edges, then a spread of magnitudes to 2^31.</summary>
    private static IEnumerable<(int, int)> Pairs(int seed)
    {
        int[] edges = [0, 1, -1, 2, -2, 0x10000, -0x10000, 0x7FFFFFFF, -0x7FFFFFFF, int.MinValue];
        foreach (int x in edges)
        foreach (int y in edges)
            yield return (x, y);
        var spread = new Random(seed);
        for (int i = 0; i < 10000; i++)
        {
            int x = spread.Next(-(1 << spread.Next(0, 31)), 1 << spread.Next(0, 31));
            yield return spread.Next(8) == 0 ? (x, spread.Next(2) == 0 ? x : -x) : (x, spread.Next(-(1 << spread.Next(0, 31)), 1 << spread.Next(0, 31)));
        }
    }

    /// <summary>Headings across the masked turn and a quarter beyond it into the word's range,
    /// with every 0..0x7FF heading paired against the turn's landmarks.</summary>
    private static IEnumerable<(int, int)> HeadingPairs()
    {
        int[] landmarks = [0, 1, 0x3FF, 0x400, 0x401, 0x7FF, 0x800, 0xBFF, 0xC00, 0xC01, 0xFFFF];
        for (int h = 0; h < 0x800; h++)
        foreach (int t in landmarks)
            yield return (h, t);
        var spread = new Random(20261020);
        for (int i = 0; i < 10000; i++)
            yield return spread.Next(4) == 0 ? (spread.Next(0, 0x10000), spread.Next(0, 0x10000)) : (spread.Next(0, 0x800), spread.Next(0, 0x800));
    }

    private void Check(string name, int count, Func<int, (string Arm, string Theirs, string Ours)> compare,
        IReadOnlyList<Outcome> original, Func<int, string> at, string[] arms)
    {
        var mismatches = new List<string>();
        var reached = arms.ToDictionary(a => a, _ => 0);
        for (int i = 0; i < count; i++)
        {
            if (original[i].Fault is { } fault)
            {
                mismatches.Add($"{at(i)}: original fault ({fault})");
                continue;
            }

            (string arm, string theirs, string ours) = compare(i);
            reached[arm] = reached.TryGetValue(arm, out int n) ? n + 1 : throw new InvalidOperationException($"unlisted arm {arm}");
            if (theirs != ours) mismatches.Add($"{at(i)}: original {theirs}, ours {ours}");
        }
        log.WriteLine($"{name}: {count} inputs; arms: " + string.Join(", ", reached.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {count} differ; first:\n" + string.Join("\n", mismatches.Take(15)));
        Assert.True(reached.All(kv => kv.Value > 0), "an arm no swept input reaches: " +
            string.Join(", ", reached.Where(kv => kv.Value == 0).Select(kv => kv.Key)));
    }
}
