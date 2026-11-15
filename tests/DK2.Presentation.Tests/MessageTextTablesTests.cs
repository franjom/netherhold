// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The message tabs' string tables and player names, shared by the Unity and Godot adapters (migration plan
/// step 1b). The install pins are Level1's shipped strings: a wrong table, entry offset or briefing file changes them.</summary>
public sealed class MessageTextTablesTests
{
    [InstallFact]
    public void AnUnreadableTableWarnsOnceAndResolvesNothingFromIt()
    {
        string install = Path.Combine(Path.GetTempPath(), "dk2-text-" + Guid.NewGuid().ToString("N"));
        string text = Path.Combine(install, "Data", "Text", "Default");
        Directory.CreateDirectory(text);
        try
        {
            foreach (string file in new[] { "Text.str", "MBToUni.dat" })
                File.Copy(Path.Combine(Dk2InstallGate.Path, "Data", "Text", "Default", file), Path.Combine(text, file));
            File.WriteAllBytes(Path.Combine(text, "Speech.str"), [1, 2, 3]);
            using var sources = new InstallHudSources(install);
            var warnings = new List<string>();
            var tables = new MessageTextTables(sources, 0, warnings.Add);
            Assert.Single(warnings);
            Assert.StartsWith("Speech.str unavailable", warnings[0]);
            Assert.Null(tables.Resolve(-10001));
            Assert.Null(tables.Briefing(1));
        }
        finally { Directory.Delete(install, recursive: true); }
    }

    [InstallFact]
    public void AReferencePicksSpeechTheLevelTableOrTextStrByItsRange()
    {
        CampaignLevelData level = CampaignLevelData.Load(Dk2InstallGate.Path, "Level1");
        using var sources = new InstallHudSources(Dk2InstallGate.Path);
        var warnings = new List<string>();
        var tables = new MessageTextTables(sources, level.Database.Level!.TextTableId, warnings.Add);

        // Below -10000: Speech.str entry -10000 - ref.
        Assert.Equal("An Imp has become unhappy because it has been slapped", tables.Resolve(-10000 - 589));
        Assert.Equal("You have 20 minutes remaining", tables.Resolve(-10001));
        // -10000 .. -1: LEVEL1.str entry -1 - ref.
        Assert.Equal("Behold, your Dungeon Heart.  Protect it well, for if it is destroyed you will be banished forever "
            + "from this land.", tables.Resolve(-3));
        Assert.Null(tables.Resolve(-10000));
        // Positive: Text.str; zero is no string.
        Assert.Equal("Main Objective", tables.Resolve(1408));
        Assert.Equal("Sub Objective", tables.Resolve(1409));
        Assert.Null(tables.Resolve(0));
        Assert.Empty(warnings);
    }

    [InstallFact]
    public void TheBriefingLinesAreTheLevelsBriefingTable()
    {
        CampaignLevelData level = CampaignLevelData.Load(Dk2InstallGate.Path, "Level1");
        using var sources = new InstallHudSources(Dk2InstallGate.Path);
        var tables = new MessageTextTables(sources, level.Database.Level!.TextTableId);

        Assert.Equal("Assault and kill Lord Antonius", tables.Briefing(2));
        Assert.Equal("Build a 5x3 Lair", tables.Briefing(3));
        Assert.Equal("Build a 3x3 Hatchery", tables.Briefing(4));
        Assert.Equal("", tables.Briefing(5));
        Assert.Null(tables.Briefing(1000));
    }

    [InstallFact]
    public void ALevelWithNoTextTableResolvesOnlySpeechAndTextStr()
    {
        using var sources = new InstallHudSources(Dk2InstallGate.Path);
        var tables = new MessageTextTables(sources, textTableId: 0);
        Assert.Null(tables.Resolve(-3));
        Assert.Null(tables.Briefing(2));
        Assert.Equal("Main Objective", tables.Resolve(1408));
        Assert.Equal("You have 20 minutes remaining", tables.Resolve(-10001));
    }

    [Fact]
    public void TablesNeedTheHudSources() =>
        Assert.Throws<ArgumentNullException>(() => new MessageTextTables(null!, 1));

    [Fact]
    public void ANamelessPlayerIsGoodNeutralOrKeeperByItsId()
    {
        Func<byte, string> names = PlayerNames.From(new[] { Player(3, "Mortis"), Player(4, ""), Player(1, "") });
        Assert.Equal("Mortis", names(3));
        Assert.Equal("Keeper 4", names(4));
        Assert.Equal("Keeper 5", names(5));
        Assert.Equal("Good Player", names(PlayerIds.Good));
        Assert.Equal("Neutral Player", names(PlayerIds.Neutral));
        Assert.Equal("Keeper 3", PlayerNames.From(null)(3));
    }

    [InstallFact]
    public void Level1sPlayersKeepTheirRecordNames()
    {
        Func<byte, string> names = PlayerNames.From(CampaignLevelData.Load(Dk2InstallGate.Path, "Level1").Players);
        Assert.Equal("Good Player", names(1));
        Assert.Equal("Neutral Player", names(2));
        Assert.Equal("Player", names(3));
        Assert.Equal("Keeper 4", names(4));
    }

    private static PlayerDef Player(byte id, string name) => new()
    {
        PlayerId = id, Name = name, StartingGold = 0, HasAiFlag = false, TriggerId = 0, CameraX = 0, CameraY = 0,
        Ai = default!,
    };
}
