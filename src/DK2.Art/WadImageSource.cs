// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Wad;

namespace DK2.Art.Textures;

/// <summary>Install-backed images, decoded only in memory. Owns the supplied archive.</summary>
public sealed class WadImageSource : ITextureSource, IDisposable
{
    private readonly WadArchive _archive;
    private readonly Func<byte[], RgbaImage> _decodePng;

    public WadImageSource(WadArchive archive, Func<byte[], RgbaImage> decodePng)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _decodePng = decodePng ?? throw new ArgumentNullException(nameof(decodePng));
    }

    public IReadOnlyList<WadEntry> Entries => _archive.Entries;

    public string Resolve(string textureName) => _archive.TryGetEntry(textureName, out WadEntry entry)
        ? entry.Name : throw new FileNotFoundException($"WAD image '{textureName}' was not found.");

    public RgbaImage Read(string storedName) => Decode(Resolve(storedName), _archive.Extract(storedName));

    public RgbaImage Read(int entryIndex) => Decode(Entries[entryIndex].Name, _archive.Extract(entryIndex));

    private RgbaImage Decode(string name, byte[] data)
    {
        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".png": return _decodePng(data);
            case ".bmp": return BmpImageReader.Read(data);
            case ".444": return LoadingScreenImageReader.Read(data);
            default: throw new NotSupportedException($"Unsupported WAD image format: {name}");
        }
    }

    public void Dispose() => _archive.Dispose();
}
