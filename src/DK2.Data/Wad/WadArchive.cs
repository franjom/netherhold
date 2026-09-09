// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;

namespace DK2.Data.Wad;

/// <summary>Reader for Dungeon Keeper 2 WAD archives ("DWFB" version 2).
/// <para>Header: the signature <c>DWFB</c> at 0, a version always 2, 64 zero bytes, the entry
/// count at <c>0x48</c>, then the name block's absolute offset and size. Entries start at
/// <c>0x58</c>, <b>40 bytes each</b>, carrying an absolute name offset and size, a data offset, a
/// stored size, a compression type (0 or 4) and an uncompressed size. The full field table,
/// including the four unknown dwords, is in docs/research/wad-format.md.</para>
/// <para>CONFIRMED against all five WADs of the reference install.</para></summary>
public sealed class WadArchive : IDisposable
{
    private const uint Signature = 0x42465744; // "DWFB" little-endian
    private const int HeaderCountOffset = 0x48;
    private const int EntrySize = 40; // 9 dwords: unk, nameOff, nameSize, dataOff, storedSize, type, uncompSize, unk[3]

    private readonly FileStream _stream;
    private readonly Dictionary<string, WadEntry> _byName;

    public IReadOnlyList<WadEntry> Entries { get; }
    public string FilePath { get; }

    private WadArchive(FileStream stream, string filePath, List<WadEntry> entries)
    {
        _stream = stream;
        FilePath = filePath;
        Entries = entries;
        _byName = new Dictionary<string, WadEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (WadEntry e in entries)
            _byName[e.Name] = e;
    }

    public static WadArchive Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> head = stackalloc byte[8];
            fs.ReadExactly(head);
            uint sig = BinaryPrimitives.ReadUInt32LittleEndian(head);
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
            if (sig != Signature)
                throw new InvalidDataException($"Not a DK2 WAD (bad signature) : {path}");
            if (version != 2)
                throw new InvalidDataException($"Unsupported WAD version {version}: {path}");

            fs.Position = HeaderCountOffset;
            Span<byte> dir = stackalloc byte[16];
            fs.ReadExactly(dir);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(dir);
            uint namesOffset = BinaryPrimitives.ReadUInt32LittleEndian(dir[4..]);
            uint namesSize = BinaryPrimitives.ReadUInt32LittleEndian(dir[8..]);

            byte[] entryBlock = new byte[count * EntrySize];
            fs.ReadExactly(entryBlock);

            byte[] nameBlock = new byte[namesSize];
            fs.Position = namesOffset;
            fs.ReadExactly(nameBlock);

            var entries = new List<WadEntry>((int)count);
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> e = entryBlock.AsSpan(i * EntrySize, EntrySize);
                uint nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
                uint nameSize = BinaryPrimitives.ReadUInt32LittleEndian(e[8..]);
                uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(e[12..]);
                uint storedSize = BinaryPrimitives.ReadUInt32LittleEndian(e[16..]);
                uint compression = BinaryPrimitives.ReadUInt32LittleEndian(e[20..]);
                uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(e[24..]);

                bool compressed = compression switch
                {
                    0 => false,
                    4 => true,
                    _ => throw new InvalidDataException(
                        $"Unknown WAD compression type {compression} (entry {i}): {path}"),
                };

                // Name offsets are absolute file offsets into the name block.
                int nameStart = (int)(nameOffset - namesOffset);
                string name = Encoding.ASCII
                    .GetString(nameBlock, nameStart, (int)nameSize)
                    .TrimEnd('\0');

                entries.Add(new WadEntry
                {
                    Name = name,
                    DataOffset = dataOffset,
                    StoredSize = storedSize,
                    IsCompressed = compressed,
                    UncompressedSize = compressed ? uncompressedSize : storedSize,
                });
            }

            return new WadArchive(fs, path, entries);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public bool TryGetEntry(string name, out WadEntry entry) =>
        _byName.TryGetValue(name, out entry!);

    /// <summary>Reads and (if needed) decompresses one entry.</summary>
    public byte[] Extract(WadEntry entry)
    {
        byte[] stored = new byte[entry.StoredSize];
        _stream.Position = entry.DataOffset;
        _stream.ReadExactly(stored);

        return entry.IsCompressed
            ? WadDecompressor.Decompress(stored, (int)entry.UncompressedSize)
            : stored;
    }

    public byte[] Extract(string name) =>
        TryGetEntry(name, out WadEntry e)
            ? Extract(e)
            : throw new FileNotFoundException($"Entry '{name}' not found in {FilePath}");

    public void Dispose() => _stream.Dispose();
}
