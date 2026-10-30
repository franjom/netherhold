// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;

namespace DK2.Data.Media;

public readonly record struct SoundArchiveId(int Value);
/// <summary>Archive id/name and the uninterpreted11-byte record preceding the name (flag byte at+9).</summary>
public sealed record SoundArchive(SoundArchiveId Id, string Name, byte[] Unknown0x00);

/// <summary>Sound archive names. Layout STRONGLY INFERRED; front-end-assets.md section 5.3.</summary>
public sealed class BankMap
{
    private BankMap(IReadOnlyList<SoundArchive> archives) => Archives = archives;
    public IReadOnlyList<SoundArchive> Archives { get; }

    public static BankMap Read(ReadOnlySpan<byte> data)
    {
        using var reader = SoundMapReader.Open(data, 0xE9612C01, 0xA00009B4);
        int count = SoundMapReader.Count(reader, 11);
        var records = new byte[count][];
        for (int i = 0; i < count; i++) records[i] = SoundMapReader.Bytes(reader, 11);
        var archives = new SoundArchive[count];
        for (int i = 0; i < count; i++)
        {
            int length = SoundMapReader.Count(reader, 1);
            string name = Encoding.ASCII.GetString(SoundMapReader.Bytes(reader, length)).TrimEnd('\0', ' ');
            archives[i] = new SoundArchive(new SoundArchiveId(i + 1), name, records[i]);
        }
        SoundMapReader.End(reader);
        return new BankMap(archives);
    }
}

internal static class SoundMapReader
{
    internal static BinaryReader Open(ReadOnlySpan<byte> data, uint first, uint third)
    {
        if (data.Length < 28) throw new InvalidDataException("Sound map header is truncated.");
        var reader = new BinaryReader(new MemoryStream(data.ToArray(), false));
        if (reader.ReadUInt32() != first || reader.ReadUInt32() != 0x11D231D0
            || reader.ReadUInt32() != third || reader.ReadUInt32() != 0x03F293C9)
        {
            reader.Dispose();
            throw new InvalidDataException("Sound map signature is invalid.");
        }
        reader.BaseStream.Position = 24;
        return reader;
    }

    internal static int Count(BinaryReader reader, int width)
    {
        Require(reader, 4);
        uint count = reader.ReadUInt32();
        if (count > (reader.BaseStream.Length - reader.BaseStream.Position) / width)
            throw new InvalidDataException("Sound map count exceeds its payload.");
        return checked((int)count);
    }

    internal static void Require(BinaryReader reader, int bytes)
    {
        if (bytes < 0 || bytes > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Sound map record is truncated.");
    }

    internal static byte[] Bytes(BinaryReader reader, int count)
    {
        Require(reader, count);
        return reader.ReadBytes(count);
    }

    internal static void End(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Sound map has trailing data.");
    }
}
