// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>DK2's <c>Separation</c> steering force, the rule that stops two creatures standing in
/// the same place (Q345, docs/research/navigation-and-steering.md §3.2).
/// <para><b>Nothing calls this.</b> The force law is read; how DK2 turns a set of behaviour forces
/// into motion is not (Q343), and our mover has no velocity to accept one. Wiring it would mean
/// inventing the integration, so it is built, tested and left unwired — as ADR-0153 did for the
/// damage formula. ADR-0156.</para>
/// <para>Scalars are the steering layer's 16.16 fixed point, not <see cref="Fp32"/>'s 1/4096.
/// Arithmetic is reproduced operation for operation rather than reinterpreted: where the original
/// divides a 64-bit numerator by a 16.16 ratio, so does this.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-and-steering.md")]
public static class SeparationRules
{
    /// <summary>1.0 in the steering layer's 16.16 scalars.</summary>
    public const int One = 1 << 16;

    /// <summary>Below this ratio of distance to summed radii the force saturates at
    /// <c>max_force</c> instead of following the curve — <c>0x28F</c>, about 1 %.</summary>
    public const int SaturationRatio = 0x28F;

    /// <summary>The original's distance: the longer axis plus half the shorter, an integer
    /// approximation to the Euclidean length that never leaves fixed point.</summary>
    public static int OctileDistance(int dx, int dy)
    {
        int x = dx < 0 ? -dx : dx;
        int y = dy < 0 ? -dy : dy;
        int smaller = x < y ? x : y;
        return x + y - (smaller >> 1);
    }

    /// <summary>Seeds for <see cref="IntegerSquareRoot"/>, indexed by the position of the input's
    /// highest set bit. Each is round(2^(i/2)), so the first guess is already the right size.
    /// </summary>
    private static readonly int[] SquareRootSeed =
    [
        1, 2, 2, 4, 5, 8, 11, 16, 22, 32, 45, 64, 90, 128, 181, 256,
        362, 512, 724, 1024, 1448, 2048, 2896, 4096, 5792, 8192, 11585, 16384,
        23170, 32768, 46340, 65535,
    ];

    /// <summary>The original's integer square root: seed by the highest set bit, then halve the
    /// guess toward the quotient until the quotient stops being smaller. Reproduced rather than
    /// replaced by a library call, because it stops at the first converged guess and that is what
    /// the force curve is fed.</summary>
    public static int IntegerSquareRoot(int value)
    {
        if (value <= 0) return 0;

        int highest = 0;
        for (int v = value; v > 1; v >>= 1) highest++;

        int guess = SquareRootSeed[highest];
        while (true)
        {
            int quotient = value / guess;
            if (quotient >= guess) return guess;
            guess = (guess + quotient) >> 1;
        }
    }

    /// <summary>Whether two bodies push at all: only while they overlap, and never at zero
    /// distance, where there would be no direction to push along.</summary>
    public static bool Applies(int distance, int radiusSum) =>
        distance > 0 && distance <= radiusSum;

    /// <summary>How deeply they overlap, as distance over summed radii in 16.16. 0 is concentric
    /// and <see cref="One"/> is just touching.</summary>
    public static int Ratio(int distance, int radiusSum) =>
        (int)(((long)distance << 16) / radiusSum);

    /// <summary>The force's size. Three arms: saturated when almost concentric; a plain reciprocal
    /// against a body that blocks, unless this one ignores blockers; otherwise the same reciprocal
    /// through <see cref="IntegerSquareRoot"/>, which softens how fast it falls away.</summary>
    /// <param name="ratio"><see cref="Ratio"/>.</param>
    /// <param name="maxForce">the pushing body's <c>max_force</c>.</param>
    /// <param name="neighbourBlocks">the neighbour's <c>is_blocker</c>.</param>
    /// <param name="ignoresBlockers">the pushing body's <c>ignore_blocker</c>.</param>
    public static int Magnitude(int ratio, int maxForce, bool neighbourBlocks, bool ignoresBlockers)
    {
        if (ratio <= SaturationRatio) return maxForce;

        if (neighbourBlocks && !ignoresBlockers) return Reciprocal(ratio);

        int root = IntegerSquareRoot(ratio);
        return root == 0 ? maxForce : Reciprocal(root);
    }

    /// <summary>2^32 over the divisor, less one — the original's own expression, kept in that shape
    /// because the 64-bit numerator is what makes it exact.</summary>
    private static int Reciprocal(int divisor) => (int)((1L << 32) / divisor - One);
}
