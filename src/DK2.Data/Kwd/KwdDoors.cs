// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Parser for the verified fields of 616-byte DOORS(140) records.</summary>
public static class KwdDoors
{
    public static byte[] WriteRecord(DoorDef record, KwdRecordImage template)
    {
        byte[] bytes = KwdOverlay.Image(template, KwdRecordType.Doors, RecordSize);
        KwdOverlay.Ascii(bytes, 0, 32, record.Name, Name(bytes, 0), trimmed: true);
        bytes[OffsetDoorId] = record.DoorId;
        bytes[OffsetCrateObjectId] = record.CrateObjectId;
        bytes[OffsetFlags] = (byte)record.Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetHealth), record.Health);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(OffsetHealthGain), record.HealthGain);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetGoldCost), record.GoldCost);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(OffsetManufactureRequired), record.ManufactureRequired);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(OffsetNameStringId), record.NameStringId);
        return bytes;
    }
    private const int RecordSize = 616;

    // Six 84-byte art resources follow the 32-byte name. The gameplay tail is
    // verified against all eight shipped records and the manual's six prices.
    private const int OffsetFlags = 0x228;
    private const int OffsetHealth = 0x22C;
    private const int OffsetHealthGain = 0x21C;
    private const int OffsetGoldCost = 0x22E;
    private const int OffsetManufactureRequired = 0x234;
    private const int OffsetDoorId = 0x244;
    private const int OffsetCrateObjectId = 0x246;
    private const int OffsetNameStringId = 0x23C;

    public static List<DoorDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<DoorDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Doors) continue;
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
                $"DOORS content is not {chunk.ItemCount} records of {RecordSize} bytes.");
    }

    private static DoorDef ParseRecord(byte[] record, int at) => new()
    {
        Name = Name(record, at),
        DoorId = record[at + OffsetDoorId],
        CrateObjectId = record[at + OffsetCrateObjectId],
        Health = U16(record, at + OffsetHealth),
        HealthGain = BinaryPrimitives.ReadInt16LittleEndian(record.AsSpan(at + OffsetHealthGain)),
        GoldCost = U16(record, at + OffsetGoldCost),
        ManufactureRequired = BinaryPrimitives.ReadUInt32LittleEndian(
            record.AsSpan(at + OffsetManufactureRequired)),
        Flags = (DoorFlags)record[at + OffsetFlags],
        NameStringId = U16(record, at + OffsetNameStringId),
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
