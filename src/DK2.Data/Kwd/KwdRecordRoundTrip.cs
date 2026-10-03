// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>One exact on-disk record image, independent of its typed projection.</summary>
public sealed class KwdRecordImage : KwdRoundTripRecord
{
    public KwdRecordImage(KwdRecordType type, byte[] bytes, int? tag = null) : base(bytes)
    {
        Type = type;
        Tag = tag;
    }

    public KwdRecordType Type { get; }
    public int? Tag { get; }
}

/// <summary>
/// Splits every KWD/KLD chunk shape into exact record images. Typed parsers own
/// semantics; this owns M1's stronger byte promise for old and new readers alike.
/// </summary>
public static class KwdRecordRoundTrip
{
    /// <summary>Joins images without changing the template's count or record shape.</summary>
    public static byte[] Join(KwdChunk template, IReadOnlyList<KwdRecordImage> records)
    {
        if (template.Type == KwdRecordType.Globals)
            throw new InvalidDataException("GLOBALS has no established standalone record layout; its known form is a LEVEL path row.");
        bool tagged = KwdTaggedRecords.IsTagged(template.Type);
        int count = template.Type == KwdRecordType.Level ? 1
            : template.Type == KwdRecordType.Map ? template.Content.Length / 4 : template.ItemCount;
        if (records.Count != count)
            throw new InvalidDataException($"{template.Type}: joining requires {count} images, received {records.Count}.");
        int stride = tagged ? 0 : template.Type == KwdRecordType.Level
            ? template.Content.Length : FixedStride(template);
        using var stream = new MemoryStream();
        for (int i = 0; i < records.Count; i++)
        {
            KwdRecordImage record = records[i];
            if (record.Type != template.Type)
                throw new InvalidDataException($"{template.Type}: image {i} has type {record.Type}.");
            byte[] bytes = record.ToBytes();
            if (tagged)
            {
                if (bytes.Length < 8 || record.Tag != BinaryPrimitives.ReadInt32LittleEndian(bytes)
                    || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != bytes.Length - 8)
                    throw new InvalidDataException($"{template.Type}: image {i} has an inconsistent tag or payload size.");
            }
            else if (record.Tag is not null || bytes.Length != stride)
                throw new InvalidDataException($"{template.Type}: image {i} must be an untagged {stride}-byte record.");
            stream.Write(bytes, 0, bytes.Length);
        }
        byte[] joined = stream.ToArray();
        if (tagged) _ = KwdTaggedRecords.Split(template with { Content = joined });
        else _ = Split(template with { Content = joined });
        return joined;
    }
    private static readonly IReadOnlyDictionary<KwdRecordType, int> Strides =
        new Dictionary<KwdRecordType, int>
        {
            [KwdRecordType.Map] = 4,
            [KwdRecordType.Terrain] = 552,
            [KwdRecordType.Rooms] = 1055,
            [KwdRecordType.Traps] = 579,
            [KwdRecordType.Doors] = 616,
            [KwdRecordType.KeeperSpells] = 406,
            [KwdRecordType.CreatureSpells] = 266,
            [KwdRecordType.Creatures] = 5449,
            [KwdRecordType.Players] = 205,
            [KwdRecordType.Variables] = 16,
            [KwdRecordType.Objects] = 894,
            [KwdRecordType.EffectElements] = 182,
            [KwdRecordType.Shots] = 239,
            [KwdRecordType.Effects] = 246,
        };

    public static List<KwdRecordImage> Split(KwdChunk chunk)
    {
        if (chunk.Type == KwdRecordType.Globals)
            throw new InvalidDataException("GLOBALS has no standalone record in the shipped corpus; it is a LEVEL path discriminator.");
        if (chunk.Type == KwdRecordType.Level)
            return [new KwdRecordImage(chunk.Type, chunk.Content)];
        if (KwdTaggedRecords.IsTagged(chunk.Type))
        {
            var result = new List<KwdRecordImage>(chunk.ItemCount);
            foreach (KwdTaggedRecords.TaggedRecord record in KwdTaggedRecords.Split(chunk))
            {
                byte[] bytes = new byte[KwdTaggedRecords.RecordHeaderSize + record.Payload.Length];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, record.Tag);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), record.Payload.Length);
                record.Payload.Span.CopyTo(bytes.AsSpan(8));
                result.Add(new KwdRecordImage(chunk.Type, bytes, record.Tag));
            }
            return result;
        }

        int stride = FixedStride(chunk);
        int count = chunk.Type == KwdRecordType.Map ? chunk.Content.Length / stride : chunk.ItemCount;
        if (count < 0 || chunk.Content.Length != (long)count * stride)
            throw new InvalidDataException($"{chunk.Type}: {chunk.Content.Length} bytes do not divide into {count} records of {stride}.");
        return Enumerable.Range(0, count)
            .Select(i => new KwdRecordImage(chunk.Type, chunk.Content.AsSpan(i * stride, stride).ToArray()))
            .ToList();
    }

    private static int FixedStride(KwdChunk chunk)
    {
        if (!Strides.TryGetValue(chunk.Type, out int stride))
            throw new InvalidDataException($"No record-image layout for {chunk.Type}.");
        if (chunk.Type == KwdRecordType.Creatures && chunk.ItemCount > 0
            && chunk.Content.Length == (long)chunk.ItemCount * 5537)
            stride = 5537;
        return stride;
    }
}
