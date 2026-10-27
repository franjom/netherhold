// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class CampaignResultsTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void TheStatisticsRowsUseTheNativeLabelsAndFormats()
    {
        var block = new LevelStatisticsBlock
        {
            LevelWon = true, TimeTaken = 3 * 3600 + 7 * 60 + 9, GoldMined = 4500, ManaStored = -3,
            CreatureTraining = (2 << 12) | 0xCD, HeroesDestroyed = 6,
        };
        var profile = new CampaignProfile { LevelAttempts = Attempts(slot: 4, value: 2) };
        profile = CampaignProgression.RecordStatistics(profile, Keeper, block, new CampaignSlot(4), keeperRank: 60);
        CampaignResults results = CampaignResults.Build(profile, new CampaignSlot(4))!;

        Assert.Equal(new uint[] { 1516, 1410, 1508, 1413, 1509, 1510, 1511, 1411, 1512, 1513, 1514, 1429, 1515, 1636 },
            results.Cells.Select(c => c.LabelTextId));
        Assert.Equal(21u, results.Cells[0].ValueTextId);
        Assert.Equal("03 : 07", results.Cells[1].Value);       // hours and minutes once an hour has passed
        Assert.Equal("6", results.Cells[4].Value);
        Assert.Equal("-3", results.Cells[8].Value);            // %ld
        Assert.Equal("2.5", results.Cells[12].Value);          // 0xCD/4096 is .05, printed unpadded
        Assert.Equal("3", results.Cells[13].Value);            // attempts + 1 when won
        Assert.Equal(2898u, results.RankTitleTextId);          // 60: Deviant
        Assert.Equal(block.TotalEvilRating, results.Total);
        Assert.Equal(block.TotalEvilRating, profile.TotalEvilRating[3]);
        Assert.Equal(1u, profile.PlayerStatisticsValid[Keeper.Value]);
    }

    [Fact]
    public void UnderAnHourTheTimeIsMinutesAndSecondsAndNoBlockMeansNoRows()
    {
        var profile = CampaignProgression.RecordStatistics(new CampaignProfile(), Keeper,
            new LevelStatisticsBlock { TimeTaken = 125 }, new CampaignSlot(1), keeperRank: null);
        Assert.Equal("02 : 05", CampaignResults.Build(profile, new CampaignSlot(1))!.Cells[1].Value);
        Assert.Equal(22u, CampaignResults.Build(profile, new CampaignSlot(1))!.Cells[0].ValueTextId);
        Assert.Null(CampaignResults.Build(new CampaignProfile(), new CampaignSlot(1)));
    }

    [Fact]
    public void TheSummarySitsAtItsFixedBoxesWithTheOverallSumAndSpecialsOutsideASecretLevel()
    {
        var ratings = new uint[40];
        ratings[0] = 100; ratings[3] = 250; ratings[24] = 7; ratings[25] = 9000;   // slot 26 is a secret one
        var profile = new CampaignProfile { TotalEvilRating = ratings };
        profile = CampaignProgression.RecordStatistics(profile, Keeper,
            new LevelStatisticsBlock { SpecialsFound = 2, SpecialsMissed = 3 }, new CampaignSlot(4), keeperRank: 60);
        CampaignResults results = CampaignResults.Build(profile, new CampaignSlot(4))!;
        CampaignResultText[] summary = results.Summary().ToArray();

        Assert.Equal(new FrontEndRect(191 * 4, 110 * 4, 449 * 4, 370 * 4), summary[0].Rect);
        Assert.Equal((2898u, FrontEndTextColour.Rank), (summary[0].TextId, summary[0].Colour));
        Assert.Equal((1641u, 148f * 4), (summary[1].TextId, summary[1].Rect.Y));
        Assert.Equal((results.Total.ToString(), FrontEndTextColour.Red, 460f * 4), (summary[2].Text, summary[2].Colour, summary[2].Rect.X));
        Assert.Equal((1637u, FrontEndTextColour.Grey), (summary[3].TextId, summary[3].Colour));
        Assert.Equal((results.Total + 100 + 7).ToString(), summary[4].Text);      // slots 1-25 only
        Assert.Equal((1638u, "2 / 5"), (summary[5].TextId, summary[6].Text));
    }

    [Fact]
    public void ASecretLevelPrintsNoOverallRating()
    {
        var profile = CampaignProgression.RecordStatistics(new CampaignProfile(), Keeper,
            new LevelStatisticsBlock(), new CampaignSlot(26), keeperRank: 10);
        CampaignResults results = CampaignResults.Build(profile, new CampaignSlot(26))!;
        Assert.Null(results.Overall);
        Assert.DoesNotContain(results.Summary(), t => t.TextId == 1637);
    }

    [Fact]
    public void TheRowsSkipEnemyKeepersVanquishedStepTwentyFivePixelsAndClipToTheList()
    {
        var profile = CampaignProgression.RecordStatistics(new CampaignProfile(), Keeper,
            new LevelStatisticsBlock { LevelWon = true }, new CampaignSlot(1), keeperRank: null);
        CampaignResults results = CampaignResults.Build(profile, new CampaignSlot(1))!;
        var list = new FrontEndRect(760, 968, 1500, 512);                     // widget 180
        CampaignResultText[] rows = results.Rows(list, 0).ToArray();

        // Seven listed cells, 1508 hidden: six lines at 0, 25, ..., 125 pixels; the last cut to 3 pixels.
        Assert.Equal(new uint[] { 1516, 1410, 1413, 1509, 1510, 1511 }, rows.Where((_, i) => i % 2 == 0).Select(r => r.TextId));
        Assert.Equal(968 + 125 * 4, rows[10].Rect.Y);
        Assert.Equal(512 - 125 * 4, rows[10].Rect.Height);
        CampaignResultText value = rows[1];
        Assert.Equal((21u, FrontEndTextColour.Value, FrontEndTextAlignment.Right), (value.TextId, value.Colour, value.Alignment));
        Assert.Equal((760f + 1000, 500f), (value.Rect.X, value.Rect.Width));
        // The scroll range is the remake's: five whole lines fit, so the list stops at row 9.
        Assert.Equal(9, results.LastFirstRow(list));
        CampaignResultText last = results.Rows(list, 9).Last(r => r.Colour == FrontEndTextColour.Label);
        Assert.Equal((1636u, 968f + 100 * 4), (last.TextId, last.Rect.Y));
    }

    [Theory]
    [InlineData(0u, 2895u)]
    [InlineData(17u, 2895u)]
    [InlineData(18u, 2896u)]
    [InlineData(82u, 2899u)]
    [InlineData(83u, 2900u)]
    public void RankTitlesFollowTheNativeThresholds(uint rank, uint title) => Assert.Equal(title, KeeperRank.TitleTextId(rank));

    [Fact]
    public void TheRankSaturatesItsTermsAndCapsAtAHundred()
    {
        var empty = new LevelStatisticsBlock();
        // 40 − 1 (level −1 × 1.1111) − 10 − 10 − 10 − 10 + 10 (no losses) + 10 (fast) + 0 (no conversions) = 19.
        Assert.Equal(19u, KeeperRank.Compute(empty, nativeTurn: 1, ticksPerSecond: 4, averageTimeMinutes: 30, levelRating: 4096));
        var strong = new LevelStatisticsBlock
        {
            DungeonHeartsDestroyed = 4, CreatureTraining = 10 << 12, CreaturesCommanded = 1000, EnemyCreaturesKilled = 1000,
            RoomsTakenOver = 1000, ItemsManufactured = 1000, LevelWon = true, CreaturesConverted = 1000,
        };
        Assert.Equal(100u, KeeperRank.Compute(strong, 1, 4, 30, 4096 * 2));
    }

    private static uint[] Attempts(int slot, uint value)
    {
        var attempts = new uint[40]; attempts[slot - 1] = value; return attempts;
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(6, "6a")]
    [InlineData(7, "6b")]
    [InlineData(8, "7")]
    [InlineData(14, "11c")]
    [InlineData(15, "12")]
    [InlineData(19, "15b")]
    [InlineData(24, "20")]
    public void HiScoreLabelsNameTheLevelAndItsBranch(int slot, string label) =>
        Assert.Equal(label, HiScoreTable.Label(new CampaignSlot(slot), ""));

    [Fact]
    public void AQualifyingWinOpensNameEntryAndCommitSavesThenFliesToTheMap()
    {
        var frame = new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000);
        var session = new FrontEndSession(_ => new[] { frame, frame, frame }, FrontEndPage.CampaignResults);
        var profile = CampaignProgression.RecordResult(new CampaignProfile { LevelNumber = 2 }, new CampaignSlot(2), won: true);
        profile = CampaignProgression.RecordStatistics(profile, Keeper,
            new LevelStatisticsBlock { LevelWon = true, GoldMined = 700 }, new CampaignSlot(2), keeperRank: 10);
        var scores = Enumerable.Range(0, 10).Select(i => new HiScoreEntry((uint)(1000 - i * 100), "K" + i, "1")).ToArray();
        using var map = new CampaignMapController(session, profile, false,
            slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()), scores);
        IReadOnlyList<HiScoreEntry>? saved = null;
        map.HiScoresChanged += rows => saved = rows;
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromMilliseconds(10)); session.Update(TimeSpan.FromSeconds(2));

        Assert.True(map.AcceptResults());
        Assert.Equal(FrontEndPage.HighScores, session.Page);
        Assert.Equal(3, map.EntryRow);                         // 700 meets the fourth row's 700
        Assert.Equal(3, map.Profile.LevelNumber);
        Assert.True(map.TypeEntry('A')); Assert.True(map.TypeEntry('b')); Assert.True(map.EraseEntry());
        Assert.True(map.CommitEntry());
        Assert.Equal(new HiScoreEntry(700, "A", "2"), saved![3]);
        Assert.Equal("K3", saved[4].Name);
        Assert.Equal(10, saved.Count);
        Assert.Equal(FrontEndPath.BriefingToTable, session.Path);
    }

    /// <summary>During name entry page 18's accept finishes the entry (DK2's entry rewrites widget 47 to
    /// D0x68, 0x0053E320) instead of returning to Extras.</summary>
    [Fact]
    public void DuringNameEntryTheAcceptCommitsInsteadOfReturningToExtras()
    {
        var frame = new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000);
        var session = new FrontEndSession(_ => new[] { frame, frame, frame }, FrontEndPage.CampaignResults);
        var profile = CampaignProgression.RecordResult(new CampaignProfile { LevelNumber = 2 }, new CampaignSlot(2), won: true);
        profile = CampaignProgression.RecordStatistics(profile, Keeper,
            new LevelStatisticsBlock { LevelWon = true, GoldMined = 700 }, new CampaignSlot(2), keeperRank: 10);
        var scores = Enumerable.Range(0, 10).Select(i => new HiScoreEntry((uint)(1000 - i * 100), "K" + i, "1")).ToArray();
        using var map = new CampaignMapController(session, profile, false,
            slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()), scores);
        using var menu = new FrontEndMenuController(session, _ => new FrontEndRect(0, 0, 10, 10), map);
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromMilliseconds(10)); session.Update(TimeSpan.FromSeconds(2));
        Assert.True(map.AcceptResults());
        Assert.True(map.EntryRow >= 0);
        Assert.True(menu.Activate(new(47)));
        Assert.Equal(-1, map.EntryRow);
        Assert.Equal(FrontEndPath.BriefingToTable, session.Path);
        Assert.NotEqual(FrontEndPage.Extras, session.Page);
    }

    [Fact]
    public void ALossOrALowScoreGoesStraightBackToTheMap()
    {
        var scores = Enumerable.Repeat(new HiScoreEntry(5000, "K", "1"), 10).ToArray();
        Assert.False(HiScoreTable.Qualifies(scores, 4999));
        Assert.True(HiScoreTable.Qualifies(scores, 5000));
        Assert.Equal(-1, HiScoreTable.Position(scores, 10));
    }

    [Theory]
    [InlineData(1, 1, 0x589u)]
    [InlineData(6, 6, 0x592u)]
    [InlineData(7, 6, 0x592u)]
    [InlineData(14, 11, 0x599u)]
    [InlineData(19, 15, 0x588u)]
    [InlineData(23, 19, 0x58Eu)]
    public void AWonMapPlaysItsLevelsTitleCardAndCutscene(int slot, int level, uint title)
    {
        var statuses = new CampaignLevelStatus[40]; statuses[slot - 1] = CampaignLevelStatus.Won;
        var cutscene = CampaignCutscene.AfterLevel(new CampaignProfile { LevelNumber = (byte)slot, PlayerLevelStatus = statuses })!;
        Assert.Equal(level, CampaignCutscene.LevelNumber(new CampaignSlot(slot)));
        Assert.Equal((title, title + 1), (cutscene.TitleTextId, cutscene.SpeechId));
        Assert.Equal(new[] { $"Data/Movies/CutSceneLevel{level}.tgq" }, cutscene.Movies);
    }

    [Fact]
    public void LevelTwentyPlaysBothOutrosAndLossesOrSecretsPlayNothing()
    {
        var statuses = new CampaignLevelStatus[40]; statuses[23] = CampaignLevelStatus.Won; statuses[0] = CampaignLevelStatus.Open;
        var outro = CampaignCutscene.AfterLevel(new CampaignProfile { LevelNumber = 24, PlayerLevelStatus = statuses })!;
        Assert.Equal(0u, outro.TitleTextId);
        Assert.Equal(new[] { "Data/Movies/Outro.tgq", "Data/Movies/BullfrogOutro.tgq" }, outro.Movies);
        Assert.Null(CampaignCutscene.AfterLevel(new CampaignProfile { LevelNumber = 1, PlayerLevelStatus = statuses }));
        Assert.Null(CampaignCutscene.AfterLevel(new CampaignProfile { LevelNumber = 24, SecretLevelNumber = 2, PlayerLevelStatus = statuses }));
    }
}
