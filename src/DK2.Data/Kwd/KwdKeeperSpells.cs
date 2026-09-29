// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Reads the researched fields of 406-byte KEEPERSPELLS(150) records.
/// Costs: imps-and-mana.md; shots: research-tree.md; cooldown and selectors:
/// keeper-spell-thunderbolt.md. Unknown bytes remain unparsed.</summary>
public static class KwdKeeperSpells
{
    private const int RecordSize = 406;

    /// <summary>Mana per second the spell's result costs while it exists.</summary>
    // Found here by scanning for the measured 7; OpenKeeper independently names the
    // same offset `manaDrain` and reads it u16, bracketed by its `e0` and `ee`.
    private const int OffsetManaUpkeepPerSecond = 0xE2;

    /// <summary>Mana to cast, once. 32-bit: Summon Horny's 100000 overflows a ushort.</summary>
    // s32 chosen here because 100000 overflows a u16; OpenKeeper independently reads
    // the same offset as s32 `manaCost`, with `bonusIcon` at 0x120 as the checkpoint.
    private const int OffsetManaCost = 0x11C;

    // The shot fields, located by DK2's own loader 0x004F7CE0, which copies each from the
    // file record into the 122-byte runtime spell (research-tree.md §5). OpenKeeper
    // names the same offsets shotData1/2, shotTypeId, keeperSpellId and bonusShot*.
    private const int OffsetShotData1 = 0xD0;
    private const int OffsetShotData2 = 0xD4;
    private const int OffsetKeeperSpellId = 0xEE;
    private const int OffsetShotTypeId = 0xF0;
    private const int OffsetBonusShotTypeId = 0x113;
    private const int OffsetBonusShotData1 = 0x114;
    private const int OffsetBonusShotData2 = 0x118;

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
        KeeperSpellId = rec[b + OffsetKeeperSpellId],
        CooldownRaw = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + 0xCC)),
        LocationSelector = rec[b + 0xEF],
        TargetSelector = rec[b + 0xDA],
        // 0x004F7CE0 copies runtime +0x54 from its intermediate at 0x154, which is file + 0x78.
        Flags = (KeeperSpellFlags)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + 0xDC)),
        ShotTypeId = rec[b + OffsetShotTypeId],
        ShotData1 = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetShotData1)),
        ShotData2 = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetShotData2)),
        BonusShotTypeId = rec[b + OffsetBonusShotTypeId],
        BonusShotData1 = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetBonusShotData1)),
        BonusShotData2 = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetBonusShotData2)),
    };

    private static string Name(byte[] rec, int b)
    {
        ReadOnlySpan<byte> s = rec.AsSpan(b, 32);
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]);
    }
}
