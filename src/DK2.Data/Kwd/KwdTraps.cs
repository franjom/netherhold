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
    public static byte[] WriteRecord(TrapDef record, KwdRecordImage template)
    {
        byte[] bytes = KwdOverlay.Image(template, KwdRecordType.Traps, RecordSize);
        KwdOverlay.Ascii(bytes, 0, 32, record.Name, Name(bytes, 0), trimmed: true);
        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        bytes[OffsetTrapId] = record.TrapId;
        bytes[OffsetShotTypeId] = record.ShotTypeId;
        bytes[OffsetCrateObjectId] = record.CrateObjectId;
        bytes[OffsetTriggerType] = record.TriggerType;
        U16(OffsetThreat, record.Threat);
        U16(OffsetHealth, record.Health);
        U16(OffsetGoldCost, record.GoldCost);
        U16(OffsetManufactureRequired, record.ManufactureRequired);
        U16(OffsetManaUpkeepPerSecond, record.ManaUpkeepPerSecond);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(OffsetFlags), record.Flags);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(OffsetHealthGain), record.HealthGain);
        I32(OffsetRange, record.RangeTiles);
        I32(OffsetRecharge, record.RechargeSecondsRaw);
        I32(OffsetCharge, record.ChargeSecondsRaw);
        I32(OffsetThreatSeconds, record.ThreatSecondsRaw);
        I32(OffsetManaCostToFire, record.ManaCostToFire);
        I32(OffsetShotData1, record.ShotData1);
        I32(OffsetShotData2, record.ShotData2);
        I32(OffsetShotX, record.ShotOffsetX);
        I32(OffsetShotY, record.ShotOffsetY);
        I32(OffsetShotZ, record.ShotOffsetZ);
        return bytes;
    }
    private const int RecordSize = 579;

    // Five 84-byte art resources follow the 32-byte name. Gold prices are an
    // exact 11-for-11 match with the manual; shot ids resolve through SHOTS.
    private const int OffsetThreat = 0x1EA;
    private const int OffsetHealth = 0x1F0;
    private const int OffsetGoldCost = 0x1F2;
    private const int OffsetManufactureRequired = 0x1FA;
    private const int OffsetManaUpkeepPerSecond = 0x202;
    private const int OffsetTrapId = 0x20C;
    private const int OffsetShotTypeId = 0x20D;
    private const int OffsetCrateObjectId = 0x20E;
    private const int OffsetFlags = 0x1EC;
    private const int OffsetTriggerType = 0x20B;
    private const int OffsetRange = 0x1DC;
    private const int OffsetRecharge = 0x1C8;
    private const int OffsetCharge = 0x1CC;
    private const int OffsetThreatSeconds = 0x1D0;
    private const int OffsetManaCostToFire = 0x1D4;
    private const int OffsetShotData1 = 0x1E0;
    private const int OffsetShotData2 = 0x1E4;
    private const int OffsetShotX = 0x231;
    private const int OffsetShotY = 0x235;
    private const int OffsetShotZ = 0x239;
    private const int OffsetHealthGain = 0x241;

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
        Threat = U16(record, at + OffsetThreat),
        Health = U16(record, at + OffsetHealth),
        GoldCost = U16(record, at + OffsetGoldCost),
        ManufactureRequired = U16(record, at + OffsetManufactureRequired),
        ShotTypeId = record[at + OffsetShotTypeId],
        CrateObjectId = record[at + OffsetCrateObjectId],
        ManaUpkeepPerSecond = U16(record, at + OffsetManaUpkeepPerSecond),
        Flags = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(at + OffsetFlags)),
        TriggerType = record[at + OffsetTriggerType],
        RangeTiles = I32(record, at + OffsetRange),
        RechargeSecondsRaw = I32(record, at + OffsetRecharge),
        ChargeSecondsRaw = I32(record, at + OffsetCharge),
        ThreatSecondsRaw = I32(record, at + OffsetThreatSeconds),
        ManaCostToFire = I32(record, at + OffsetManaCostToFire),
        ShotData1 = I32(record, at + OffsetShotData1),
        ShotData2 = I32(record, at + OffsetShotData2),
        ShotOffsetX = I32(record, at + OffsetShotX),
        ShotOffsetY = I32(record, at + OffsetShotY),
        ShotOffsetZ = I32(record, at + OffsetShotZ),
        HealthGain = BinaryPrimitives.ReadInt16LittleEndian(record.AsSpan(at + OffsetHealthGain)),
    };

    private static int I32(byte[] record, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(at));

    private static ushort U16(byte[] record, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(at));

    private static string Name(byte[] record, int at)
    {
        ReadOnlySpan<byte> bytes = record.AsSpan(at, 32);
        int end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }
}
