// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// The verified gameplay fields from one 616-byte DOORS(140) record.
/// Presentation resources and fields whose meaning is not pinned are omitted.
/// </summary>
public sealed record DoorDef
{
    /// <summary>The editor name stored in the record's first 32 bytes.</summary>
    public required string Name { get; init; }

    /// <summary>The stable record id; the shipped table contains 1 through 8.</summary>
    public required byte DoorId { get; init; }

    /// <summary>Initial health of a placed door.</summary>
    public required ushort Health { get; init; }

    /// <summary>Gold price shown by the original manual and stored in the record.</summary>
    public required ushort GoldCost { get; init; }

    /// <summary>Workshop manufacture work required to produce the door.</summary>
    public required uint ManufactureRequired { get; init; }
}
