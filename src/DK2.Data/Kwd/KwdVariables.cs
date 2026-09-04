// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
