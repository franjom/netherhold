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

    public static byte[] WriteEffectRecord(EffectDef row, KwdRecordImage template)
    {
        byte[] image = KwdOverlay.Image(template, KwdRecordType.Effects, EffectSize);
        KwdOverlay.Ascii(image, 0, 32, row.Name, Ascii(image, 0, 32), trimmed: true);
        WriteArt(image, 32, row.ArtResource);
        WriteLight(image, 116, row.Light);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(140), row.Mass);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(144), row.AirFriction);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(148), row.Elasticity);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(152), row.Radius);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(156), row.MinSpeedXy);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(160), row.MaxSpeedXy);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(164), row.MinSpeedYz);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(168), row.MaxSpeedYz);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(172), row.MinScale);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(176), row.MaxScale);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(180), row.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(184), row.EffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(186), row.MinHp);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(188), row.MaxHp);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(190), row.FadeDuration);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(192), row.NextEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(194), row.DeathEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(196), row.HitSolidEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(198), row.HitWaterEffectId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(200), row.HitLavaEffectId);
        WriteU16s(image, 202, 8, row.GenerateIds);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(218), row.OuterOriginRange);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(220), row.LowerHeightLimit);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(222), row.UpperHeightLimit);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(224), row.OrientationRange);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(226), row.SpriteSpinRateRange);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(228), row.WhirlpoolRate);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(230), row.DirectionalSpread);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(232), row.CircularPathRate);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(234), row.InnerOriginRange);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(236), row.GenerateRandomness);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(238), row.Unknown_00EE);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(240), row.Unknown_00F0);
        image[242] = row.GenerationType;
        image[243] = row.ElementsPerTurn;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(244), row.Unknown_00F4);
        return image;
    }

    public static byte[] WriteEffectElementRecord(EffectElementDef row, KwdRecordImage template)
    {
        byte[] image = KwdOverlay.Image(template, KwdRecordType.EffectElements, EffectElementSize);
        KwdOverlay.Ascii(image, 0, 32, row.Name, Ascii(image, 0, 32), trimmed: true);
        WriteArt(image, 32, row.ArtResource);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(116), row.Mass);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(120), row.AirFriction);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(124), row.Elasticity);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(128), row.MinSpeedXy);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(132), row.MaxSpeedXy);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(136), row.MinSpeedYz);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(140), row.MaxSpeedYz);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(144), row.MinScale);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(148), row.MaxScale);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(152), row.ScaleRatio);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(156), row.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(160), row.EffectElementId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(162), row.MinHp);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(164), row.MaxHp);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(166), row.DeathElementId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(168), row.HitSolidElementId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(170), row.HitWaterElementId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(172), row.HitLavaElementId);
        image[174] = row.Red;
        image[175] = row.Green;
        image[176] = row.Blue;
        image[177] = row.RandomColorIndex;
        image[178] = row.TableColorIndex;
        image[179] = row.FadePercentage;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(180), row.NextEffectId);
        return image;
    }

    internal static void WriteU16s(byte[] image, int offset, int count, IReadOnlyList<ushort> values)
    {
        if (values is null || values.Count != count)
            throw new InvalidDataException($"Field at +0x{offset:X} requires exactly {count} ushort slots.");
        for (int i = 0; i < count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset + i * 2), values[i]);
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

    internal static void WriteRaw(byte[] image, int offset, int width, ReadOnlyMemory<byte> value)
    {
        if (value.Length != width)
            throw new InvalidDataException($"Raw field at +0x{offset:X} requires exactly {width} bytes.");
        value.Span.CopyTo(image.AsSpan(offset, width));
    }
}
