// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Wad;

namespace DK2.Convert.Tests;

public class WadDirectoryIndexTests
{
    [Fact]
    public void DirectoryRunsKeepRootIconsAndBothObjectiveShotsReachable()
    {
        var names = new[] { "Cancel.png", "Confirm.png", @"Obj_Shots\Level1-0.png", "Level1-1.png", @"Buttons\Tick.png", "Confirm.png", "Cancel.png" };
        var entries = names.Select(n => new WadEntry { Name = n, DataOffset = 0, StoredSize = 0, UncompressedSize = 0, IsCompressed = false }).ToArray();
        var index = new WadDirectoryIndex(entries);
        Assert.Equal(0, index.Find("Cancel.png")); Assert.Equal(1, index.Find("Confirm.png"));
        Assert.Equal(3, index.Find("obj_shots/level1-1.PNG"));
        Assert.Equal(5, index.Find("Buttons/Confirm.png"));
        Assert.Equal(6, index.Find("Buttons/Cancel.png"));
        Assert.Throws<FileNotFoundException>(() => { index.Find("Level1-1.png"); });
    }
}
