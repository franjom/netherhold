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

    /// <summary>Keeper strength contribution; record +0x1EA, descriptor +0x8C.
    /// See computer-keeper-threat.md section 2.</summary>
    public ushort Threat { get; init; }

    /// <summary>Initial health of a placed trap.</summary>
    public required ushort Health { get; init; }

    /// <summary>Gold price shown by the original manual and stored in the record.</summary>
    public required ushort GoldCost { get; init; }

    /// <summary>The crate object a Workshop makes for this trap (ADR-0203): every shipped trap with a
    /// crate matches its own crate object here. 0 when it has none.</summary>
    public byte CrateObjectId { get; init; }

    /// <summary>Workshop manufacture work required to produce the trap.</summary>
    public required ushort ManufactureRequired { get; init; }

    /// <summary>Id of the SHOTS record emitted by this trap, or zero for entries which emit no
    /// shot.
    /// <para><b>A record id, not a row index, and the two differ by one.</b> SHOTS carries its own
    /// id byte at <c>+0xBE</c>, running 1..65 across the 65 shipped records, and every trap's value
    /// matches one of them: Gas Trap 58 is <i>Gas Trap Cloud</i>, Boulder Trap 26 is <i>Boulder</i>,
    /// Jack In The Box 64 is <i>Jack In The Box</i>. Index the table by row instead and Gas Trap
    /// fires <i>Lightning Trap Shot</i>. Asserted in
    /// <c>DoorsAndTrapsDefinitionsOnRealDataTests</c>; SHOTS parses into definitions keyed by this
    /// id.</para></summary>
    public required byte ShotTypeId { get; init; }

    /// <summary>Standing mana drain measured per game second, or zero.</summary>
    public required ushort ManaUpkeepPerSecond { get; init; }
}
