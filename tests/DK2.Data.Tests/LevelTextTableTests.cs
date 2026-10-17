// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Text;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class LevelTextTableTests
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "LEVEL1.str")]
    [InlineData(6, "LEVEL6A.str")]
    [InlineData(24, "LEVEL20.str")]
    [InlineData(26, "MYPETDUNGEON.str")]
    [InlineData(34, "DEMO3.str")]
    [InlineData(35, "MYPETDUNGEON.str")]
    [InlineData(40, "MYPETDUNGEON.str")]
    [InlineData(41, null)]
    public void TheIdNamesTheFileOfTheBinarysTable(int id, string? file) =>
        Assert.Equal(file, LevelTextTable.FileName(id));

    [InstallTheory]
    [InlineData("level1")]
    [InlineData("level2")]
    [InlineData("Level6a")]
    [InlineData("level11B")]
    [InlineData("Level20")]
    public void EachCampaignLevelNamesItsOwnTable(string level)
    {
        LevelDef def = GameDatabaseLoader.LoadCampaignLevel(Dk2InstallGate.Path, level)!;
        Assert.Equal(level.ToUpperInvariant() + ".STR", LevelTextTable.FileName(def.TextTableId)?.ToUpperInvariant());
    }

    [InstallFact]
    public void LevelOnesOpeningSpeechesAreTheHeartAndLordAntonius()
    {
        LevelDef def = GameDatabaseLoader.LoadCampaignLevel(Dk2InstallGate.Path, "level1")!;
        var table = Dk2StringTable.Open(Path.Combine(
            Dk2InstallGate.Path, "Data", "Text", "Default", LevelTextTable.FileName(def.TextTableId)!));

        // Trigger action 24 queues SpeechId; the subtitle is string SpeechId - 1.
        Assert.StartsWith("Behold, your Dungeon Heart.", table.Entries[3 - 1].Value);
        Assert.StartsWith("Here walks the goodly Lord Antonius", table.Entries[2 - 1].Value);
    }
}
