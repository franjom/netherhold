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
