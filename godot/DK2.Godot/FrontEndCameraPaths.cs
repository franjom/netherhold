// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Camera;
using DK2.Data.Model;
using DK2.Data.Wad;
using DK2.Presentation;

namespace DK2.GodotAdapter;

/// <summary>EnginePath%d.kcs out of Paths.WAD, read once per path: the front-end session times its pages by these
/// paths even with no 3D station drawn behind the menu. Mirrors Unity's <c>DungeonRunner.CameraPathLoader</c>.</summary>
public static class FrontEndCameraPaths
{
    public static Func<FrontEndPath, IReadOnlyList<KcsFrame>?> Loader(string install)
    {
        string wad = System.IO.Path.Combine(install, "Data", "Paths.WAD");
        var cache = new Dictionary<FrontEndPath, IReadOnlyList<KcsFrame>?>();
        return path =>
        {
            if (cache.TryGetValue(path, out IReadOnlyList<KcsFrame>? frames)) return frames;
            frames = null;
            if (File.Exists(wad))
            {
                using WadArchive archive = WadArchive.Open(wad);
                string name = "EnginePath" + ((int)path).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".kcs";
                foreach (WadEntry entry in archive.Entries)
                {
                    if (!string.Equals(System.IO.Path.GetFileName(entry.Name), name, StringComparison.OrdinalIgnoreCase)) continue;
                    frames = KcsCameraPath.Read(archive.Extract(entry), entry.Name).Frames;
                    break;
                }
            }
            cache[path] = frames;
            return frames;
        };
    }
}
