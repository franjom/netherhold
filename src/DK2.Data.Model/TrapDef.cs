// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// The verified gameplay fields from one 579-byte TRAPS(130) record.
/// Ambiguous timing, trigger and firing fields are deliberately omitted.
/// </summary>
public sealed record TrapDef
{
    /// <summary>The editor name stored in the record's first 32 bytes.</summary>
    public required string Name { get; init; }

    /// <summary>The stable record id; the shipped table contains 1 through 16.</summary>
    public required byte TrapId { get; init; }

    /// <summary>Initial health of a placed trap.</summary>
    public required ushort Health { get; init; }

    /// <summary>Gold price shown by the original manual and stored in the record.</summary>
    public required ushort GoldCost { get; init; }

    /// <summary>Workshop manufacture work required to produce the trap.</summary>
    public required ushort ManufactureRequired { get; init; }

    /// <summary>
    /// Id of the SHOTS record emitted by this trap, or zero for entries which
    /// emit no shot. The shipped references resolve to matching shot names.
    /// </summary>
    public required byte ShotTypeId { get; init; }

    /// <summary>Standing mana drain measured per game second, or zero.</summary>
    public required ushort ManaUpkeepPerSecond { get; init; }
}
