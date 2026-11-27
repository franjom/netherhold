// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>An effect definition id from the shipped data; zero requests no effect.</summary>
public readonly record struct EffectId(ushort Value)
{
    public static EffectId None
    {
        get
        {
            return default;
        }
    }

    public static implicit operator EffectId(ushort value)
    {
        return new EffectId(value);
    }

    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
