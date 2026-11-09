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
    public static byte[] WriteRecord(KeeperSpellDef record, KwdRecordImage template)
    {
        byte[] bytes = KwdOverlay.Image(template, KwdRecordType.KeeperSpells, RecordSize);
        KwdOverlay.Ascii(bytes, 0, 32, record.Name, Name(bytes, 0), trimmed: false);
        KwdOverlay.Ascii(bytes, OffsetSoundCategory, 32, record.SoundCategory, Name(bytes, OffsetSoundCategory), trimmed: false);
        KwdOverlay.Ascii(bytes, OffsetGuiButtonSoundCategory, 32, record.GuiButtonSoundCategory,
            Name(bytes, OffsetGuiButtonSoundCategory), trimmed: false);
        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        if (record.ManaUpkeepPerSecond < 0 || record.ManaUpkeepPerSecond > ushort.MaxValue)
            throw new InvalidDataException("KEEPERSPELLS mana upkeep must fit its unsigned 16-bit field.");
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetManaUpkeepPerSecond), (ushort)record.ManaUpkeepPerSecond);
        I32(OffsetManaCost, record.ManaCost);
        I32(OffsetCooldown, record.CooldownRaw);
        I32(OffsetShotData1, record.ShotData1);
        I32(OffsetShotData2, record.ShotData2);
        I32(OffsetBonusShotData1, record.BonusShotData1);
        I32(OffsetBonusShotData2, record.BonusShotData2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(OffsetFlags), (uint)record.Flags);
        bytes[OffsetKeeperSpellId] = record.KeeperSpellId;
        bytes[OffsetLocationSelector] = record.LocationSelector;
        bytes[OffsetTargetSelector] = record.TargetSelector;
        bytes[OffsetShotTypeId] = record.ShotTypeId;
        bytes[OffsetBonusShotTypeId] = record.BonusShotTypeId;
        return bytes;
    }
    private const int RecordSize = 406;
    private const int OffsetCooldown = 0xCC;
    private const int OffsetLocationSelector = 0xEF;
    private const int OffsetTargetSelector = 0xDA;
    private const int OffsetFlags = 0xDC;

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

    /// <summary>The category name, 32 bytes: the loader copies its intermediate <c>+0x169</c> (file + 0x78) through the sound
    /// manager's lookup into the runtime spell's <c>+0x6D</c> (<c>0x004F7CE0</c>, docs/research/sound-effects-combat.md).</summary>
    private const int OffsetSoundCategory = 0xF1;

    /// <summary>The GUI button's category name, 32 bytes, the runtime spell's <c>+0x71</c> (<c>0x004F7CE0</c>).</summary>
    private const int OffsetGuiButtonSoundCategory = 0x174;

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
            if (chunk.Content.Length != (long)recordSize * chunk.ItemCount)
                throw new InvalidDataException("KEEPERSPELLS content size does not divide into records.");

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRecord(chunk.Content, i * recordSize));
        }
        return result;
    }

    private static KeeperSpellDef ParseRecord(byte[] rec, int b) => new()
    {
        Name = Name(rec, b),
        SoundCategory = Name(rec, b + OffsetSoundCategory),
        GuiButtonSoundCategory = Name(rec, b + OffsetGuiButtonSoundCategory),
        ManaUpkeepPerSecond =
            BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + OffsetManaUpkeepPerSecond)),
        ManaCost =
            BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetManaCost)),
        KeeperSpellId = rec[b + OffsetKeeperSpellId],
        CooldownRaw = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetCooldown)),
        LocationSelector = rec[b + OffsetLocationSelector],
        TargetSelector = rec[b + OffsetTargetSelector],
        // 0x004F7CE0 copies runtime +0x54 from its intermediate at 0x154, which is file + 0x78.
        Flags = (KeeperSpellFlags)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + OffsetFlags)),
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
