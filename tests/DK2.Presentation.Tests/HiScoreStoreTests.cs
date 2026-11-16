// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class HiScoreStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dk2-hiscores-").FullName;

    private string Own => Path.Combine(_root, "user", "HiScores.dat");
    private string Install => Path.Combine(_root, "install");
    private string Original => Path.Combine(Install, "Data", "Settings", "HiScores.dat");

    private static HiScoreEntry[] Rows(string name) =>
        Enumerable.Range(0, 10).Select(i => new HiScoreEntry((uint)(1000 - i * 100), name + i, "1")).ToArray();

    private static void Write(string path, IReadOnlyList<HiScoreEntry> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, HiScoresFile.Write(rows));
    }

    [Fact]
    public void TheInstallsTableIsTheOneUnderItsSettingsFolder()
    {
        var store = new HiScoreStore(Own, Install);
        Assert.Equal(Own, store.Path);
        Assert.Equal(Original, store.InstallPath);
    }

    [Fact]
    public void OurOwnTableWinsOverTheInstalls()
    {
        Write(Own, Rows("Own")); Write(Original, Rows("Original"));
        Assert.Equal(Rows("Own"), new HiScoreStore(Own, Install).Load());
    }

    [Fact]
    public void WithoutOurOwnTableTheInstallsIsReadAndNeitherIsWritten()
    {
        Write(Original, Rows("Original"));
        byte[] original = File.ReadAllBytes(Original);
        Assert.Equal(Rows("Original"), new HiScoreStore(Own, Install).Load());
        Assert.False(File.Exists(Own));
        Assert.Equal(original, File.ReadAllBytes(Original));
    }

    [Fact]
    public void AnUnreadableOwnTableIsReportedAndTheInstallsIsUsed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Own)!);
        File.WriteAllBytes(Own, new byte[5]);
        Write(Original, Rows("Original"));
        var reported = new List<string>();
        Assert.Equal(Rows("Original"), new HiScoreStore(Own, Install).Load(reported.Add));
        Assert.Equal(new[] { Own }, reported);
    }

    [Fact]
    public void WithNeitherTableTheRowsAreEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Original)!);
        File.WriteAllBytes(Original, new byte[5]);
        var reported = new List<string>();
        Assert.Same(HiScoreTable.Empty, new HiScoreStore(Own, Install).Load(reported.Add));
        Assert.Equal(new[] { Original }, reported);
        Assert.Same(HiScoreTable.Empty, new HiScoreStore(Own, Path.Combine(_root, "nowhere")).Load());
    }

    [Fact]
    public void ATrackedMapsCommittedEntryIsWrittenToOurOwnTableOnly()
    {
        Write(Original, Rows("Original"));
        byte[] original = File.ReadAllBytes(Original);
        var store = new HiScoreStore(Own, Install);
        var frame = new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000);
        var session = new FrontEndSession(_ => new[] { frame, frame, frame }, FrontEndPage.CampaignResults);
        var profile = CampaignProgression.RecordResult(new CampaignProfile { LevelNumber = 2 }, new CampaignSlot(2), won: true);
        profile = CampaignProgression.RecordStatistics(profile, PlayerIds.FirstKeeper,
            new LevelStatisticsBlock { LevelWon = true, GoldMined = 700 }, new CampaignSlot(2), keeperRank: 10);
        using var map = new CampaignMapController(session, profile, false,
            slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()), store.Load());
        store.Track(map);
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromMilliseconds(10)); session.Update(TimeSpan.FromSeconds(2));
        Assert.True(map.AcceptResults());
        Assert.True(map.TypeEntry('Z'));
        Assert.True(map.CommitEntry());

        Assert.Equal(map.HiScores, HiScoresFile.Read(File.ReadAllBytes(Own)));
        Assert.Equal("Z", map.HiScores[3].Name);                // 700 meets the fourth row's 700
        Assert.Equal(original, File.ReadAllBytes(Original));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
