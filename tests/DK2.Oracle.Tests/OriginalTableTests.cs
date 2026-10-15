// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;
using DK2.Simulation.Steering;
using DK2.TestSupport;

namespace DK2.Oracle.Tests;

/// <summary>
/// Our lookup tables against the bytes of the original's own, entry by entry. Each C# table was
/// typed in or rebuilt from a formula that fits it; a formula that fits the entries someone
/// checked can still miss one (a table may be a formula, or only nearly one), and only the
/// binary can say which.
/// </summary>
public class OriginalTableTests
{
    /// <summary>Math_Isqrt's seeds: 32 unsigned 16-bit words read by <c>MOVZX</c> at
    /// <c>0x0065FAF1</c>.</summary>
    [InstallFact]
    public void SquareRootSeeds_MatchTheOriginalsTable()
    {
        OriginalExecutable exe = OriginalExecutable.Load();
        for (int i = 0; i < 32; i++)
            Assert.True(exe.UInt16(0x0065FB14u + (uint)(2 * i)) == IntegerMath.SquareRootSeedEntry(i),
                $"seed {i}: original {exe.UInt16(0x0065FB14u + (uint)(2 * i))}, ours {IntegerMath.SquareRootSeedEntry(i)}");
    }

    /// <summary>Math_Atan2's 257 unsigned 16-bit words at <c>0x0065FC94</c>.</summary>
    [InstallFact]
    public void ArctangentTable_MatchesTheOriginalsTable()
    {
        OriginalExecutable exe = OriginalExecutable.Load();
        for (int i = 0; i <= 256; i++)
            Assert.True(exe.UInt16(0x0065FC94u + (uint)(2 * i)) == Bearing.ArctangentEntry(i),
                $"atan {i}: original {exe.UInt16(0x0065FC94u + (uint)(2 * i))}, ours {Bearing.ArctangentEntry(i)}");
    }

    /// <summary>The 2560-dword sine table at <c>0x006C1488</c>, read through the two accessors
    /// that index it: <see cref="TrapRules.Sine"/> covers entries 0-2047 and
    /// <see cref="TrapRules.Cosine"/>, a quarter turn on, the last 512.</summary>
    [InstallFact]
    public void SineTable_MatchesTheOriginalsTable()
    {
        OriginalExecutable exe = OriginalExecutable.Load();
        for (int i = 0; i < 2560; i++)
        {
            int ours = i < Bearing.Turn ? TrapRules.Sine(i) : TrapRules.Cosine(i - 512);
            int original = exe.Int32(0x006C1488u + (uint)(4 * i));
            Assert.True(original == ours, $"sine {i}: original {original}, ours {ours}");
        }
    }
}
