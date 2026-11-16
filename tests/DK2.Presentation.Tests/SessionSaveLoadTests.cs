// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary><see cref="SessionSaver"/>, <see cref="SessionLoader"/> and <see cref="SessionContext"/>, moved out of Unity's
/// DungeonRunner (Saves, LevelEnd) for both adapters.</summary>
public sealed class SessionSaveLoadTests : IDisposable
{
    private static readonly DateTime SavedAt = new(2026, 11, 16, 9, 30, 0, DateTimeKind.Utc);
    private readonly string _root = Directory.CreateTempSubdirectory("dk2-session-").FullName;
    private int _profileLoads;

    private string ProfilePath => Path.Combine(_root, "campaign-profile.dk2p");
    private string SavesDirectory => Path.Combine(_root, "Saves");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static byte[] Bytes(CampaignProfile profile) => CampaignProfileFile.Write(profile);

    private static CampaignLevelData Level(string name = "Level5")
    {
        TerrainDef terrain = new()
        {
            Name = "test", TerrainId = 2, Flags = TerrainFlags.Solid, StartingHealth = 1, MaxHealth = 1, GoldValue = 0,
            ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var database = new GameDatabase([terrain], [], [], new GlobalVariables());
        var map = new LevelMap(4, 4, Enumerable.Repeat(new MapTile(2, PlayerIds.Unowned, 0, 0), 16).ToArray());
        var level = new LevelDef([])
        {
            Name = name, Description = "", Author = "", Email = "", Information = "", Unknown_1184 = default, Messages = [],
            SoundCategory = "", PreviousRewards = default, NextRewards = default, IntroductionOverrideObjects = default,
            IntroductionOverrideTextIds = [], TerrainPath = "", Unknown_6403 = default, Paths = [], UnknownPathTail = [],
        };
        return new CampaignLevelData(name, database, map, level, [], [], []);
    }

    private CampaignProfileStore Store() => new(import: null, () => new DateTime(2026, 11, 16));

    private Action LoadInto(CampaignProfileStore store) => () =>
    {
        _profileLoads++;
        store.Load(ProfilePath, GameSessionMode.Campaign, FrontEndPage.CampaignResults);
    };

    private SessionSaver Saver(CampaignProfileStore store) => new(store, LoadInto(store), () => SavedAt);

    private SessionLoader Loader(CampaignProfileStore store, CampaignLevelData level) => new(store, LoadInto(store), _ => level);

    private SaveSlotsView Saves(string name)
    {
        var saves = new SaveSlotsView(SavesDirectory);
        saves.SetName(name);
        return saves;
    }

    [Fact]
    public void TheHeaderIsTheContextsWithTheInjectedClockAndTheTypedName()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        CampaignProfileStore store = Store();
        var context = new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        SaveSlotsView saves = Saves("my keep");

        string path = Saver(store).Save(simulation, simulation.World.Carry, context, saves);

        Assert.Equal(SavedSessionFile.PathForName(SavesDirectory, "my keep"), path);
        SavedSession saved = SavedSessionFile.Load(path);
        Assert.Equal(new SavedSessionHeader("Level5", (byte)GameSessionMode.Campaign, (ushort)FrontEndPage.CampaignResults,
            SavedAt, "my keep"), saved.Header);
        Assert.Equal(WorldSnapshot.ToBytes(simulation), saved.Snapshot);
        Assert.Equal(Bytes(store.Profile!), Bytes(saved.Profile));
        Assert.Single(saves.Entries);
    }

    [Fact]
    public void TheProfileIsLoadedOnlyWhenTheStoreHasNone()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        CampaignProfileStore store = Store();
        var context = new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.None);
        SessionSaver saver = Saver(store);

        saver.Save(simulation, default, context, Saves("one"));
        saver.Save(simulation, default, context, Saves("two"));

        Assert.Equal(1, _profileLoads);
    }

    [Fact]
    public void ThePlayedSlotsCarryIsWrittenToTheProfileBeforeTheSave()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        CampaignProfileStore store = Store();
        store.Load(ProfilePath, GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        store.Profile = store.Profile! with { LevelNumber = 5 };
        var context = new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        context.PlaySlotOf(store.Profile);
        var carry = new CampaignCarry { HiddenLandFound = 1, TransferStored = true, TransferCreatureTypeId = 9, TransferCreatureLevel = 4 };

        string path = Saver(store).Save(simulation, carry, context, Saves("carried"));

        CampaignProfile saved = SavedSessionFile.Load(path).Profile;
        Assert.Equal((byte)9, saved.TransferCreatureId);
        Assert.Equal((byte)4, saved.TransferCreatureLevel);
        Assert.Equal(1u, saved.SecretLevels[0]);
        Assert.Equal(carry, context.CarryApplied);
        Assert.Equal(Bytes(saved), File.ReadAllBytes(ProfilePath));
    }

    [Fact]
    public void WithNoPlayedSlotTheCarryIsNotApplied()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        CampaignProfileStore store = Store();
        var context = new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.None);
        var carry = new CampaignCarry { TransferStored = true, TransferCreatureTypeId = 9, TransferCreatureLevel = 4 };

        string path = Saver(store).Save(simulation, carry, context, Saves("plain"));

        Assert.Equal((byte)0, SavedSessionFile.Load(path).Profile.TransferCreatureId);
        Assert.Equal(default, context.CarryApplied);
    }

    [Fact]
    public void TheCarryIsAppliedOnceAndOnlyItsNewHiddenLandsCount()
    {
        CampaignProfileStore store = Store();
        store.Load(ProfilePath, GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        store.Profile = store.Profile! with { LevelNumber = 8 };
        var context = new SessionContext("Level8", GameSessionMode.Campaign, FrontEndPage.CampaignResults)
        {
            CarryApplied = new CampaignCarry { HiddenLandFound = 1 },
        };

        context.ApplyCarry(store, new CampaignCarry { HiddenLandFound = 1 });
        Assert.Equal(0u, store.Profile.SecretLevels[1]);
        Assert.NotEqual(Bytes(store.Profile), File.ReadAllBytes(ProfilePath));

        context.ApplyCarry(store, new CampaignCarry { HiddenLandFound = 2 });
        Assert.Equal(1u, store.Profile.SecretLevels[1]);
        Assert.Equal(2, context.CarryApplied.HiddenLandFound);
        Assert.Equal(Bytes(store.Profile), File.ReadAllBytes(ProfilePath));
    }

    [Theory]
    [InlineData("Level5", 5, 5)]
    [InlineData("level5", 5, 5)]
    [InlineData("Level6", 5, 0)]
    public void ThePlayedSlotIsTheProfilesOnlyWhenItNamesThisLevel(string levelName, byte profileLevel, byte slot)
    {
        var context = new SessionContext(levelName, GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        context.PlaySlotOf(new CampaignProfile { LevelNumber = profileLevel });
        Assert.Equal(slot, context.PlayedSlot.Value);
    }

    [Theory]
    [InlineData(GameSessionMode.Campaign, FrontEndPage.CampaignResults, true)]
    [InlineData(GameSessionMode.Campaign, FrontEndPage.None, false)]
    [InlineData(GameSessionMode.FrontEnd, FrontEndPage.CampaignResults, false)]
    public void OnlyACampaignLevelReturningToTheResultsIsLaunchedFromTheFrontEnd(GameSessionMode mode, FrontEndPage page, bool launched)
        => Assert.Equal(launched, new SessionContext("Level1", mode, page).LaunchedFromFrontEnd);

    [Fact]
    public void ALoadRestoresTheExactCheckpointAndResumesItsLevelAndReturnPage()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        CampaignProfileStore store = Store();
        var context = new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        string path = Saver(store).Save(simulation, default, context, Saves("checkpoint"));

        GameSessionRequest request = Loader(Store(), level).Load(path);

        Assert.Equal(GameSessionMode.Campaign, request.Mode);
        Assert.Equal("Level5", request.LevelName);
        Assert.Equal(FrontEndPage.CampaignResults, request.ReturnPage);
        Assert.NotNull(request.Resume);
        Assert.Equal("checkpoint", request.Resume!.Session.Header.DisplayName);
        Assert.Equal(simulation.StateHash(), request.Resume.Simulation.StateHash());
        Assert.Equal(simulation.World.CurrentTick, request.Resume.Simulation.World.CurrentTick);
    }

    [Fact]
    public void ALoadWritesTheSavedProfileToTheProfileFileWithoutAdoptingIt()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        var saved = CampaignProgression.EnsureOpenMap(new CampaignProfile { LevelNumber = 11, PlayerValues = [42, 0, 0, 0, 0, 0, 0, 0] });
        string path = SavedSessionFile.PathForName(_root, "theirs");
        SavedSessionFile.Save(path, new SavedSession(new SavedSessionHeader("Level5", (byte)GameSessionMode.Campaign, 0, SavedAt, "theirs"),
            WorldSnapshot.ToBytes(simulation), saved));
        CampaignProfileStore store = Store();
        CampaignProfile live = store.Load(ProfilePath, GameSessionMode.Campaign, FrontEndPage.None);
        _profileLoads = 0;

        Loader(store, level).Load(path);

        Assert.Equal(Bytes(saved), File.ReadAllBytes(ProfilePath));
        Assert.Same(live, store.Profile);
        Assert.Equal(0, _profileLoads);
    }

    [Fact]
    public void ALoadWithNoProfileFileLoadsTheStoreFirst()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        string path = Saver(Store()).Save(simulation, default,
            new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.None), Saves("first"));
        File.Delete(ProfilePath);
        CampaignProfileStore store = Store();
        _profileLoads = 0;

        Loader(store, level).Load(path);

        Assert.Equal(1, _profileLoads);
        Assert.Equal(ProfilePath, store.Path);
        Assert.True(File.Exists(ProfilePath));
    }

    /// <summary>The loader also refuses a game mode other than a campaign's, which <see cref="SavedSessionFile"/> already
    /// refuses to write or read, so only the return page is reachable here.</summary>
    [Fact]
    public void ASaveWithAnUnknownReturnPageIsRefusedAndWritesNothing()
    {
        const byte mode = (byte)GameSessionMode.Campaign;
        const ushort page = 0x7777;
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        string path = SavedSessionFile.PathForName(_root, "odd");
        SavedSessionFile.Save(path, new SavedSession(new SavedSessionHeader("Level5", mode, page, SavedAt, "odd"),
            WorldSnapshot.ToBytes(simulation), new CampaignProfile()));

        Assert.Throws<SaveIncompatibleException>(() => Loader(Store(), level).Load(path));
        Assert.False(File.Exists(ProfilePath));
    }

    [Fact]
    public void ASaveOfALevelTheInstallLacksIsRefusedAndWritesNothing()
    {
        GameSimulation simulation = Level().CreateSimulation(7, fogOfWarEnabled: false);
        string path = Saver(Store()).Save(simulation, default,
            new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.None), Saves("missing"));
        File.Delete(ProfilePath);
        var loader = new SessionLoader(Store(), LoadInto(Store()), _ => throw new InvalidOperationException("no map"));

        var refusal = Assert.Throws<SaveIncompatibleException>(() => loader.Load(path));
        Assert.Equal(SaveIncompatibleReason.Corrupt, refusal.Reason);
        Assert.False(File.Exists(ProfilePath));
    }

    /// <summary>An adapter whose profile could not be read keeps an adopted one with no file (Godot's fallback): a load
    /// is refused before anything is written, rather than writing a profile to no path.</summary>
    [Fact]
    public void ALoadOverAProfileWithNoFileIsRefusedAndWritesNothing()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        string path = Saver(Store()).Save(simulation, default,
            new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.None), Saves("unreadable"));
        File.Delete(ProfilePath);
        CampaignProfileStore store = Store();
        var loader = new SessionLoader(store, () => store.Adopt(new CampaignProfile()), _ => level);

        var refusal = Assert.Throws<IOException>(() => loader.Load(path));

        Assert.Equal("The campaign profile could not be read, so a save cannot be loaded.", refusal.Message);
        Assert.Null(store.Path);
        Assert.False(File.Exists(ProfilePath));
    }

    /// <summary>The same fallback on a save: the carry still goes into the in-memory profile and the save, and no
    /// profile file is written.</summary>
    [Fact]
    public void ASaveWithACarryOverAProfileWithNoFileKeepsItInMemory()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        CampaignProfileStore store = Store();
        store.Adopt(new CampaignProfile { LevelNumber = 5 });
        var context = new SessionContext("Level5", GameSessionMode.Campaign, FrontEndPage.CampaignResults);
        context.PlaySlotOf(store.Profile!);
        var carry = new CampaignCarry { TransferStored = true, TransferCreatureTypeId = 9, TransferCreatureLevel = 4 };

        string path = Saver(store).Save(simulation, carry, context, Saves("memory"));

        Assert.Equal((byte)9, store.Profile!.TransferCreatureId);
        Assert.Equal((byte)9, SavedSessionFile.Load(path).Profile.TransferCreatureId);
        Assert.Equal(carry, context.CarryApplied);
        Assert.Null(store.Path);
        Assert.False(File.Exists(ProfilePath));
    }

    /// <summary>A save whose checkpoint cannot be read is refused at <see cref="WorldSnapshot.FromBytes"/>, before the
    /// saved profile would replace the player's.</summary>
    [Fact]
    public void ASaveWithACorruptSnapshotIsRefusedAndLeavesTheProfileAlone()
    {
        CampaignLevelData level = Level();
        CampaignProfileStore store = Store();
        store.Load(ProfilePath, GameSessionMode.Campaign, FrontEndPage.None);
        byte[] before = File.ReadAllBytes(ProfilePath);
        string path = SavedSessionFile.PathForName(_root, "garbage");
        SavedSessionFile.Save(path, new SavedSession(new SavedSessionHeader("Level5", (byte)GameSessionMode.Campaign, 0, SavedAt, "garbage"),
            [0x47, 0x41, 0x52, 0x42, 0x41, 0x47, 0x45, 0x21, 1, 2, 3, 4, 5, 6, 7, 8], new CampaignProfile { LevelNumber = 20 }));

        Assert.ThrowsAny<IOException>(() => Loader(store, level).Load(path));

        Assert.Equal(before, File.ReadAllBytes(ProfilePath));
    }

    [Fact]
    public void ARestartOfAResumedRequestStartsTheLevelAfresh()
    {
        CampaignLevelData level = Level();
        GameSimulation simulation = level.CreateSimulation(7, fogOfWarEnabled: false);
        var header = new SavedSessionHeader("Level5", (byte)GameSessionMode.Campaign, (ushort)FrontEndPage.CampaignResults, SavedAt, "x");
        GameSessionRequest resumed = GameSessionRequest.Resumed(new SessionResume(new SavedSession(header, [1], new CampaignProfile()), simulation));

        GameSessionRequest restart = GameSessionRequest.Restart(resumed);

        Assert.Null(restart.Resume);
        Assert.Equal(GameSessionRequest.Campaign("Level5"), restart);
    }

    /// <summary>The determinism contract of a save: a mid-game Level1 saved and loaded through the moved code has the
    /// same state hash, and every turn after it hashes as the run that was never saved.</summary>
    [InstallFact]
    public void ASavedAndLoadedLevelContinuesWithTheUnsavedRunsHashes()
    {
        string install = Dk2InstallGate.Path;
        PlayerId keeper = PlayerIds.FirstKeeper;
        GameSimulation unsaved = CampaignLevelData.Load(install, "Level1").CreateSimulation(1, fogOfWarEnabled: true);
        unsaved.World.LocalPlayerId = keeper;
        for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame + 300; i++) unsaved.Tick(InputFrame.Empty);
        ulong savedHash = unsaved.StateHash();
        CampaignProfileStore store = Store();
        string path = Saver(store).Save(unsaved, unsaved.World.Carry,
            new SessionContext("Level1", GameSessionMode.Campaign, FrontEndPage.None), Saves("determinism"));
        Assert.Equal(savedHash, unsaved.StateHash());

        GameSimulation loaded = new SessionLoader(store, LoadInto(store), name => CampaignLevelData.Load(install, name))
            .Load(path).Resume!.Simulation;
        loaded.World.LocalPlayerId = keeper;

        Assert.Equal(unsaved.World.CurrentTick, loaded.World.CurrentTick);
        Assert.Equal(savedHash, loaded.StateHash());
        for (int i = 0; i < 300; i++)
        {
            unsaved.Tick(InputFrame.Empty);
            loaded.Tick(InputFrame.Empty);
            Assert.Equal(unsaved.StateHash(), loaded.StateHash());
        }
    }

    /// <summary>The load restores the exact checkpoint (<see cref="WorldSnapshot.FromBytes"/>), not DK2's own reload
    /// (<see cref="WorldSnapshot.LoadGame"/>, which re-chains every room row-major by clearing each tile's slab order;
    /// whether ours should is open): a room tile's slab order, and so the hash, survives the save.</summary>
    [InstallFact]
    public void ALoadKeepsTheRoomChainsAsSavedRatherThanReChainingThem()
    {
        string install = Dk2InstallGate.Path;
        GameSimulation simulation = CampaignLevelData.Load(install, "Level1").CreateSimulation(1, fogOfWarEnabled: true);
        simulation.World.LocalPlayerId = PlayerIds.FirstKeeper;
        simulation.Tick(InputFrame.Empty);
        // A chain as a room built tile by tile leaves it (DK2's 0x004C7380 order), on the Heart's first tile.
        int heart = -1;
        for (int i = 0; i < simulation.World.TileCount && heart < 0; i++)
            if (RoomIndex.RoomAt(simulation.World, simulation.Database, i) is not null) heart = i;
        Assert.True(heart >= 0);
        simulation.World.TileAtIndex(heart).SlabOrder = 7;
        byte[] snapshot = WorldSnapshot.ToBytes(simulation);
        CampaignLevelData records = CampaignLevelData.Load(install, "Level1");
        TriggerTree Tree() => CampaignSimulationFactory.Triggers(records.Level, records.Players, records.Things, records.Triggers);
        // The precondition: on this world DK2's reload and the exact checkpoint differ.
        Assert.NotEqual(WorldSnapshot.FromBytes(snapshot, records.Database, SimulationSystems.Default(), Tree()).StateHash(),
            WorldSnapshot.LoadGame(snapshot, records.Database, SimulationSystems.Default(), Tree()).StateHash());

        CampaignProfileStore store = Store();
        string path = Saver(store).Save(simulation, simulation.World.Carry,
            new SessionContext("Level1", GameSessionMode.Campaign, FrontEndPage.None), Saves("chain"));
        GameSimulation loaded = new SessionLoader(store, LoadInto(store), name => CampaignLevelData.Load(install, name))
            .Load(path).Resume!.Simulation;

        Assert.Equal(7, loaded.World.TileAtIndex(heart).SlabOrder);
        Assert.Equal(simulation.StateHash(), loaded.StateHash());
    }
}
