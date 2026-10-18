// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data;

/// <summary>The original v1.70 executable's indexed identity strings; see world-hover-text.md.</summary>
public sealed class CreatureNamePool
{
    private readonly string[] _names, _bloodGroups;
    public IReadOnlyList<string> Names => _names;
    public IReadOnlyList<string> BloodGroups => _bloodGroups;
    private CreatureNamePool(string[] names, string[] bloodGroups)
    { _names = names; _bloodGroups = bloodGroups; }
    public string Name(CreatureNameIndex index) => index.Value < _names.Length ? _names[index.Value] : "";
    public string BloodGroup(CreatureBloodGroupIndex index) => index.Value < _bloodGroups.Length ? _bloodGroups[index.Value] : "";

    public static CreatureNamePool Open(string executable) => Read(File.ReadAllBytes(executable));

    public static CreatureNamePool Read(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        void Require(int at, int size)
        {
            if (at < 0 || size < 0 || at > data.Length - size)
                throw new InvalidDataException("Truncated DK2 executable data.");
        }
        int U16(int at) { Require(at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2)); }
        uint U32(int at) { Require(at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4)); }
        if (data.Length < 64 || U16(0) != 0x5A4D) throw new InvalidDataException("Not a PE executable.");
        uint peOffset = U32(0x3C);
        if (peOffset > data.Length - 24) throw new InvalidDataException("Truncated PE header.");
        int pe = (int)peOffset;
        if (U32(pe) != 0x4550 || U16(pe + 24) != 0x10B)
            throw new InvalidDataException("Expected a 32-bit DK2 executable.");
        uint imageBase = U32(pe + 52);
        int sections = U16(pe + 6), table = pe + 24 + U16(pe + 20);
        Require(table, sections * 40);
        int Offset(uint address)
        {
            if (address < imageBase) throw new InvalidDataException("Identity pointer precedes the executable image.");
            uint rva = address - imageBase;
            for (int i = 0; i < sections; i++)
            {
                int section = table + i * 40;
                uint start = U32(section + 12), size = U32(section + 16);
                if (rva < start || rva - start >= size) continue;
                ulong offset = (ulong)U32(section + 20) + rva - start;
                if (offset < (ulong)data.Length) return (int)offset;
            }
            throw new InvalidDataException("Identity pointer is outside the executable's stored sections.");
        }
        string[] ReadPool(uint address, int count)
        {
            int pointers = Offset(address);
            Require(pointers, count * 4);
            var strings = new string[count];
            for (int i = 0; i < count; i++)
            {
                int start = Offset(U32(pointers + i * 4)), end = start;
                while (end + 1 < data.Length && end - start < 512 && U16(end) != 0) end += 2;
                if (end + 1 >= data.Length || end == start || end - start >= 512)
                    throw new InvalidDataException("Invalid DK2 identity string.");
                strings[i] = Encoding.Unicode.GetString(data, start, end - start);
            }
            return strings;
        }
        return new CreatureNamePool(ReadPool(0x0069A118, 500), ReadPool(0x0069A8E8, 7));
    }
}
