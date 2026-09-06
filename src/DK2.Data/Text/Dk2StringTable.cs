// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace DK2.Data.Text;

/// <summary>One localised string and its global resource id.</summary>
public sealed record Dk2StringEntry(uint Id, string Value, uint StoredOffset);

/// <summary>
/// Reads a DK2 <c>BFST</c> string table using its sibling <c>MBToUni.dat</c>
/// character map.
/// </summary>
public sealed class Dk2StringTable
{
    private const int HeaderSize = 12;
    private readonly IReadOnlyDictionary<uint, string> byId;

    private Dk2StringTable(uint fileId, List<Dk2StringEntry> entries)
    {
        FileId = fileId;
        Entries = new ReadOnlyCollection<Dk2StringEntry>(entries);
        byId = new ReadOnlyDictionary<uint, string>(
            entries.ToDictionary(entry => entry.Id, entry => entry.Value));
    }

    /// <summary>The table resource id stored at header +4; it is not a string-id base.</summary>
    public uint FileId { get; }
    public IReadOnlyList<Dk2StringEntry> Entries { get; }

    public static Dk2StringTable Open(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("String table path must not be empty.", nameof(path));
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null)
            throw new ArgumentException($"String table path has no directory: '{path}'.", nameof(path));

        string codePagePath = Path.Combine(directory, "MBToUni.dat");
        return Read(File.ReadAllBytes(path), File.ReadAllBytes(codePagePath), path);
    }

    /// <summary>Reads in-memory data. Exposed so format tests need no proprietary fixture.</summary>
    public static Dk2StringTable Read(
        ReadOnlySpan<byte> tableData,
        ReadOnlySpan<byte> codePageData,
        string sourceName = "<memory>")
    {
        if (tableData.Length < HeaderSize)
            throw Invalid(sourceName, "file is shorter than the 12-byte BFST header");
        if (!tableData[..4].SequenceEqual("BFST"u8))
            throw Invalid(sourceName, "expected BFST magic");

        uint fileId = BinaryPrimitives.ReadUInt32LittleEndian(tableData[4..]);
        uint countValue = BinaryPrimitives.ReadUInt32LittleEndian(tableData[8..]);
        if (countValue > int.MaxValue)
            throw Invalid(sourceName, $"entry count {countValue} is too large");
        int count = (int)countValue;

        long tableEndLong = HeaderSize + (long)count * sizeof(uint);
        if (tableEndLong > tableData.Length)
            throw Invalid(sourceName, $"offset table ends beyond the file at {tableEndLong}");
        int tableEnd = (int)tableEndLong;

        Dk2CodePage codePage = Dk2CodePage.Read(codePageData, sourceName);
        var storedOffsets = new uint[count];
        for (int i = 0; i < count; i++)
        {
            uint storedOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                tableData.Slice(HeaderSize + i * sizeof(uint), sizeof(uint)));
            long absoluteOffset = HeaderSize + (long)storedOffset;
            if (absoluteOffset < tableEnd)
                throw Invalid(sourceName,
                    $"entry {i} starts at {absoluteOffset}, inside the offset table ending at {tableEnd}");
            if (absoluteOffset > tableData.Length)
                throw Invalid(sourceName,
                    $"entry {i} starts beyond the file at {absoluteOffset}");
            storedOffsets[i] = storedOffset;
        }

        uint[] physicalStarts = storedOffsets.Distinct().OrderBy(offset => offset).ToArray();
        var nextStart = new Dictionary<uint, int>(physicalStarts.Length);
        for (int i = 0; i < physicalStarts.Length; i++)
        {
            long end = i + 1 < physicalStarts.Length
                ? HeaderSize + (long)physicalStarts[i + 1]
                : tableData.Length;
            nextStart.Add(physicalStarts[i], checked((int)end));
        }

        var entries = new List<Dk2StringEntry>(count);
        for (int i = 0; i < count; i++)
        {
            uint storedOffset = storedOffsets[i];
            int start = checked((int)(HeaderSize + (long)storedOffset));
            string value = DecodeEntry(
                tableData.Slice(start, nextStart[storedOffset] - start), codePage, sourceName, i);
            entries.Add(new Dk2StringEntry((uint)i, value, storedOffset));
        }

        return new Dk2StringTable(fileId, entries);
    }

    public bool TryResolve(uint id, out string value) => byId.TryGetValue(id, out value!);

    public string Resolve(uint id) =>
        TryResolve(id, out string value)
            ? value
            : throw new KeyNotFoundException(
                $"String id {id} is outside this table's range " +
                $"0..{(Entries.Count == 0 ? 0 : Entries.Count - 1)}.");

    private static string DecodeEntry(
        ReadOnlySpan<byte> data,
        Dk2CodePage codePage,
        string sourceName,
        int entryIndex)
    {
        var result = new StringBuilder(data.Length);
        int position = 0;
        while (position + sizeof(uint) <= data.Length)
        {
            uint chunkHeader = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            position += sizeof(uint);
            int chunkType = (int)(chunkHeader & 0xff);
            int chunkLength = checked((int)(chunkHeader >> 8));

            switch (chunkType)
            {
                case 0:
                    if (chunkLength != 0)
                        throw Invalid(sourceName,
                            $"entry {entryIndex} has an end chunk with length {chunkLength}");
                    return result.ToString();

                case 1:
                    if (chunkLength > data.Length - position)
                        throw Invalid(sourceName,
                            $"entry {entryIndex} string chunk exceeds its entry boundary");
                    DecodeCharacters(data.Slice(position, chunkLength), codePage, result,
                        sourceName, entryIndex);
                    position += chunkLength;
                    break;

                case 2:
                    // The stored value is zero-based; the text's substitution tokens are one-based.
                    result.Append('%').Append(chunkLength + 1);
                    break;

                default:
                    throw Invalid(sourceName,
                        $"entry {entryIndex} has unknown chunk type {chunkType}");
            }

            position = checked((position + 3) & ~3);
        }

        throw Invalid(sourceName, $"entry {entryIndex} has no end chunk");
    }

    private static void DecodeCharacters(
        ReadOnlySpan<byte> encoded,
        Dk2CodePage codePage,
        StringBuilder result,
        string sourceName,
        int entryIndex)
    {
        int position = 0;
        while (position < encoded.Length)
        {
            int index = encoded[position++] - 1;
            if (index < 0)
                throw Invalid(sourceName, $"entry {entryIndex} contains character code zero");

            if (index >= codePage.MultiByteThreshold)
            {
                if (position >= encoded.Length)
                    throw Invalid(sourceName,
                        $"entry {entryIndex} ends midway through a two-byte character code");
                index = checked(index * 255 - codePage.MultiByteThreshold * 254
                    + encoded[position++] - 1);
            }

            result.Append(codePage.CharacterAt(index, sourceName, entryIndex));
        }
    }

    private static InvalidDataException Invalid(string sourceName, string detail) =>
        new($"{sourceName}: {detail}.");

    private sealed class Dk2CodePage
    {
        private Dk2CodePage(int multiByteThreshold, char[] characters)
        {
            MultiByteThreshold = multiByteThreshold;
            Characters = characters;
        }

        public int MultiByteThreshold { get; }
        private char[] Characters { get; }

        public static Dk2CodePage Read(ReadOnlySpan<byte> data, string sourceName)
        {
            const int headerSize = 8;
            if (data.Length < headerSize)
                throw Invalid(sourceName, "MBToUni.dat is shorter than its 8-byte header");
            if (!data[..4].SequenceEqual("BFMU"u8))
                throw Invalid(sourceName, "expected BFMU magic in MBToUni.dat");
            if ((data.Length - headerSize) % sizeof(ushort) != 0)
                throw Invalid(sourceName, "MBToUni.dat has a partial UTF-16LE character");

            int threshold = 255 - data[4];
            var characters = new char[(data.Length - headerSize) / sizeof(ushort)];
            for (int i = 0; i < characters.Length; i++)
            {
                characters[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(
                    data.Slice(headerSize + i * sizeof(ushort), sizeof(ushort)));
            }
            return new Dk2CodePage(threshold, characters);
        }

        public char CharacterAt(int index, string sourceName, int entryIndex)
        {
            if ((uint)index >= (uint)Characters.Length)
                throw Invalid(sourceName,
                    $"entry {entryIndex} references missing code-page character {index}");
            return Characters[index];
        }
    }
}
