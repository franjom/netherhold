// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;

namespace DK2.Art.Textures;

public sealed record EngineTextureEntry(
    string Name, int Width, int Height, int StoredSize,
    int ScaledWidth, int ScaledHeight, bool HasAlpha, long DataOffset);

/// <summary>Catalogues and decodes model textures from EngineTextures.dir/.dat.</summary>
public sealed class EngineTextureArchive : ITextureSource, IDisposable
{
    private const uint DirectoryMagic = 0x43484354; // TCHC
    private static readonly IReadOnlyDictionary<string, string> ShippedNameCorrections =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Goblin2"] = "GoblinFront",
            ["Goblinbak"] = "GoblinBack",
        };
    private readonly FileStream _data;
    private readonly Dictionary<string, EngineTextureEntry> _byName;

    public uint Version { get; }
    public IReadOnlyList<EngineTextureEntry> Entries { get; }

    private EngineTextureArchive(FileStream data, uint version,
        List<EngineTextureEntry> entries)
    {
        _data = data;
        Version = version;
        Entries = entries;
        _byName = entries.ToDictionary(entry => entry.Name,
            StringComparer.OrdinalIgnoreCase);
    }

    public static EngineTextureArchive OpenInstall(string installDirectory) => Open(
        Path.Combine(installDirectory, "DK2TextureCache", "EngineTextures.dir"),
        Path.Combine(installDirectory, "DK2TextureCache", "EngineTextures.dat"));

    public static EngineTextureArchive Open(string directoryPath, string dataPath)
    {
        byte[] directory = File.ReadAllBytes(directoryPath);
        if (directory.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(directory) != DirectoryMagic)
            throw new InvalidDataException($"Not a DK2 engine texture directory: {directoryPath}");
        uint directorySize = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(4));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(8));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(12));
        if (directorySize > directory.Length || directorySize < 16 || count > int.MaxValue)
            throw new InvalidDataException("Invalid engine texture directory size or entry count.");

        var data = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var entries = new List<EngineTextureEntry>((int)count);
            int position = 16;
            Span<byte> header = stackalloc byte[20];
            for (uint i = 0; i < count; i++)
            {
                int start = position;
                while (position < directory.Length && directory[position] != 0) position++;
                if (position == directory.Length || position + 5 > directory.Length)
                    throw new InvalidDataException($"Invalid engine texture directory entry {i}.");
                string name = Encoding.ASCII.GetString(directory, start, position - start);
                position++;
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(position));
                position += 4;
                if (offset > data.Length - header.Length)
                    throw new InvalidDataException($"Texture '{name}' points outside the data file.");
                data.Position = offset;
                data.ReadExactly(header);
                uint storedWithHeader = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
                if (storedWithHeader < 8 || storedWithHeader - 8 > data.Length - data.Position)
                    throw new InvalidDataException($"Texture '{name}' has an invalid stored size.");
                entries.Add(new EngineTextureEntry(name,
                    PositiveInt(BinaryPrimitives.ReadUInt32LittleEndian(header), "width", name),
                    PositiveInt(BinaryPrimitives.ReadUInt32LittleEndian(header[4..]), "height", name),
                    checked((int)storedWithHeader - 8),
                    BinaryPrimitives.ReadUInt16LittleEndian(header[12..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(header[14..]),
                    (BinaryPrimitives.ReadUInt32LittleEndian(header[16..]) >> 7) != 0,
                    checked((long)offset + 20)));
            }
            return new EngineTextureArchive(data, version, entries);
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }

    public bool TryGetEntry(string name, out EngineTextureEntry entry) =>
        _byName.TryGetValue(name, out entry!);

    public EngineTextureEntry FindBest(string textureName)
    {
        if (ShippedNameCorrections.TryGetValue(textureName, out string? corrected))
            textureName = corrected;
        if (TryGetEntry(textureName, out EngineTextureEntry exact)) return exact;
        if (TryGetEntry(textureName + "MM0", out EngineTextureEntry highest)) return highest;
        throw new FileNotFoundException($"Engine texture '{textureName}' was not found.");
    }

    public RgbaImage Decode(EngineTextureEntry entry)
    {
        byte[] compressed = new byte[entry.StoredSize];
        // One stream, positioned per read: the read is locked, the decode is not.
        lock (_data)
        {
            _data.Position = entry.DataOffset;
            _data.ReadExactly(compressed);
        }
        return new RgbaImage(entry.Width, entry.Height,
            Dk2TextureDecoder.Decode(compressed, entry.Width, entry.Height, entry.HasAlpha));
    }

    public RgbaImage Decode(string textureName) => Decode(FindBest(textureName));
    string ITextureSource.Resolve(string textureName) => FindBest(textureName).Name;
    RgbaImage ITextureSource.Read(string storedName) => Decode(_byName[storedName]);
    public void Dispose() => _data.Dispose();

    private static int PositiveInt(uint value, string field, string name)
    {
        if (value == 0 || value > int.MaxValue)
            throw new InvalidDataException($"Texture '{name}' has invalid {field} {value}.");
        return (int)value;
    }
}

/// <summary>A decoded texture: <see cref="Width"/> by <see cref="Height"/> pixels, four bytes
/// (R, G, B, A) each, rows top first.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);
