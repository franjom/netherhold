// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

public static partial class KwdLevel
{
    public static byte[] WriteRecord(LevelDef row, KwdChunk template)
    {
        if (template.Type != KwdRecordType.Level) throw new InvalidDataException("An explicit LEVEL chunk template is required.");
        LevelDef original = Parse([template])!;
        byte[] image = template.Content.ToArray();
        int metadata = original.Unknown_6403.Length + BaseMetadataSize;
        WriteUtf16(image, 0, 64, row.Name, original.Name);
        WriteUtf16(image, 128, 1024, row.Description, original.Description);
        WriteUtf16(image, 2176, 64, row.Author, original.Author);
        WriteUtf16(image, 2304, 64, row.Email, original.Email);
        WriteUtf16(image, 2432, 1024, row.Information, original.Information);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(4480), row.TriggerId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(4482), row.TicksPerSecond);
        KwdAdditionalRecords.WriteRaw(image, 4484, 520, row.Unknown_1184);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25484), row.Flags);
        KwdOverlay.Ascii(image, 25486, 32, row.SoundCategory, original.SoundCategory, trimmed: true);
        image[25518] = row.TalismanPieces;
        KwdAdditionalRecords.WriteRaw(image, 25519, 4, row.PreviousRewards);
        KwdAdditionalRecords.WriteRaw(image, 25523, 4, row.NextRewards);
        image[25527] = row.SoundTrack;
        image[25528] = row.TextTableId;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25529), row.TitleTextId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25531), row.PlotTextId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25533), row.DebriefTextId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25535), row.ObjectiveTextId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25537), row.Unknown_63B9);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25539), row.SubObjective1TextId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25541), row.SubObjective2TextId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25543), row.SubObjective3TextId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(25545), row.SpecialLevelIndex);
        KwdAdditionalRecords.WriteRaw(image, 25547, 8, row.IntroductionOverrideObjects);
        KwdAdditionalRecords.WriteU16s(image, 25555, 8, row.IntroductionOverrideTextIds);
        KwdOverlay.Ascii(image, 25571, 32, row.TerrainPath, original.TerrainPath, trimmed: true);
        KwdAdditionalRecords.WriteRaw(image, BaseMetadataSize, metadata - BaseMetadataSize, row.Unknown_6403);
        if (row.Messages.Count != 512 || row.Paths.Count != original.Paths.Count || row.UnknownPathTail.Count != original.UnknownPathTail.Count)
            throw new InvalidDataException("LEVEL overlay requires unchanged message, path and tail counts.");
        for (int i = 0; i < 512; i++)
            WriteUtf16(image, 5004 + i * 40, 20, row.Messages[i], original.Messages[i]);
        int at = metadata + 8;
        for (int i = 0; i < row.Paths.Count; i++, at += PathSize)
            WritePathRecord(row.Paths[i], new KwdRecordImage(KwdRecordType.Level, image.AsSpan(at, PathSize).ToArray())).CopyTo(image, at);
        for (int i = 0; i < row.UnknownPathTail.Count; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at + i * 4), row.UnknownPathTail[i]);
        return image;
    }

    public static byte[] WritePathRecord(KwdLevelFilePath row, KwdRecordImage template)
    {
        byte[] image = KwdOverlay.Image(template, KwdRecordType.Level, PathSize);
        BinaryPrimitives.WriteInt32LittleEndian(image, (int)row.RecordType);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(4), row.Unknown_0004);
        KwdOverlay.Ascii(image, 8, 64, row.Path, KwdAdditionalRecords.Ascii(image, 8, 64), trimmed: true);
        return image;
    }

    private static void WriteUtf16(byte[] image, int at, int characters, string value, string original)
    {
        if (value == original) return;
        if (value is null || value.Length > characters || value.Trim() != value || value.Contains('\0'))
            throw new InvalidDataException($"Text at +0x{at:X} must round-trip in {characters} UTF-16 code units.");
        byte[] encoded;
        try { encoded = new UnicodeEncoding(false, false, true).GetBytes(value); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Text contains an unpaired UTF-16 surrogate.", exception); }
        encoded.CopyTo(image, at);
        if (value.Length < characters) { image[at + encoded.Length] = 0; image[at + encoded.Length + 1] = 0; }
    }
}
