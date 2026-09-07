// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Wad;
using DK2.TestSupport;

namespace DK2.Data.Tests;

/// <summary>
/// Integration tests against the reference install's real WAD archives.
/// Auto-skip when no installation is present.
/// </summary>
public class WadArchiveTests
{
    private static string DataDir()
    {
        string dir = Dk2InstallGate.Path;
        Assert.True(File.Exists(Path.Combine(dir, "DKII.EXE")), "the install has no DKII.EXE");
        return Path.Combine(dir, "Data");
    }

    public static TheoryData<string> AllWads => new()
    {
        "EngineTextures.WAD", "FrontEnd.WAD", "Meshes.WAD", "Paths.WAD", "Sprite.WAD",
    };

    [InstallTheory]
    [MemberData(nameof(AllWads))]
    public void EveryWad_Parses_WithSaneEntries(string wadName)
    {
        string data = DataDir();

        using var wad = WadArchive.Open(Path.Combine(data, wadName));

        Assert.NotEmpty(wad.Entries);
        foreach (WadEntry e in wad.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name), "entry with empty name");
            // Names should be printable ASCII (paths with backslashes).
            Assert.All(e.Name, c => Assert.InRange(c, ' ', '~'));
            Assert.True(e.StoredSize > 0, $"zero-size entry {e.Name}");
        }
    }

    [InstallTheory]
    [MemberData(nameof(AllWads))]
    public void EveryEntry_Extracts_ToDeclaredSize(string wadName)
    {
        string data = DataDir();

        using var wad = WadArchive.Open(Path.Combine(data, wadName));

        // Extract EVERY entry — the decompressor throws on any size mismatch,
        // so this exercises the full corpus (~2600 files across the archives).
        foreach (WadEntry e in wad.Entries)
        {
            byte[] bytes = wad.Extract(e);
            Assert.Equal((int)e.UncompressedSize, bytes.Length);
        }
    }

    [InstallFact]
    public void MeshesWad_Entries_AreKmfFiles()
    {
        string data = DataDir();

        using var wad = WadArchive.Open(Path.Combine(data, "Meshes.WAD"));
        WadEntry kmf = wad.Entries.First(e => e.Name.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase));
        byte[] bytes = wad.Extract(kmf);

        // KMF files begin with the 4CC "KMSH" (CONFIRMED: OpenKeeper/dk2-tools docs;
        // verified here against real extracted data).
        Assert.True(bytes.Length > 4);
        Assert.Equal("KMSH", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
