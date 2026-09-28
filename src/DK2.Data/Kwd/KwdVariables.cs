// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Decodes VARIABLES(230) chunks into a <see cref="GlobalVariables"/> table.
///
/// Record layout: 16 bytes each — u32 variable id + a 12-byte payload whose
/// shape depends on the id (CONFIRMED: 621 records * 16 bytes == contentSize
/// of the reference GlobalVariables.kwd; docs/research/kwd-variables.md).
/// </summary>
public static class KwdVariables
{
    public static GlobalVariables Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new GlobalVariables();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Variables) continue;

            ReadOnlySpan<byte> data = chunk.Content;
            if (data.Length != chunk.ItemCount * 16)
                throw new InvalidDataException(
                    $"VARIABLES content is {data.Length} bytes for {chunk.ItemCount} records (expected {chunk.ItemCount * 16}).");

            for (int i = 0; i < chunk.ItemCount; i++)
            {
                ReadOnlySpan<byte> r = data.Slice(i * 16, 16);
                var id = (VariableTypeId)BinaryPrimitives.ReadInt32LittleEndian(r);
                ReadOnlySpan<byte> p = r[4..];

                switch (id)
                {
                    case VariableTypeId.CreaturePool:
                        result.CreaturePool.Add(new CreaturePoolEntry(
                            BinaryPrimitives.ReadInt32LittleEndian(p),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..])));
                        break;

                    case VariableTypeId.Availability:
                        result.Availabilities.Add(new AvailabilityEntry(
                            BinaryPrimitives.ReadUInt16LittleEndian(p),
                            BinaryPrimitives.ReadUInt16LittleEndian(p[2..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..])));
                        break;

                    case VariableTypeId.SacrificesId:
                        result.Sacrifices.Add(new SacrificeEntry(
                            p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7],
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..])));
                        break;

                    case VariableTypeId.CreatureStatsId:
                    case VariableTypeId.CreatureFirstPersonId:
                        result.CreatureStats.Add(new CreatureStatEntry(
                            BinaryPrimitives.ReadInt32LittleEndian(p),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..]),
                            FirstPerson: id == VariableTypeId.CreatureFirstPersonId));
                        break;

                    case VariableTypeId.Research:
                        result.Research.Add(new ResearchEntry(
                            BinaryPrimitives.ReadInt32LittleEndian(p),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..])));
                        break;

                    default:
                        // Plain value. Last occurrence wins (files can carry repeats).
                        result.Misc[id] = new MiscVariable(
                            id,
                            BinaryPrimitives.ReadInt32LittleEndian(p),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..]));
                        break;
                }
            }
        }
        return result;
    }
}
