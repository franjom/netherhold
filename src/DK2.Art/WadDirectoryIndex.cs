// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Wad;

namespace DK2.Art.Textures;

/// <summary>Restores directory runs for image access without changing the archive's legacy bare-name lookup.</summary>
public sealed class WadDirectoryIndex
{
    private readonly Dictionary<string, int> _entries = new(StringComparer.OrdinalIgnoreCase);
    public WadDirectoryIndex(IReadOnlyList<WadEntry> entries)
    {
        string directory = "";
        for (int i = 0; i < entries.Count; i++)
        {
            string name = entries[i].Name.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) { directory = name.Substring(0, slash + 1); name = name.Substring(slash + 1); }
            _entries[directory + name] = i;
        }
    }
    public int Find(string path) => _entries.TryGetValue(path.Replace('\\', '/'), out int index)
        ? index : throw new FileNotFoundException("WAD image was not found in its directory: " + path);
}
