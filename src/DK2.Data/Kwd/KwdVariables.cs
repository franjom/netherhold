// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Reads VARIABLES(230) cells and overlays typed edits on explicit cell images.
/// Cells are 16 bytes: an id and a 12-byte payload (research/kwd-variables.md).</summary>
public static class KwdVariables
{
    private const int Stride = 16, IdOffset = 0, PayloadOffset = 4;
    private const int SecondWord = 4, ThirdWord = 8, AvailabilityPlayer = 2;
    private const int Type1 = 0, Id1 = 1, Type2 = 2, Id2 = 3, Type3 = 4, Id3 = 5,
        RewardType = 6, SpeechId = 7;

    public static GlobalVariables Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new GlobalVariables();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Variables) continue;
            if (chunk.ItemCount < 0 || chunk.Content.Length != (long)chunk.ItemCount * Stride)
                throw new InvalidDataException($"VARIABLES content is {chunk.Content.Length} bytes for {chunk.ItemCount} records.");
            for (int i = 0; i < chunk.ItemCount; i++)
                switch (ParseRecord(chunk.Content.AsSpan(i * Stride, Stride)))
                {
                    case CreaturePoolEntry entry: result.CreaturePool.Add(entry); break;
                    case AvailabilityEntry entry: result.Availabilities.Add(entry); break;
                    case SacrificeEntry entry: result.Sacrifices.Add(entry); break;
                    case CreatureStatEntry entry: result.CreatureStats.Add(entry); break;
                    case ResearchEntry entry: result.Research.Add(entry); break;
                    case CreatureFieldOverrideEntry entry: result.CreatureFieldOverrides.Add(entry); break;
                    // Table projection deliberately folds repeats; cell writing does not.
                    case MiscVariable entry: result.Misc[entry.Id] = entry; break;
                }
        }
        return result;
    }

    public static KwdVariableEntry ParseRecord(ReadOnlySpan<byte> record)
    {
        if (record.Length != Stride) throw new InvalidDataException("VARIABLES requires a 16-byte cell.");
        var id = (VariableTypeId)BinaryPrimitives.ReadInt32LittleEndian(record[IdOffset..]);
        ReadOnlySpan<byte> p = record[PayloadOffset..];
        return id switch
        {
            VariableTypeId.CreaturePool => new CreaturePoolEntry(I32(p), I32(p[SecondWord..]), I32(p[ThirdWord..])),
            VariableTypeId.Availability => new AvailabilityEntry(U16(p), U16(p[AvailabilityPlayer..]),
                I32(p[SecondWord..]), I32(p[ThirdWord..])),
            VariableTypeId.SacrificesId => new SacrificeEntry(p[Type1], p[Id1], p[Type2], p[Id2],
                p[Type3], p[Id3], p[RewardType], p[SpeechId], I32(p[ThirdWord..])),
            VariableTypeId.CreatureStatsId or VariableTypeId.CreatureFirstPersonId => new CreatureStatEntry(
                I32(p), I32(p[SecondWord..]), I32(p[ThirdWord..]), id == VariableTypeId.CreatureFirstPersonId),
            VariableTypeId.Research => new ResearchEntry(I32(p), I32(p[SecondWord..]), I32(p[ThirdWord..])),
            VariableTypeId.CreatureFieldOverride => new CreatureFieldOverrideEntry(I32(p), I32(p[SecondWord..]), I32(p[ThirdWord..])),
            _ => new MiscVariable(id, I32(p), I32(p[SecondWord..]), I32(p[ThirdWord..]))
        };
    }

    /// <summary>Overlays one cell; plain duplicate ids and source order remain the caller's images.
    /// Changing payload shape requires a matching explicit template.</summary>
    public static byte[] WriteRecord(KwdVariableEntry entry, KwdRecordImage template)
    {
        byte[] image = KwdOverlay.Image(template, KwdRecordType.Variables, Stride);
        if (entry is null || ParseRecord(image).GetType() != entry.GetType())
            throw new InvalidDataException("VARIABLES entry must match the explicit template's payload shape.");
        Span<byte> p = image.AsSpan(PayloadOffset);
        switch (entry)
        {
            case CreaturePoolEntry row:
                WriteWords(p, row.CreatureId, row.Value, row.PlayerId); break;
            case AvailabilityEntry row:
                BinaryPrimitives.WriteUInt16LittleEndian(p, row.Kind);
                BinaryPrimitives.WriteUInt16LittleEndian(p[AvailabilityPlayer..], row.PlayerId);
                BinaryPrimitives.WriteInt32LittleEndian(p[SecondWord..], row.TypeId);
                BinaryPrimitives.WriteInt32LittleEndian(p[ThirdWord..], row.Value); break;
            case SacrificeEntry row:
                p[Type1] = row.Type1; p[Id1] = row.Id1; p[Type2] = row.Type2; p[Id2] = row.Id2;
                p[Type3] = row.Type3; p[Id3] = row.Id3; p[RewardType] = row.RewardType; p[SpeechId] = row.SpeechId;
                BinaryPrimitives.WriteInt32LittleEndian(p[ThirdWord..], row.RewardValue); break;
            case CreatureStatEntry row:
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(IdOffset), (int)(row.FirstPerson
                    ? VariableTypeId.CreatureFirstPersonId : VariableTypeId.CreatureStatsId));
                WriteWords(p, row.StatId, row.Value, row.Level); break;
            case ResearchEntry row:
                WriteWords(p, row.Kind, row.TypeId, row.Cost); break;
            case CreatureFieldOverrideEntry row:
                WriteWords(p, row.CreatureId, row.Selector, row.Value); break;
            case MiscVariable row:
                BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(IdOffset), (int)row.Id);
                if (ParseRecord(image) is not MiscVariable)
                    throw new InvalidDataException("A plain VARIABLES cell cannot select a structured payload id.");
                WriteWords(p, row.Value, row.Unk1, row.Unk2); break;
            default: throw new InvalidDataException("Unsupported VARIABLES entry type.");
        }
        return image;
    }

    private static int I32(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadInt32LittleEndian(p);
    private static ushort U16(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt16LittleEndian(p);
    private static void WriteWords(Span<byte> p, int first, int second, int third)
    {
        BinaryPrimitives.WriteInt32LittleEndian(p, first);
        BinaryPrimitives.WriteInt32LittleEndian(p[SecondWord..], second);
        BinaryPrimitives.WriteInt32LittleEndian(p[ThirdWord..], third);
    }
}
