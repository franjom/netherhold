// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

public static partial class KwdAdditionalRecords
{
    public static byte[] WriteCreatureSpellRecord(CreatureSpellDef row, KwdRecordImage template)
    {
        byte[] image = KwdOverlay.Image(template, KwdRecordType.CreatureSpells, CreatureSpellSize);
        KwdOverlay.Ascii(image, 0, 32, row.Name, Ascii(image, 0, 32), trimmed: true);
        WriteArt(image, 32, row.EditorIcon);
        WriteArt(image, 116, row.GuiIcon);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(200), row.ShotData1);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(204), row.ShotData2);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(208), row.Range);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(212), row.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(216), row.CombatPoints);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(218), row.SoundEvent);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(220), row.NameStringId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(222), row.TooltipStringId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(224), row.GeneralDescriptionStringId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(226), row.StrengthStringId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(228), row.WeaknessStringId);
        image[230] = row.CreatureSpellId;
        image[231] = row.ShotTypeId;
        image[232] = row.AlternativeShotId;
        image[233] = row.AlternativeRoomId;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(234), row.RechargeTime);
        image[238] = row.AlternativeShot;
        WriteRaw(image, 239, 27, row.Unknown_00EF);
        return image;
    }

    public static byte[] WriteShotRecord(ShotDef row, KwdRecordImage template)
    {
        byte[] image = KwdOverlay.Image(template, KwdRecordType.Shots, ShotSize);
        KwdOverlay.Ascii(image, 0, 32, row.Name, Ascii(image, 0, 32), trimmed: true);
        WriteArt(image, 32, row.MeshResource);
        WriteLight(image, 116, row.Light);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(140), row.AirFriction);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(144), row.Mass);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(148), row.Speed);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(152), row.Unknown_0098);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(156), row.Unknown_009C);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(160), row.ShotProcessFlags);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(164), row.Radius);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(168), row.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(172), row.GeneralEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(174), row.CreationEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(176), row.DeathEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(178), row.TimedEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(180), row.HitSolidEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(182), row.HitLavaEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(184), row.HitWaterEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(186), row.HitThingEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(188), row.Health);
        image[190] = row.ShotId;
        image[191] = row.DeathShotId;
        image[192] = row.TimedDelay;
        image[193] = row.HitSolidShotId;
        image[194] = row.HitLavaShotId;
        image[195] = row.HitWaterShotId;
        image[196] = row.HitThingShotId;
        image[197] = row.DamageType;
        image[198] = row.CollideType;
        image[199] = row.ProcessType;
        image[200] = row.AttackCategory;
        KwdOverlay.Ascii(image, 201, 32, row.SoundCategory, Ascii(image, 201, 32), trimmed: true);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(233), row.Threat);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(235), row.BurnDuration);
        return image;
    }

    private static void WriteLight(byte[] image, int offset, KwdLight row)
    {
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset), row.X);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset + 4), row.Y);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset + 8), row.Z);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset + 12), row.Radius);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset + 16), row.Flags);
        image[offset + 20] = row.Red; image[offset + 21] = row.Green;
        image[offset + 22] = row.Blue; image[offset + 23] = row.Alpha;
    }

    private static void WriteArt(byte[] image, int offset, KwdArtResource row)
    {
        KwdOverlay.Ascii(image, offset, 64, row.Name, Art(image, offset).Name, trimmed: true);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset + 64), row.Flags);
        WriteRaw(image, offset + 68, 12, row.Unknown_0044);
        image[offset + 80] = row.Type; image[offset + 81] = row.Unknown_0051;
        image[offset + 82] = row.Unknown_0052; image[offset + 83] = row.Unknown_0053;
    }

    private static void WriteRaw(byte[] image, int offset, int width, ReadOnlyMemory<byte> value)
    {
        if (value.Length != width)
            throw new InvalidDataException($"Raw field at +0x{offset:X} requires exactly {width} bytes.");
        value.Span.CopyTo(image.AsSpan(offset, width));
    }
}
