// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Campaign;

/// <summary>The runtime HiScores.dat format: ten 132-byte rows; not the WAD's default table.</summary>
public static class HiScoresFile
{
    public static IReadOnlyList<HiScoreEntry> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != 1320) throw new InvalidDataException("Expected ten 132-byte high-score rows.");
        var entries = new HiScoreEntry[10];
        for (int i = 0; i < entries.Length; i++)
        {
            ReadOnlySpan<byte> row = data.Slice(i * 132, 132);
            entries[i] = new HiScoreEntry(BinaryPrimitives.ReadUInt32LittleEndian(row),
                Text(row.Slice(4, 64)), Text(row.Slice(68, 64)));
        }
        return System.Array.AsReadOnly(entries);
    }

    public static byte[] Write(IReadOnlyList<HiScoreEntry> entries)
    {
        if (entries is null || entries.Count != 10) throw new ArgumentException("Expected ten high-score rows.", nameof(entries));
        byte[] data = new byte[1320];
        for (int i = 0; i < entries.Count; i++)
        {
            HiScoreEntry entry = entries[i] ?? throw new ArgumentException("Null high-score row.", nameof(entries));
            Span<byte> row = data.AsSpan(i * 132, 132);
            BinaryPrimitives.WriteUInt32LittleEndian(row, entry.EvilRating);
            WriteText(entry.Name, row.Slice(4, 64)); WriteText(entry.LevelLabel, row.Slice(68, 64));
        }
        return data;
    }

    public static void Save(string path, IReadOnlyList<HiScoreEntry> entries) => ProfileFileStorage.Save(path, Write(entries));

    private static string Text(ReadOnlySpan<byte> field)
    {
        int length = 0;
        while (length < field.Length && BinaryPrimitives.ReadUInt16LittleEndian(field[length..]) != 0) length += 2;
        return Encoding.Unicode.GetString(field[..length]);
    }

    private static void WriteText(string value, Span<byte> field)
    {
        if (value is null || value.Length > 31 || value.IndexOf('\0') >= 0)
            throw new ArgumentException("High-score text must fit 31 UTF-16 units and a terminator.");
        Encoding.Unicode.GetBytes(value.AsSpan(), field);
    }
}
