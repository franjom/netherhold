// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>Integer arithmetic the original performs its own way, reproduced rather than replaced
/// so that every consumer gets the same rounding.</summary>
public static class IntegerMath
{
    /// <summary>Seeds for <see cref="SquareRoot"/>, indexed by the position of the input's highest
    /// set bit; each is floor(2^((i+1)/2)), the last capped at 65535, so the first guess is never
    /// below the integer root. The original's 32-entry table of 16-bit words at <c>0x0065FB14</c>.</summary>
    private static readonly int[] SquareRootSeed =
    [
        1, 2, 2, 4, 5, 8, 11, 16, 22, 32, 45, 64, 90, 128, 181, 256,
        362, 512, 724, 1024, 1448, 2048, 2896, 4096, 5792, 8192, 11585, 16384,
        23170, 32768, 46340, 65535,
    ];

    /// <summary>The seed table, exposed so a test can hold it to the original's bytes.</summary>
    public static int SquareRootSeedEntry(int i) => SquareRootSeed[i];

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

    /// <summary>The original's integer length of a 3-vector (<c>0x00555990</c>): absolute components,
    /// unshifted while all are below 2^11, shifted 8 while below 2^21, otherwise X and Y shifted 18 and
    /// Z only 8; the root of the 32-bit sum of squares, read unsigned, is shifted back by the band's X
    /// shift. Held to the original value for value by <c>OriginalGeometryTests</c>.</summary>
    public static int Length(int x, int y, int z)
    {
        uint ax = Magnitude(x), ay = Magnitude(y), az = Magnitude(z);
        int shift = (ax | ay | az) < 2048 ? 0 : (ax | ay | az) < 2097152 ? 8 : 18;
        ax >>= shift; ay >>= shift; az >>= shift == 18 ? 8 : shift;
        return SquareRoot(unchecked(ax * ax + ay * ay + az * az)) << shift;
    }

    /// <summary>|v| as the original's CDQ/XOR/SUB forms it: int.MinValue stays 2^31, read unsigned.</summary>
    private static uint Magnitude(int v) => unchecked((uint)(v < 0 ? -v : v));
}
