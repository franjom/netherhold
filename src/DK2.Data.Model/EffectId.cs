// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>An effect definition id from the shipped data; zero requests no effect.</summary>
public readonly record struct EffectId(ushort Value)
{
    public static EffectId None => default;
    public static implicit operator EffectId(ushort value) => new(value);
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
