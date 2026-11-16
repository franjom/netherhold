// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class CampaignProfileStoreTests : IDisposable
{
    private static readonly DateTime OldStamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Directory.CreateTempSubdirectory("dk2-profile-").FullName;
    private DateTime _clock = new(2026, 11, 16, 12, 0, 0);

    private string ProfilePath => Path.Combine(_root, "campaign-profile.dk2p");

    private CampaignProfileStore Store(Func<CampaignProfile?>? import = null) => new(import, () => _clock);

    private static byte[] Bytes(CampaignProfile profile) => CampaignProfileFile.Write(profile);

    private static CampaignProfile WithStatuses(params (int Slot, CampaignLevelStatus Status)[] statuses)
    {
        var all = new CampaignLevelStatus[40];
        foreach ((int slot, CampaignLevelStatus status) in statuses) all[slot - 1] = status;
        return new CampaignProfile { PlayerLevelStatus = Array.AsReadOnly(all) };
    }

    private void WriteOld(CampaignProfile profile)
    {
        File.WriteAllBytes(ProfilePath, Bytes(profile));
        File.SetLastWriteTimeUtc(ProfilePath, OldStamp);
    }

    [Fact]
    public void AMissingFileIsCreatedFromTheImportWithItsFirstMapOpened()
    {
        int imports = 0;
        CampaignProfileStore store = Store(() => { imports++; return new CampaignProfile { LevelNumber = 3 }; });
        Assert.Null(store.Path);
        Assert.Null(store.Profile);

        CampaignProfile loaded = store.Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.None);

        Assert.Equal(1, imports);
        Assert.Equal(ProfilePath, store.Path);
        Assert.Same(loaded, store.Profile);
        Assert.Equal(3, loaded.LevelNumber);
        Assert.Equal(CampaignLevelStatus.Open, loaded.PlayerLevelStatus[0]);
        Assert.Equal(Bytes(loaded), File.ReadAllBytes(ProfilePath));
    }

    [Fact]
    public void WithNoImportAMissingFileStartsAFreshCampaign()
    {
        CampaignProfile loaded = Store().Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.None);
        Assert.Equal(Bytes(CampaignProgression.EnsureOpenMap(new CampaignProfile())), Bytes(loaded));
        Assert.Equal(Bytes(loaded), File.ReadAllBytes(ProfilePath));
    }

    [Fact]
    public void AnExistingFileIsReadAndTheImportIsNotAsked()
    {
        CampaignProfile saved = WithStatuses((1, CampaignLevelStatus.Won), (2, CampaignLevelStatus.Open)) with { LevelNumber = 2 };
        WriteOld(saved);
        CampaignProfile loaded = Store(() => throw new InvalidOperationException("import asked"))
            .Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.None);
        Assert.Equal(Bytes(saved), Bytes(loaded));
    }

    [Fact]
    public void AnUnchangedProfileIsNotWrittenBack()
    {
        WriteOld(WithStatuses((1, CampaignLevelStatus.Open)));
        Store().Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.CampaignResults);
        Assert.Equal(OldStamp, File.GetLastWriteTimeUtc(ProfilePath));
    }

    [Fact]
    public void AProfileWithNoOpenMapIsOpenedAndWrittenBack()
    {
        WriteOld(WithStatuses((1, CampaignLevelStatus.Won), (2, CampaignLevelStatus.Won)));
        CampaignProfile loaded = Store().Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.None);
        Assert.Equal(CampaignLevelStatus.Open, loaded.PlayerLevelStatus[2]);
        Assert.NotEqual(OldStamp, File.GetLastWriteTimeUtc(ProfilePath));
        Assert.Equal(Bytes(loaded), File.ReadAllBytes(ProfilePath));
    }

    /// <summary>Map 2 won with its branch row lost: only the front end entered from the results re-applies it.</summary>
    [Theory]
    [InlineData(GameSessionMode.FrontEnd, FrontEndPage.CampaignResults, true)]
    [InlineData(GameSessionMode.FrontEnd, FrontEndPage.None, false)]
    [InlineData(GameSessionMode.FrontEnd, FrontEndPage.Load, false)]
    [InlineData(GameSessionMode.Campaign, FrontEndPage.CampaignResults, false)]
    public void OnlyTheFrontEndAfterALevelRepairsTheWonMap(GameSessionMode mode, FrontEndPage returnPage, bool repairs)
    {
        CampaignProfile saved = WithStatuses((1, CampaignLevelStatus.Open), (2, CampaignLevelStatus.Won)) with { LevelNumber = 2 };
        CampaignProfile repaired = CampaignProgression.RepairAfterLevel(saved);
        Assert.NotEqual(Bytes(saved), Bytes(repaired));
        WriteOld(saved);

        CampaignProfile loaded = Store().Load(ProfilePath, mode, returnPage);

        Assert.Equal(repairs, CampaignProfileStore.RepairsAfterLevel(mode, returnPage));
        Assert.Equal(Bytes(repairs ? repaired : saved), Bytes(loaded));
        Assert.Equal(Bytes(loaded), File.ReadAllBytes(ProfilePath));
        Assert.Equal(repairs, File.GetLastWriteTimeUtc(ProfilePath) != OldStamp);
    }

    /// <summary>Both steps act and do not commute: the repair runs on the opened profile, after the opening.</summary>
    [Fact]
    public void TheRepairRunsOnTheProfileWithItsMapOpened()
    {
        // Map 5 won: EnsureOpenMap opens 2 (the first locked), the repair opens 6 and 7.
        WriteOld(WithStatuses((1, CampaignLevelStatus.Won), (5, CampaignLevelStatus.Won)) with { LevelNumber = 5 });
        CampaignProfile loaded = Store().Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.CampaignResults);
        Assert.Equal(new[] { 2, 6, 7 }, OpenSlots(loaded));
        Assert.Equal(Bytes(loaded), File.ReadAllBytes(ProfilePath));
    }

    [Fact]
    public void TheMapIsOpenedBeforeTheRepairClosesABranch()
    {
        // Map 6 won with 8 already won: EnsureOpenMap opens 7, which the repair then closes; nothing is left open.
        // In the other order the repair closes 7 first and EnsureOpenMap would open 9.
        WriteOld(WithStatuses((1, CampaignLevelStatus.Won), (2, CampaignLevelStatus.Won), (3, CampaignLevelStatus.Won),
            (4, CampaignLevelStatus.Won), (5, CampaignLevelStatus.Won), (6, CampaignLevelStatus.Won), (8, CampaignLevelStatus.Won))
            with { LevelNumber = 6 });
        CampaignProfile loaded = Store().Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.CampaignResults);
        Assert.Equal(CampaignLevelStatus.ClosedBranch, loaded.PlayerLevelStatus[6]);
        Assert.Empty(OpenSlots(loaded));
    }

    private static int[] OpenSlots(CampaignProfile profile) =>
        Enumerable.Range(1, 24).Where(slot => profile.PlayerLevelStatus[slot - 1] == CampaignLevelStatus.Open).ToArray();

    [Fact]
    public void AnAdoptedProfileHasNoFileAndKeepsTrackedChangesInMemory()
    {
        _clock = Day(available: true);
        CampaignProfileStore store = Store();
        CampaignProfile adopted = store.Adopt(new CampaignProfile());
        Assert.Null(store.Path);
        Assert.Same(adopted, store.Profile);
        Assert.Equal(new[] { 1 }, OpenSlots(adopted));
        Assert.True(store.SecretFiveAvailable);
        var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromSeconds(1));
        using var map = new CampaignMapController(session, WithStatuses((1, CampaignLevelStatus.Won), (2, CampaignLevelStatus.Open)), false,
            slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()));
        store.Track(map);
        Assert.True(map.StartNew());
        Assert.Same(map.Profile, store.Profile);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void EachLoadReadsTheMoonFromTheGivenClock()
    {
        DateTime open = Day(available: true), closed = Day(available: false);
        CampaignProfileStore store = Store();
        _clock = open;
        store.Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.None);
        Assert.True(store.SecretFiveAvailable);
        _clock = closed;
        store.Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.None);
        Assert.False(store.SecretFiveAvailable);
    }

    private static DateTime Day(bool available)
    {
        var day = new DateTime(2026, 1, 1, 12, 0, 0);
        while (CampaignMoon.IsSecretFiveAvailable(CampaignMoon.PhaseAt(day)) != available) day = day.AddDays(1);
        return day;
    }

    [Fact]
    public void SaveWritesTheLiveProfile()
    {
        CampaignProfileStore store = Store();
        store.Load(ProfilePath, GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        store.Profile = store.Profile! with { TransferCreatureId = 7 };
        store.Save();
        Assert.Equal(7, CampaignProfileFile.Read(File.ReadAllBytes(ProfilePath)).TransferCreatureId);
    }

    [Fact]
    public void ATrackedMapsChangeBecomesLiveThenNotifiesThenIsWritten()
    {
        WriteOld(WithStatuses((1, CampaignLevelStatus.Won), (2, CampaignLevelStatus.Won), (3, CampaignLevelStatus.Open)) with { LevelNumber = 3 });
        CampaignProfileStore store = Store();
        CampaignProfile before = store.Load(ProfilePath, GameSessionMode.FrontEnd, FrontEndPage.None);
        var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromSeconds(1));
        using var map = new CampaignMapController(session, before, false,
            slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()));
        store.Track(map);
        CampaignProfile? notified = null;
        store.Changed += profile =>
        {
            notified = profile;
            Assert.Same(profile, store.Profile);
            Assert.Equal(Bytes(before), File.ReadAllBytes(ProfilePath));
        };

        Assert.True(map.StartNew());

        Assert.NotNull(notified);
        Assert.Same(map.Profile, notified);
        Assert.Same(map.Profile, store.Profile);
        Assert.NotEqual(Bytes(before), Bytes(map.Profile));
        Assert.Equal(Bytes(map.Profile), File.ReadAllBytes(ProfilePath));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
