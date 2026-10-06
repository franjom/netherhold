// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

namespace DK2.Data.Model;

/// <summary>A player: one of the ids <see cref="PlayerIds"/> names, 0 for nobody up to the last
/// rival keeper. The map format's id is one byte, and records that mirror DK2's layout keep it as
/// one; this type is what travels through rules, systems and commands, so a byte that means
/// something else cannot take a player's place by accident.</summary>
/// <remarks>A byte converts in implicitly, so the layout-mirroring records and the
/// <see cref="PlayerIds"/> constants flow into a <see cref="PlayerId"/> parameter unchanged. The way
/// out is explicit (<see cref="Value"/> or a cast), so a player never reads as a number by
/// accident. <see cref="ToString"/> is the bare number, as event text and CSV rows have always
/// printed it.</remarks>
public readonly record struct PlayerId(byte Value) : IComparable<PlayerId>
{
    public static implicit operator PlayerId(byte value) => new(value);

    public static explicit operator byte(PlayerId id) => id.Value;

    public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);

    public static bool operator <(PlayerId left, PlayerId right) => left.Value < right.Value;

    public static bool operator >(PlayerId left, PlayerId right) => left.Value > right.Value;

    public static bool operator <=(PlayerId left, PlayerId right) => left.Value <= right.Value;

    public static bool operator >=(PlayerId left, PlayerId right) => left.Value >= right.Value;

    /// <summary>Whether this player is a keeper: <see cref="PlayerIds.IsKeeper"/>.</summary>
    public bool IsKeeper => PlayerIds.IsKeeper(Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
