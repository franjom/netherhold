// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Camera;
using DK2.Data.Model;
using DK2.Data.Wad;

namespace DK2.Presentation;

/// <summary>The install's camera paths: <c>EnginePath%d.kcs</c> out of <c>Data/Paths.WAD</c>, read once per path. The
/// front-end session times its pages by them and scripted views fly them, in every engine.</summary>
public static class EngineCameraPaths
{
    /// <summary>A loader over the install; a path the install lacks (or no Paths.WAD) loads as null, remembered too.</summary>
    public static Func<int, IReadOnlyList<KcsFrame>?> Loader(string install)
    {
        if (install == null)
        {
            throw new ArgumentNullException(nameof(install));
        }

        string wad = Path.Combine(install, "Data", "Paths.WAD");
        var cache = new Dictionary<int, IReadOnlyList<KcsFrame>?>();
        return pathId =>
        {
            if (cache.TryGetValue(pathId, out IReadOnlyList<KcsFrame>? frames))
            {
                return frames;
            }

            frames = null;
            if (File.Exists(wad))
            {
                using var archive = WadArchive.Open(wad);
                string name = "EnginePath" + pathId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".kcs";
                foreach (WadEntry entry in archive.Entries)
                {
                    if (!string.Equals(Path.GetFileName(entry.Name), name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    frames = KcsCameraPath.Read(archive.Extract(entry), entry.Name).Frames;
                    break;
                }
            }
            cache[pathId] = frames;
            return frames;
        };
    }
}
