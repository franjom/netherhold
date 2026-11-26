// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class FrontEndHostTests
{
    private static (CampaignMapController Map, Func<IReadOnlyList<HiScoreEntry>?> Saved) NameEntry()
    {
        (CampaignMapController map, Func<IReadOnlyList<HiScoreEntry>?> saved, _) = NameEntryWithSession();
        return (map, saved);
    }

    private static (CampaignMapController Map, Func<IReadOnlyList<HiScoreEntry>?> Saved, FrontEndSession Session) NameEntryWithSession()
    {
        var frame = new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000);
        var session = new FrontEndSession(_ => new[] { frame, frame, frame }, FrontEndPage.CampaignResults);
        CampaignProfile profile = CampaignProgression.RecordResult(new CampaignProfile { LevelNumber = 2 }, new CampaignSlot(2), won: true);
        profile = CampaignProgression.RecordStatistics(profile, PlayerIds.FirstKeeper,
            new LevelStatisticsBlock { LevelWon = true, GoldMined = 700 }, new CampaignSlot(2), keeperRank: 10);
        HiScoreEntry[] scores = [.. Enumerable.Range(0, 10).Select(i => new HiScoreEntry((uint)(1000 - i * 100), "K" + i, "1"))];
        var map = new CampaignMapController(session, profile, false, slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()), scores);
        IReadOnlyList<HiScoreEntry>? saved = null;
        map.HiScoresChanged += rows => saved = rows;
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromMilliseconds(10)); session.Update(TimeSpan.FromSeconds(2));
        Assert.True(map.AcceptResults());
        Assert.Equal(3, map.EntryRow);
        return (map, () => saved, session);
    }

    [Fact]
    public void TheCampaignOpensOnTheLiveProfileAndPersistsAfterNotifying()
    {
        string root = Directory.CreateTempSubdirectory("dk2-campaign-").FullName;
        try
        {
            var profiles = new CampaignProfileStore(null, () => new DateTime(2026, 11, 16, 12, 0, 0));
            string profilePath = Path.Combine(root, "campaign-profile.dk2p");
            File.WriteAllBytes(profilePath, CampaignProfileFile.Write(new CampaignProfile
            {
                LevelNumber = 3, PlayerLevelStatus = Array.AsReadOnly(new[] { CampaignLevelStatus.Won, CampaignLevelStatus.Won,
                    CampaignLevelStatus.Open }.Concat(new CampaignLevelStatus[37]).ToArray()),
            }));
            CampaignProfile before = profiles.Load(profilePath, GameSessionMode.FrontEnd, FrontEndPage.None);
            var hiScoreStore = new HiScoreStore(Path.Combine(root, "HiScores.dat"), Path.Combine(root, "install"));
            HiScoreEntry[] rows = [.. Enumerable.Range(0, 10).Select(i => new HiScoreEntry((uint)(10 - i), "R" + i, "1"))];
            var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
            session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromSeconds(1));
            var seen = new List<string>();
            using CampaignMapController map = FrontEndHost.OpenCampaign(session, profiles, hiScoreStore, rows,
                slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()),
                profile => seen.Add(File.ReadAllBytes(profilePath).SequenceEqual(CampaignProfileFile.Write(profile)) ? "written" : "pending"));

            Assert.Same(before, map.Profile);
            Assert.Same(rows, map.HiScores);
            Assert.Equal(profiles.SecretFiveAvailable, map.SecretFiveAvailable);
            Assert.True(map.StartNew());
            Assert.Equal(new[] { "pending" }, seen);
            Assert.Same(map.Profile, profiles.Profile);
            Assert.Equal(CampaignProfileFile.Write(map.Profile), File.ReadAllBytes(profilePath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TheCampaignsCommittedHiScoresAreWrittenToTheirStore()
    {
        string root = Directory.CreateTempSubdirectory("dk2-campaign-").FullName;
        try
        {
            var frame = new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000);
            var session = new FrontEndSession(_ => new[] { frame, frame, frame }, FrontEndPage.CampaignResults);
            CampaignProfile profile = CampaignProgression.RecordResult(new CampaignProfile { LevelNumber = 2 }, new CampaignSlot(2), won: true);
            profile = CampaignProgression.RecordStatistics(profile, PlayerIds.FirstKeeper,
                new LevelStatisticsBlock { LevelWon = true, GoldMined = 700 }, new CampaignSlot(2), keeperRank: 10);
            var profiles = new CampaignProfileStore(null, () => new DateTime(2026, 11, 16, 12, 0, 0));
            profiles.Adopt(profile);
            var hiScoreStore = new HiScoreStore(Path.Combine(root, "HiScores.dat"), Path.Combine(root, "install"));
            HiScoreEntry[] rows = [.. Enumerable.Range(0, 10).Select(i => new HiScoreEntry((uint)(1000 - i * 100), "K" + i, "1"))];
            using CampaignMapController map = FrontEndHost.OpenCampaign(session, profiles, hiScoreStore, rows,
                slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()), _ => { });
            session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromMilliseconds(10)); session.Update(TimeSpan.FromSeconds(2));
            Assert.True(map.AcceptResults());
            FrontEndHost.TypeName(map, "Z\r");
            Assert.Equal("Z", HiScoresFile.Read(File.ReadAllBytes(hiScoreStore.Path))[3].Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("Ab\bC\rXY", "AC")]
    [InlineData("Q\n", "Q")]
    public void TypedTextErasesOnBackspaceAndCommitsOnEnter(string typed, string name)
    {
        (CampaignMapController map, Func<IReadOnlyList<HiScoreEntry>?> saved) = NameEntry();
        using (map)
        {
            FrontEndHost.TypeName(map, typed);
        }

        Assert.Equal(-1, map.EntryRow);
        Assert.Equal(name, saved()![3].Name);
    }

    [Fact]
    public void ARefusedCommitStillDropsTheRestOfTheFramesText()
    {
        (CampaignMapController map, Func<IReadOnlyList<HiScoreEntry>?> saved, FrontEndSession session) = NameEntryWithSession();
        // A path in flight: CommitEntry refuses (EntryRow stays), and the text after Enter is still not typed.
        Assert.True(session.TryTransition(new(FrontEndPath.BriefingToTable, FrontEndState.CampaignMapEnter)));
        Assert.True(session.PathPlaying);
        using (map)
        {
            FrontEndHost.TypeName(map, "A\rB");
        }

        Assert.Equal(3, map.EntryRow);
        Assert.Equal("A", map.HiScores[3].Name);
        Assert.Null(saved());
    }

    [Fact]
    public void TextWithoutEnterLeavesTheEntryOpen()
    {
        (CampaignMapController map, Func<IReadOnlyList<HiScoreEntry>?> saved) = NameEntry();
        using (map)
        {
            FrontEndHost.TypeName(map, "Ke\b");
        }

        Assert.Equal(3, map.EntryRow);
        Assert.Equal("K", map.HiScores[3].Name);
        Assert.Null(saved());
    }

    private static ObjectView View(int id) => new(id, 1, new PlayerId(0), new TilePos(0, 0), default, 0, 0, 0, 0, 0, Fp32.One);

    [Fact]
    public void ThePickTableMapsPiecesByTheirNumberAndMarkersByTheirSlot()
    {
        var slots = new Dictionary<int, CampaignSlot> { [99] = new CampaignSlot(9) };
        FrontEndHost.FillPickSlots(new[]
        {
            new FrontEndProp(View(10), MapPiece: FrontEndMapPiece.Level6b),
            new FrontEndProp(View(11), MarkerSlot: new CampaignSlot(4)),
            new FrontEndProp(View(12)),
            new FrontEndProp(View(13), MapPiece: FrontEndMapPiece.Secret2, MarkerSlot: new CampaignSlot(2)),
        }, slots);
        Assert.Equal(new Dictionary<int, CampaignSlot>
        {
            [10] = new CampaignSlot(7), [11] = new CampaignSlot(4), [13] = new CampaignSlot(27),
        }, slots);
    }

    [Theory]
    [InlineData(0, 0, 0f)]
    [InlineData(2.6, 10, .4f)]
    [InlineData(.2, 0, .8f)]
    public void TheSceneryClockRunsAtDK2sFourTurnsASecond(double seconds, long tick, float fraction)
    {
        (GameTick at, float into) = FrontEndHost.CreatureFrame(seconds);
        Assert.Equal(new GameTick(tick), at);
        Assert.Equal(fraction, into, 4);
    }

    [Fact]
    public void TheMovieGalleryReadsItsFilesFromTheInstall()
    {
        string install = Directory.CreateTempSubdirectory("dk2-movies-").FullName;
        try
        {
            var won = new CampaignLevelStatus[40]; won[0] = CampaignLevelStatus.Won;
            var profile = new CampaignProfile { PlayerLevelStatus = Array.AsReadOnly(won) };
            Assert.False(FrontEndHost.MovieGallery(profile, install, _ => "").Entries[0].Available);
            Directory.CreateDirectory(Path.Combine(install, "Data", "Movies"));
#pragma warning disable IDE0301 // Preserve the original empty collection lowering.
            File.WriteAllBytes(Path.Combine(install, "Data", "Movies", "Intro.tgq"), Array.Empty<byte>());
#pragma warning restore IDE0301
            FrontEndMovieGallery gallery = FrontEndHost.MovieGallery(profile, install, id => id == 77 ? "Intro" : "?");
            Assert.True(gallery.Entries[0].Available);
            Assert.Equal("Intro", gallery.Entries[0].Title);
            Assert.False(gallery.Entries[1].Available);
            Assert.Equal("", gallery.Entries[1].Title);               // no Speech.str in this install
        }
        finally { Directory.Delete(install, recursive: true); }
    }

    [Fact]
    public void TheStartupRunsOnceAndOnlyOnAFreshFrontEnd()
    {
        var gate = new FrontEndStartupGate();
        Assert.False(gate.Begin(skip: false, FrontEndPage.CampaignResults));
        Assert.True(gate.Shown);
        gate.Reset();
        Assert.True(gate.Begin(skip: false, FrontEndPage.None));
        Assert.False(gate.Begin(skip: false, FrontEndPage.None));
        gate.Reset();
        Assert.False(gate.Begin(skip: true, FrontEndPage.None));
        Assert.False(gate.Begin(skip: false, FrontEndPage.None));
    }
}
