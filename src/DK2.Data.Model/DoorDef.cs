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

    /// <summary>Signed recovery delta at file 0x21C, runtime definition +0x93.</summary>
    public short HealthGain { get; init; }

    /// <summary>Gold price shown by the original manual and stored in the record.</summary>
    public required ushort GoldCost { get; init; }

    /// <summary>The crate object a Workshop makes for this door (ADR-0203): every shipped door with a
    /// crate matches its own crate object here. 0 when it has none.</summary>
    public byte CrateObjectId { get; init; }

    /// <summary>Workshop manufacture work required to produce the door.</summary>
    public required uint ManufactureRequired { get; init; }

    /// <summary>The flags byte at record offset 0x228, runtime <c>def+0x8B</c>
    /// (doors-and-traps.md, "Door runtime behavior").</summary>
    public DoorFlags Flags { get; init; }
}

/// <summary>The door record's flags byte. Named by which shipped records carry each bit: the
/// Secret Door alone has bit 5, both barricades bit 6, the two hero doors bit 7 (CONFIRMED
/// from data); the binary's navigation writer treats bit 6 as a permanent lock.</summary>
[Flags]
public enum DoorFlags : byte
{
    None = 0,
    Secret = 0x20,
    Barricade = 0x40,
    Hero = 0x80,
}
