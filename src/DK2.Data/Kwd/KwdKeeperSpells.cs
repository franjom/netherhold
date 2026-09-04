// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Parser for KEEPERSPELLS(150) records: 13 spells over 5278 bytes, 406 each.
///
/// <para><b>Three fields of the 406 are read.</b> The record is not understood
/// and this parser does not pretend otherwise — spells proper arrive at M7. What
/// it exists for is that two of those three fields carry numbers the simulation
/// needs now and could not otherwise get: what Create Imp costs to cast, and
/// what an imp costs to keep.</para>
///
/// <para><b>Both offsets are validated structurally, not trusted.</b> The rule
/// this project uses for a record nobody has a template for: the stride must
/// divide the content exactly, a candidate reading must be plausible for
/// <i>every</i> record, and at least one value must be anchored to something
/// measured in the original.</para>
///
/// <list type="bullet">
/// <item><b>0x11C, mana cost (s32).</b> Possession 500, Create Imp 1500, Sight
/// Of Evil 5000, Heal 5000, Thunderbolt 6000, Call To Arms 10000, Chicken
/// 10000, Create Gold 15000, Turncoat 20000, Tremor 30000, Inferno 50000,
/// Summon Horny 100000, OneShotReaper 0. Thirteen plausible prices that rank the
/// way the game plays, and the owner measured Create Imp's 1500 independently.
/// Note the width: Summon Horny's 100000 does not fit in 16 bits.</item>
/// <item><b>0xE2, mana upkeep per second (u16).</b> Non-zero for exactly the
/// five spells with a persistent result — Create Imp 7, Summon Horny 2000, Call
/// To Arms 90, Possession 25, OneShotReaper 125 — and zero for all eight
/// instants. <b>Two independent anchors land in that column</b>: the owner
/// measured the imp's 7 from the mana orb, and 2000 is the figure the community
/// reports for a summoned Horned Reaper. A column that is zero for precisely the
/// spells which have nothing to keep is not a coincidence.</item>
/// </list>
///
/// <para>See docs/research/imps-and-mana.md and Q53/Q55.</para>
/// </summary>
public static class KwdKeeperSpells
{
    private const int RecordSize = 406;

    /// <summary>Mana per second the spell's result costs while it exists.</summary>
    // OpenKeeper calls this field `manaDrain` and reads it as an unsigned
    // short at exactly this offset (2026-09-03 cross-check; its own `e0` and
    // `ee` offset comments bracket it, so the arithmetic is self-checking).
    // Found here by scanning for a measured 7; named there. Independent.
    private const int OffsetManaUpkeepPerSecond = 0xE2;

    /// <summary>Mana to cast, once. 32-bit: Summon Horny's 100000 overflows a ushort.</summary>
    // OpenKeeper calls this `manaCost` and reads it as a SIGNED 32-bit integer
    // at exactly this offset, with `bonusIcon` at 0x120 immediately after it as
    // the checkpoint. We chose s32 independently, because Summon Horny's 100000
    // overflows a u16 - and arrived at the same width for the same reason.
    private const int OffsetManaCost = 0x11C;

    public static List<KeeperSpellDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<KeeperSpellDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.KeeperSpells) continue;

            int recordSize = chunk.ItemCount == 0 ? RecordSize : chunk.Content.Length / chunk.ItemCount;
            if (recordSize != RecordSize)
                throw new InvalidDataException(
                    $"KEEPERSPELLS record size {recordSize} is not {RecordSize}.");
            if (chunk.Content.Length != recordSize * chunk.ItemCount)
                throw new InvalidDataException("KEEPERSPELLS content size does not divide into records.");

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRecord(chunk.Content, i * recordSize));
        }
        return result;
    }

    private static KeeperSpellDef ParseRecord(byte[] rec, int b) => new()
    {
        Name = Name(rec, b),
        ManaUpkeepPerSecond =
            BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + OffsetManaUpkeepPerSecond)),
        ManaCost =
            BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetManaCost)),
    };

    private static string Name(byte[] rec, int b)
    {
        ReadOnlySpan<byte> s = rec.AsSpan(b, 32);
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]);
    }
}
