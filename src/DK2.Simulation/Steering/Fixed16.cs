// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Steering;

/// <summary>A 16.16 vector of the steering layer, 65536 to a tile: two dwords in the original,
/// x then y. The methods are the original's small vector helpers, one each.</summary>
public readonly record struct Fixed16Vector(int X, int Y)
{
    public static readonly Fixed16Vector Zero = new(0, 0);

    public static Fixed16Vector operator +(Fixed16Vector a, Fixed16Vector b) => new(a.X + b.X, a.Y + b.Y);

    public static Fixed16Vector operator -(Fixed16Vector a, Fixed16Vector b) => new(a.X - b.X, a.Y - b.Y);

    public static Fixed16Vector operator -(Fixed16Vector a) => new(-a.X, -a.Y);

    /// <summary>Both components times a 16.16 scalar (<c>0x004D1E70</c>).</summary>
    public Fixed16Vector Scale(int k) => new(Fixed16.Mul(X, k), Fixed16.Mul(Y, k));

    /// <summary>Both components over a 16.16 scalar (<c>0x004D1EC0</c>).</summary>
    public Fixed16Vector Over(int k) => new(Fixed16.Div(X, k), Fixed16.Div(Y, k));

    /// <summary>The left perpendicular (<c>0x004D5060</c>): x takes -y, y takes x.</summary>
    public Fixed16Vector Perpendicular() => new(-Y, X);

    public int Dot(Fixed16Vector o) => Fixed16.Mul(X, o.X) + Fixed16.Mul(Y, o.Y);

    public int Octile() => Fixed16.Octile(X, Y);

    public int Length() => Fixed16.Length(X, Y);

    /// <summary>The tile this position is on: the integer part of each component.</summary>
    public (int X, int Y) Tile() => (X >> 16, Y >> 16);
}

/// <summary>The steering layer's 16.16 arithmetic, operation for operation as the original does
/// it (docs/research/steering-behaviours.md). A product is 64-bit and keeps bits 16..47; a
/// quotient shifts the dividend up by 16 and truncates toward zero; a length is the three-band
/// square root. Positions are sixteen times the simulation's 1/4096 units.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class Fixed16
{
    public const int One = 1 << 16;

    public static int Mul(int a, int b) => (int)(((long)a * b) >> 16);

    public static int Div(int a, int b) => (int)(((long)a << 16) / b);

    public static int Abs(int v) => v < 0 ? -v : v;

    /// <summary>The longer axis plus half the shorter (<c>0x004D4570</c>).</summary>
    public static int Octile(int dx, int dy)
    {
        int x = Abs(dx), y = Abs(dy);
        int smaller = x < y ? x : y;
        return x + y - (smaller >> 1);
    }

    /// <summary>Euclidean length in three bands: exact under 1/16 tile, else the inputs shifted by 8
    /// or by 18 before squaring and the root shifted back. The band is chosen on the OR of the
    /// absolute components, so a vector just past a band edge loses that band's precision.</summary>
    public static int Length(int dx, int dy)
    {
        int x = Abs(dx), y = Abs(dy);
        int bits = x | y;
        if ((bits & ~0xFFF) == 0) return IntegerMath.SquareRoot((x * x) + (y * y));
        if ((bits & ~0x3FFFFF) == 0)
        {
            x >>= 8;
            y >>= 8;
            return IntegerMath.SquareRoot((x * x) + (y * y)) << 8;
        }

        x >>= 18;
        y >>= 18;
        return IntegerMath.SquareRoot((x * x) + (y * y)) << 18;
    }

    /// <summary>A unit vector (<c>0x004D1F10</c>) in the same three bands. The smallest squares 64
    /// times the vector and multiplies in 32 bits; the others divide 2^32 by the length in their
    /// band's units and take the 64-bit product. A zero vector stays zero.</summary>
    public static Fixed16Vector Normalise(Fixed16Vector v)
    {
        int x = Abs(v.X), y = Abs(v.Y);
        int bits = x | y;
        if ((bits & ~0xFFF) == 0)
        {
            int root = IntegerMath.SquareRoot(((x * x) + (y * y)) << 6);
            int scale = root == 0 ? 0 : 0x80000 / root;
            return new Fixed16Vector(unchecked(v.X * scale), unchecked(v.Y * scale));
        }

        if ((bits & ~0x3FFFFF) == 0)
        {
            x >>= 8;
            y >>= 8;
            int root = IntegerMath.SquareRoot((x * x) + (y * y));
            return v.Scale(root == 0 ? 0 : 0x1000000 / root);
        }

        x >>= 18;
        y >>= 18;
        int r = IntegerMath.SquareRoot((x * x) + (y * y));
        return v.Scale(r == 0 ? 0 : 0x4000 / r);
    }

    /// <summary>Clamp a vector's length to <paramref name="max"/> (<c>0x004D4F60</c>): unchanged
    /// while shorter, else scaled by <c>max / length</c>.</summary>
    public static Fixed16Vector Truncate(Fixed16Vector v, int max)
    {
        int len = v.Length();
        return len < max ? v : v.Scale(Div(max, len));
    }
}
