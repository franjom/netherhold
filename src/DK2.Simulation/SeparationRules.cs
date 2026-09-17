// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>DK2's <c>Separation</c> steering force, the rule that stops two creatures standing in
/// the same place (Q345, docs/research/navigation-and-steering.md §3.2).
/// <para><b>Nothing calls this</b> (ADR-0156): the combination and integration are read, but our
/// mover has no velocity to accept a force until ADR-0157's phase 3.</para>
/// <para>Scalars are the steering layer's 16.16 fixed point, not <see cref="Fp32"/>'s 1/4096.
/// Arithmetic is reproduced operation for operation rather than reinterpreted: where the original
/// divides a 64-bit numerator by a 16.16 ratio, so does this.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-and-steering.md")]
public static class SeparationRules
{
    /// <summary>1.0 in the steering layer's 16.16 scalars.</summary>
    public const int One = 1 << 16;

    /// <summary>Below this distance-to-radii ratio (<c>0x28F</c>, about 1 %) the force saturates at <c>max_force</c>.</summary>
    public const int SaturationRatio = 0x28F;

    /// <summary>Every creature's <c>radius</c>: 0.2. Written as a literal by the steering reset at
    /// <c>0x004D5BA9</c>, so separation does not vary with creature size (§3.4).</summary>
    public const int ShippedRadius = 0x3333;

    /// <summary>Every creature's <c>max_force</c>: 1.5, a literal at <c>0x004D5B7D</c>. The
    /// combiner truncates each behaviour's force to this before weighting it, which is why the
    /// curve arm's very large magnitudes never reach the mover.</summary>
    public const int ShippedMaxForce = 0x18000;

    /// <summary>Every creature's <c>max_speed</c>: 0.4, a literal at <c>0x004D5B87</c>. The
    /// constructor's earlier 0x50000 is overwritten by the reset and never used.</summary>
    public const int ShippedMaxSpeed = 0x6666;

    /// <summary>The original's distance: the longer axis plus half the shorter.</summary>
    public static int OctileDistance(int dx, int dy)
    {
        int x = dx < 0 ? -dx : dx;
        int y = dy < 0 ? -dy : dy;
        int smaller = x < y ? x : y;
        return x + y - (smaller >> 1);
    }

    /// <summary>The original's integer square root, shared with the router's path length through
    /// <see cref="IntegerMath.SquareRoot"/>: it stops at its first converged guess, and that is
    /// what the force curve is fed.</summary>
    public static int IntegerSquareRoot(int value) => IntegerMath.SquareRoot(value);

    /// <summary>Bodies push only while overlapping, and never at zero distance (no direction).</summary>
    public static bool Applies(int distance, int radiusSum) =>
        distance > 0 && distance <= radiusSum;

    /// <summary>Distance over summed radii in 16.16: 0 concentric, <see cref="One"/> just touching.</summary>
    public static int Ratio(int distance, int radiusSum) =>
        (int)(((long)distance << 16) / radiusSum);

    /// <summary>The force's size. Three arms: saturated when almost concentric; a plain reciprocal
    /// against a body that blocks, unless this one ignores blockers; otherwise the same reciprocal
    /// through <see cref="IntegerSquareRoot"/>, which softens how fast it falls away.</summary>
    public static int Magnitude(int ratio, int maxForce, bool neighbourBlocks, bool ignoresBlockers)
    {
        if (ratio <= SaturationRatio) return maxForce;

        if (neighbourBlocks && !ignoresBlockers) return Reciprocal(ratio);

        int root = IntegerSquareRoot(ratio);
        return root == 0 ? maxForce : Reciprocal(root);
    }

    /// <summary>2^32 over the divisor, less one — the original's expression, exact in 64 bits.</summary>
    public static int Reciprocal(int divisor) => (int)((1L << 32) / divisor - One);

    /// <summary>The 5x5 table at <c>[0x006ED420]</c>, rows the neighbour's size class, columns the
    /// pusher's: <c>B / (A + B)</c> in 16.16, except (A 4, B 5), where the initialiser reused a
    /// register and stored 0.75 instead of 5/9. Kept cell for cell.</summary>
    private static readonly int[] SizeShareTable =
    [
        0x8000, 0x553F, 0x4000, 0x3333, 0x2A7E,
        0xAAC0, 0x8000, 0x6666, 0x553F, 0x4937,
        0xC000, 0x9999, 0x8000, 0x6DD2, 0x6000,
        0xCCCC, 0xAAC0, 0x922D, 0x8000, 0x71A9,
        0xD53F, 0xB6C8, 0xA000, 0xC000, 0x8000,
    ];

    /// <summary>The neighbour's share of the pair's size, for size classes 1..5 (definition
    /// <c>+0x624</c>, KWD <c>0xEA8</c>).</summary>
    public static int SizeShare(int pusherSize, int neighbourSize) =>
        SizeShareTable[((neighbourSize - 1) * 5) + (pusherSize - 1)];

    /// <summary>The factor the push is finally scaled by: <c>((cosine + 1) / 2 + 0.2) x share</c> as
    /// the original's plain 32-bit multiply, with no 16.16 shift. It is about 65536 times a proper
    /// product, so a contact saturates at <c>max_force</c>; and it wraps negative once the true
    /// product reaches 2^31, which turns the push toward the neighbour (Q345).</summary>
    public static int Scale(int cosine, int share)
    {
        int k = (cosine + One) >> 1;
        return unchecked((k + 0x3333) * share);
    }
}
