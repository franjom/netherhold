// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Parser for the verified fields of 579-byte TRAPS(130) records.</summary>
public static class KwdTraps
{
    private const int RecordSize = 579;

    // Five 84-byte art resources follow the 32-byte name. Gold prices are an
    // exact 11-for-11 match with the manual; shot ids resolve through SHOTS.
    private const int OffsetHealth = 0x1F0;
    private const int OffsetGoldCost = 0x1F2;
    private const int OffsetManufactureRequired = 0x1FA;
    private const int OffsetManaUpkeepPerSecond = 0x202;
    private const int OffsetTrapId = 0x20C;
    private const int OffsetShotTypeId = 0x20D;
    private const int OffsetCrateObjectId = 0x20E;

    public static List<TrapDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<TrapDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Traps) continue;
            Validate(chunk);

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRecord(chunk.Content, i * RecordSize));
        }
        return result;
    }

    private static void Validate(KwdChunk chunk)
    {
        if (chunk.ItemCount < 0 || chunk.Content.Length != chunk.ItemCount * RecordSize)
            throw new InvalidDataException(
                $"TRAPS content is not {chunk.ItemCount} records of {RecordSize} bytes.");
    }

    private static TrapDef ParseRecord(byte[] record, int at) => new()
    {
        Name = Name(record, at),
        TrapId = record[at + OffsetTrapId],
        Health = U16(record, at + OffsetHealth),
        GoldCost = U16(record, at + OffsetGoldCost),
        ManufactureRequired = U16(record, at + OffsetManufactureRequired),
        ShotTypeId = record[at + OffsetShotTypeId],
        CrateObjectId = record[at + OffsetCrateObjectId],
        ManaUpkeepPerSecond = U16(record, at + OffsetManaUpkeepPerSecond),
    };

    private static ushort U16(byte[] record, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(at));

    private static string Name(byte[] record, int at)
    {
        ReadOnlySpan<byte> bytes = record.AsSpan(at, 32);
        int end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }
}
