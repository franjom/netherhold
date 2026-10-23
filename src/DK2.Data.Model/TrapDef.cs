// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// The verified gameplay fields from one 579-byte TRAPS(130) record.
/// Runtime fields are mapped through the loader in docs/research/traps.md.
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

    public uint Flags { get; init; }
    public byte TriggerType { get; init; }
    public int RangeTiles { get; init; }
    public int RechargeSecondsRaw { get; init; }
    public int ChargeSecondsRaw { get; init; }
    public int ThreatSecondsRaw { get; init; }
    public int ManaCostToFire { get; init; }
    public int ShotData1 { get; init; }
    public int ShotData2 { get; init; }
    public int ShotOffsetX { get; init; }
    public int ShotOffsetY { get; init; }
    public int ShotOffsetZ { get; init; }
    public short HealthGain { get; init; }

    /// <summary>Text.str id of the trap's name, record offset 0x208, runtime <c>def+0xA8</c>
    /// (loader <c>0x004AA440</c>); the hover status <c>0x004A9B70</c> and popup <c>0x0042D4A0</c> read it.</summary>
    public ushort NameStringId { get; init; }
}
