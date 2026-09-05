// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Wad;

namespace DK2.Convert.Textures;

public static class TextureExtractor
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static void WritePng(RgbaImage image, string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, image.ToPng());
    }

    /// <summary>
    /// Copies the already-PNG texture entries in a DK2 WAD after applying the
    /// existing WAD decompressor. Returns the paths written.
    /// </summary>
    public static IReadOnlyList<string> ExtractPngEntries(WadArchive archive, string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(archive);
        string root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        var written = new List<string>();
        foreach (WadEntry entry in archive.Entries.Where(entry =>
            entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] png = archive.Extract(entry);
            if (!png.AsSpan().StartsWith(PngSignature))
                throw new InvalidDataException($"WAD entry '{entry.Name}' is named PNG but has no PNG signature.");
            string relative = entry.Name.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"WAD entry '{entry.Name}' escapes the output directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, png);
            written.Add(path);
        }
        return written;
    }
}
