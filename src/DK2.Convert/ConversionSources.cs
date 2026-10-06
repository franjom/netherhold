// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Convert.Textures;
using DK2.Data.Wad;

namespace DK2.Convert;

/// <summary>
/// Where the converter reads original KMF bytes from, by entry. Production reads
/// Meshes.WAD (<see cref="WadMeshSource"/>); tests supply entries in memory.
/// </summary>
public interface IMeshSource
{
    IReadOnlyList<string> EntryNames { get; }
    bool TryFind(string name, out int index);
    byte[] Read(int index);
}

/// <summary>
/// Where the converter reads original textures from. <see cref="Resolve"/> maps a
/// material's texture name to the stored name, throwing
/// <see cref="FileNotFoundException"/> when none exists; <see cref="Read"/> decodes
/// a stored name. Production reads <see cref="EngineTextureArchive"/>.
/// </summary>
public interface ITextureSource
{
    string Resolve(string textureName);
    RgbaImage Read(string storedName);
}

/// <summary>Meshes.WAD as an <see cref="IMeshSource"/>, with the archive's own lookup.</summary>
public sealed class WadMeshSource : IMeshSource, IDisposable
{
    private readonly WadArchive _archive;
    private readonly Dictionary<string, int> _byName;

    public WadMeshSource(WadArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        _archive = archive;
        EntryNames = archive.Entries.Select(entry => entry.Name).ToArray();
        _byName = new Dictionary<string, int>(EntryNames.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < EntryNames.Count; i++)
            _byName[EntryNames[i]] = i;
    }

    public static WadMeshSource OpenInstall(string installDirectory) =>
        new(WadArchive.Open(Path.Combine(installDirectory, "Data", "Meshes.WAD")));

    public IReadOnlyList<string> EntryNames { get; }

    public bool TryFind(string name, out int index) => _byName.TryGetValue(name, out index);

    public byte[] Read(int index) => _archive.Extract(_archive.Entries[index]);

    public void Dispose() => _archive.Dispose();
}
