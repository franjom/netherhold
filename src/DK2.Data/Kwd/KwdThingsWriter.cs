// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

public static partial class KwdThings
{
    public static byte[] WriteRecord(ThingRecord row, KwdRecordImage template)
    {
        if (!KwdTaggedRecords.ThingPayloadSizes.TryGetValue(row.Tag, out var layout))
            throw new InvalidDataException($"Unsupported THINGS tag {row.Tag}.");
        byte[] image = KwdOverlay.TaggedImage(template, KwdRecordType.Things, row.Tag, layout.PayloadSize);
        Span<byte> payload = image.AsSpan(8);
        switch (row)
        {
            case ObjectThing c when c.Tag == 194:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                KwdAdditionalRecords.WriteRaw(image, 8 + 8, 4, c.Unknown_0008);
                BinaryPrimitives.WriteInt32LittleEndian(payload[12..], c.KeeperSpellId);
                BinaryPrimitives.WriteInt32LittleEndian(payload[16..], c.MoneyAmount);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[20..], c.TriggerId);
                payload[22] = c.ObjectId;
                payload[23] = c.PlayerId;
                break;
            case TrapThing c when c.Tag == 195:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Unknown_0008);
                payload[12] = c.NumberOfShots;
                payload[13] = c.TrapId;
                payload[14] = c.PlayerId;
                payload[15] = c.Unknown_000F;
                break;
            case DoorThing c when c.Tag == 196:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Unknown_0008);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[12..], c.TriggerId);
                payload[14] = c.DoorId;
                payload[15] = c.PlayerId;
                payload[16] = c.Flags;
                KwdAdditionalRecords.WriteRaw(image, 8 + 17, 3, c.Unknown_0011);
                break;
            case ActionPointThing c when c.Tag == 197:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.StartX);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.StartY);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.EndX);
                BinaryPrimitives.WriteInt32LittleEndian(payload[12..], c.EndY);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[16..], c.WaitDelay);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[18..], c.Flags);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[20..], c.TriggerId);
                payload[22] = c.ActionPointId;
                payload[23] = c.NextWaypointId;
                KwdOverlay.Ascii(image, 8 + 24, 32, c.Name, KwdAdditionalRecords.Ascii(image, 8 + 24, 32), trimmed: true);
                break;
            case NeutralCreatureThing c when c.Tag == 198:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Z);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[12..], c.GoldHeld);
                payload[14] = c.Level;
                payload[15] = c.Flags;
                BinaryPrimitives.WriteInt32LittleEndian(payload[16..], c.InitialHealth);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[20..], c.TriggerId);
                payload[22] = c.CreatureId;
                payload[23] = c.Unknown_0017;
                break;
            case GoodCreatureThing c when c.Tag == 199:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Z);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[12..], c.GoldHeld);
                payload[14] = c.Level;
                payload[15] = c.Flags;
                BinaryPrimitives.WriteInt32LittleEndian(payload[16..], c.ObjectiveTargetActionPointId);
                BinaryPrimitives.WriteInt32LittleEndian(payload[20..], c.InitialHealth);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[24..], c.TriggerId);
                payload[26] = c.ObjectiveTargetPlayerId;
                payload[27] = c.Objective;
                payload[28] = c.CreatureId;
                payload[29] = c.WanderRadius;
                payload[30] = c.PartyId;
                payload[31] = c.Flags2;
                break;
            case KeeperCreatureThing c when c.Tag == 200:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Z);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[12..], c.GoldHeld);
                payload[14] = c.Level;
                payload[15] = c.Flags;
                BinaryPrimitives.WriteInt32LittleEndian(payload[16..], c.InitialHealth);
                BinaryPrimitives.WriteInt32LittleEndian(payload[20..], c.ObjectiveTargetActionPointId);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[24..], c.TriggerId);
                payload[26] = c.CreatureId;
                payload[27] = c.PlayerId;
                break;
            case HeroPartyThing c when c.Tag == 201:
                KwdOverlay.Ascii(image, 8 + 0, 32, c.Name, KwdAdditionalRecords.Ascii(image, 8 + 0, 32), trimmed: true);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[32..], c.TriggerId);
                payload[34] = c.PartyId;
                BinaryPrimitives.WriteInt32LittleEndian(payload[35..], c.Unknown_0023);
                BinaryPrimitives.WriteInt32LittleEndian(payload[39..], c.Unknown_0027);
                if (c.Members.Count != 16) throw new InvalidDataException("Hero parties require all sixteen explicit member slots.");
                for (int i = 0; i < 16; i++)
                {
                    byte[] member = new byte[40];
                    BinaryPrimitives.WriteInt32LittleEndian(member, 199);
                    BinaryPrimitives.WriteInt32LittleEndian(member.AsSpan(4), 32);
                    payload.Slice(43 + i * 32, 32).CopyTo(member.AsSpan(8));
                    WriteRecord(c.Members[i], new KwdRecordImage(KwdRecordType.Things, member, 199))
                        .AsSpan(8).CopyTo(payload.Slice(43 + i * 32, 32));
                }
                break;
            case DeadBodyThing c when c.Tag == 202:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Z);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[12..], c.GoldHeld);
                payload[14] = c.CreatureId;
                payload[15] = c.PlayerId;
                break;
            case EffectGeneratorThing c when c.Tag == 203:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Unknown_0008);
                BinaryPrimitives.WriteInt32LittleEndian(payload[12..], c.Unknown_000C);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[16..], c.Unknown_0010);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[18..], c.Unknown_0012);
                KwdAdditionalRecords.WriteU16s(image, 8 + 20, 4, c.EffectIds);
                payload[28] = c.Frequency;
                payload[29] = c.GeneratorId;
                KwdAdditionalRecords.WriteRaw(image, 8 + 30, 6, c.Unknown_001E);
                break;
            case RoomThing c when c.Tag == 204:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Unknown_0008);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[12..], c.Unknown_000C);
                payload[14] = c.Direction;
                payload[15] = c.Unknown_000F;
                BinaryPrimitives.WriteUInt16LittleEndian(payload[16..], c.InitialHealth);
                payload[18] = c.RoomType;
                payload[19] = c.PlayerId;
                break;
            case CameraThing c when c.Tag == 205:
                BinaryPrimitives.WriteInt32LittleEndian(payload[0..], c.X);
                BinaryPrimitives.WriteInt32LittleEndian(payload[4..], c.Y);
                BinaryPrimitives.WriteInt32LittleEndian(payload[8..], c.Z);
                BinaryPrimitives.WriteInt32LittleEndian(payload[12..], c.MinClipX);
                BinaryPrimitives.WriteInt32LittleEndian(payload[16..], c.MinClipY);
                BinaryPrimitives.WriteInt32LittleEndian(payload[20..], c.MinClipZ);
                BinaryPrimitives.WriteInt32LittleEndian(payload[24..], c.MaxClipX);
                BinaryPrimitives.WriteInt32LittleEndian(payload[28..], c.MaxClipY);
                BinaryPrimitives.WriteInt32LittleEndian(payload[32..], c.MaxClipZ);
                BinaryPrimitives.WriteInt32LittleEndian(payload[36..], c.ViewDistance);
                BinaryPrimitives.WriteInt32LittleEndian(payload[40..], c.MinViewDistance);
                BinaryPrimitives.WriteInt32LittleEndian(payload[44..], c.MaxViewDistance);
                BinaryPrimitives.WriteInt32LittleEndian(payload[48..], c.Zoom);
                BinaryPrimitives.WriteInt32LittleEndian(payload[52..], c.MinZoom);
                BinaryPrimitives.WriteInt32LittleEndian(payload[56..], c.MaxZoom);
                BinaryPrimitives.WriteInt32LittleEndian(payload[60..], c.Lens);
                BinaryPrimitives.WriteInt32LittleEndian(payload[64..], c.MinLens);
                BinaryPrimitives.WriteInt32LittleEndian(payload[68..], c.MaxLens);
                BinaryPrimitives.WriteUInt32LittleEndian(payload[72..], c.Flags);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[76..], c.AngleYaw);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[78..], c.AngleRoll);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[80..], c.AnglePitch);
                BinaryPrimitives.WriteUInt16LittleEndian(payload[82..], c.CameraId);
                break;
            default: throw new InvalidDataException("THINGS record type does not match its payload tag.");
        }
        return image;
    }
}

