// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Parser for OBJECTS(240) records: 145 objects over 129,630 bytes, 894 each.
/// The first 812 bytes are nine art resources and a light; integer animation timing
/// is retained alongside the gameplay tail.</summary>
/// <remarks><para>Layout STRONGLY INFERRED: field order from OpenKeeper's <c>readObjects</c>
/// (ADR-0003), offsets derived then validated — the widths close exactly on the
/// independently measured 894-byte stride, <see cref="ObjectDef.ObjectId"/> at 0x35A
/// reads 1 to 145 each exactly once, and the anchors land (object 22 is the Goblin's
/// bed, 26 the Vampire's, 31-34 the Graveyard's; <c>ObjectsOnRealDataTests</c>).
/// Evidence: docs/research/objects.md §§1-2.</para>
/// <para>Deliberately not read, all presentation or unexplained: the nine art
/// resource visuals, <c>material</c>, the three unknown bytes at 0x341, two of the
/// three effect ids (0x358 is <see cref="ObjectDef.IdleEffectId"/>), the sound category.</para></remarks>
public static class KwdObjects
{
    public static byte[] WriteRecord(ObjectDef record, KwdRecordImage template)
    {
        byte[] bytes = KwdOverlay.Image(template, KwdRecordType.Objects, RecordSize);
        KwdOverlay.Ascii(bytes, OffsetName, 32, record.Name, Name(bytes, OffsetName), trimmed: true);
        void Fp(int offset, Fp32 value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value.Raw);
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        bytes[OffsetObjectId] = record.ObjectId;
        bytes[OffsetRoomCapacity] = record.RoomCapacity;
        bytes[OffsetStartState] = record.StartState;
        bytes[OffsetPickUpPriority] = record.PickUpPriority;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(OffsetFlags), (uint)record.Flags);
        U16(OffsetHp, record.Hp);
        U16(OffsetManaValue, record.ManaValue);
        U16(OffsetIdleEffect, record.IdleEffectId);
        U16(OffsetNameStringId, record.NameStringId);
        U16(OffsetTooltipStringId, record.TooltipStringId);
        Fp(OffsetWidth, record.Width);
        Fp(OffsetHeight, record.Height);
        Fp(OffsetMass, record.Mass);
        Fp(OffsetSpeed, record.Speed);
        Fp(0x33C, record.AirFriction);
        U16(OffsetReactionTurn, record.ReactionTurn);
        for (int slot = 0; slot < Math.Min(9, record.ArtSlots.Count); slot++)
        {
            int offset = 0x20 + slot * 84;
            CreatureAnimationTiming timing = record.ArtSlots[slot];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 0x40), (uint)timing.Flags);
            bytes[offset + 0x50] = timing.ResourceType;
            if (timing.ResourceType != 6) continue;
            bytes[offset + 0x51] = timing.StartSegmentFrames;
            bytes[offset + 0x52] = timing.EndSegmentFrames;
            U16(offset + 0x44, timing.FrameCount);
            U16(offset + 0x48, timing.FramesPerSecond);
        }
        if (record.Light is KwdLight light) KwdAdditionalRecords.WriteLight(bytes, 0x314, light);
        return bytes;
    }
    private const int RecordSize = 894;

    // The gameplay tail. Everything before 0x32C is art.
    private const int OffsetName = 0x000;
    private const int OffsetWidth = 0x32C;
    private const int OffsetHeight = 0x330;
    private const int OffsetMass = 0x334;
    private const int OffsetSpeed = 0x338;
    private const int OffsetFlags = 0x344;
    private const int OffsetHp = 0x348;
    private const int OffsetReactionTurn = 0x34A;
    private const int OffsetManaValue = 0x34E;
    private const int OffsetTooltipStringId = 0x350;
    private const int OffsetNameStringId = 0x352;
    private const int OffsetIdleEffect = 0x358;
    private const int OffsetObjectId = 0x35A;
    private const int OffsetStartState = 0x35B;
    private const int OffsetRoomCapacity = 0x35C;
    private const int OffsetPickUpPriority = 0x35D;

    public static List<ObjectDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<ObjectDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Objects) continue;

            int recordSize = chunk.ItemCount == 0 ? RecordSize : chunk.Content.Length / chunk.ItemCount;
            if (recordSize != RecordSize)
                throw new InvalidDataException(
                    $"OBJECTS record size {recordSize} is not {RecordSize}.");
            if (chunk.Content.Length != recordSize * chunk.ItemCount)
                throw new InvalidDataException("OBJECTS content size does not divide into records.");

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRecord(chunk.Content, i * recordSize));
        }
        return result;
    }

    private static ObjectDef ParseRecord(byte[] rec, int b)
    {
        Fp32 Fp(int off) => new(BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + off)));
        ushort U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + off));

        var artSlots = new CreatureAnimationTiming[9];
        for (int slot = 0; slot < artSlots.Length; slot++)
        {
            int offset = 0x20 + slot * 84;
            byte type = rec[b + offset + 0x50];
            artSlots[slot] = new CreatureAnimationTiming
            {
                ResourceType = type,
                Flags = (CreatureAnimationFlags)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + offset + 0x40)),
                FrameCount = type == 6 ? U16(offset + 0x44) : (ushort)0,
                FramesPerSecond = type == 6 ? U16(offset + 0x48) : (ushort)0,
                StartSegmentFrames = type == 6 ? rec[b + offset + 0x51] : (byte)0,
                EndSegmentFrames = type == 6 ? rec[b + offset + 0x52] : (byte)0,
            };
        }

        return new ObjectDef
        {
            Name = Name(rec, b + OffsetName),
            ObjectId = rec[b + OffsetObjectId],
            Flags = (ObjectFlags)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + OffsetFlags)),

            Hp = U16(OffsetHp),
            ManaValue = U16(OffsetManaValue),
            RoomCapacity = rec[b + OffsetRoomCapacity],
            StartState = rec[b + OffsetStartState],
            IdleEffectId = U16(OffsetIdleEffect),
            PickUpPriority = rec[b + OffsetPickUpPriority],

            Width = Fp(OffsetWidth),
            Height = Fp(OffsetHeight),
            Mass = Fp(OffsetMass),
            Speed = Fp(OffsetSpeed),
            AirFriction = Fp(0x33C),
            ReactionTurn = U16(OffsetReactionTurn),
            ArtSlots = artSlots,
            Light = KwdAdditionalRecords.Light(rec, b + 0x314),

            NameStringId = U16(OffsetNameStringId),
            TooltipStringId = U16(OffsetTooltipStringId),
        };
    }

    private static string Name(byte[] rec, int at)
    {
        ReadOnlySpan<byte> s = rec.AsSpan(at, 32);
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]).Trim();
    }
}
