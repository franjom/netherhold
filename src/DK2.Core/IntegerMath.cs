// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>Integer arithmetic the original performs its own way, reproduced rather than replaced
/// so that every consumer gets the same rounding.</summary>
public static class IntegerMath
{
    /// <summary>Seeds for <see cref="SquareRoot"/>, indexed by the position of the input's highest
    /// set bit; each is round(2^(i/2)), so the first guess is already the right size. The
    /// original's 32-entry table at <c>0x0065FB14</c>.</summary>
    private static readonly int[] SquareRootSeed =
    [
        1, 2, 2, 4, 5, 8, 11, 16, 22, 32, 45, 64, 90, 128, 181, 256,
        362, 512, 724, 1024, 1448, 2048, 2896, 4096, 5792, 8192, 11585, 16384,
        23170, 32768, 46340, 65535,
    ];

    /// <summary>The original's integer square root (<c>0x0065FAE0</c>): seed by the highest set
    /// bit, then halve the guess toward the quotient until the quotient stops being smaller. It
    /// returns its first converged guess, which is at most the true root and is what both the
    /// separation curve and the funnel's path length are fed.</summary>
    public static int SquareRoot(int value)
    {
        if (value <= 0) return 0;
        return SquareRoot((uint)value);
    }

    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-tremor.md")]
    public static int SquareRoot(uint value)
    {
        if (value == 0) return 0;

        int highest = 0;
        for (uint v = value; v > 1; v >>= 1) highest++;

        int guess = SquareRootSeed[highest];
        while (true)
        {
            int quotient = (int)(value / (uint)guess);
            if (quotient >= guess) return guess;
            guess = (guess + quotient) >> 1;
        }
    }

    /// <summary>Euclidean length of an integer vector through <see cref="SquareRoot"/>, in 64-bit
    /// for the square so 1/256-tile coordinates cannot overflow it; the root itself is capped at
    /// <see cref="int.MaxValue"/> inputs, which a map cannot reach.</summary>
    public static int Length(long dx, long dy)
    {
        long sq = (dx * dx) + (dy * dy);
        return sq > int.MaxValue ? SquareRoot(int.MaxValue) : SquareRoot((int)sq);
    }
}
