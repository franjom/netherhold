// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// DK2's 32-bit fixed-point number: raw integer with denominator 4096 (2^12).
/// CONFIRMED: ufdada/dk2-tools util_struc.bt (`FLOAT32 = 4096.0`).
/// Stored raw so simulation math stays in integers (ADR-0001); convert to
/// floating point only for display.
/// </summary>
public readonly record struct Fp32(int Raw)
{
    public const int One = 4096;

    public double ToDouble() => Raw / 4096.0;

    public static Fp32 FromDouble(double value) => new((int)Math.Round(value * 4096.0));

    /// <summary>Signed 12.12 multiplication, narrowed to 32 bits after the shift;
    /// see docs/research/combat-damage.md §1.</summary>
    public static Fp32 operator *(Fp32 left, Fp32 right) =>
        new(unchecked((int)(((long)left.Raw * right.Raw) >> 12)));

    public override string ToString() => ToDouble().ToString("0.###");
}
