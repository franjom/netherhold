// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Wad;

namespace DK2.Art;

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
        if (archive is null) throw new ArgumentNullException(nameof(archive));
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

/// <summary>Finding a KWD art resource's KMF in a mesh source.</summary>
public static class KmfLookup
{
    /// <summary>
    /// Finds a KMF entry; <paramref name="entryName"/> is the stored name when found, else the
    /// name looked up. A collection member (the name followed by a digit, first by name) answers
    /// only when <paramref name="allowCollectionMember"/> is set.
    /// </summary>
    public static bool TryFind(this IMeshSource meshes, string sourceName, bool allowCollectionMember,
        out int index, out string entryName)
    {
        if (meshes is null) throw new ArgumentNullException(nameof(meshes));
        entryName = sourceName.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)
            ? sourceName : sourceName + ".kmf";
        if (meshes.TryFind(entryName, out index))
        {
            entryName = meshes.EntryNames[index];
            return true;
        }
        if (allowCollectionMember)
        {
            IReadOnlyList<string> names = meshes.EntryNames;
            index = Enumerable.Range(0, names.Count)
                .Where(candidate => names[candidate].EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)
                    && names[candidate].StartsWith(sourceName, StringComparison.OrdinalIgnoreCase)
                    && names[candidate].Length > sourceName.Length
                    && char.IsDigit(names[candidate][sourceName.Length]))
                .OrderBy(candidate => names[candidate], StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty(-1)
                .First();
            if (index >= 0)
            {
                entryName = names[index];
                return true;
            }
        }
        index = -1;
        return false;
    }
}
