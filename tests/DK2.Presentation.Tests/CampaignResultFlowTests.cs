// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary><see cref="CampaignResultFlow"/>, moved out of Unity's DungeonRunner (LevelEnd, Finale) for both adapters:
/// the per-frame order (carry, statistics once in verdict order, the HUD's result state, the result once, the camera path
/// gate, the win keys, Space) and the commands it posts.</summary>
public sealed class CampaignResultFlowTests : IDisposable
{
    private const byte Keeper = 3, Rival = 4;
    private static readonly PlayerId Viewer = new(Keeper);
    private readonly string _root = Directory.CreateTempSubdirectory("dk2-result-").FullName;
    private readonly List<PlayerCommand> _pending = new();
    private int _profileLoads;

    private string ProfilePath => Path.Combine(_root, "campaign-profile.dk2p");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static GameSimulation Simulation()
    {
        TerrainDef terrain = new()
        {
            Name = "test", TerrainId = 2, Flags = TerrainFlags.Solid, StartingHealth = 1, MaxHealth = 1, GoldValue = 0,
            ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var variables = new GlobalVariables();
        foreach (VariableTypeId id in Enum.GetValues<VariableTypeId>())
        {
            variables.Misc[id] = new MiscVariable(id, 0, 0, 0);
        }

        variables.Misc[VariableTypeId.AverageTime] = new MiscVariable(VariableTypeId.AverageTime, 30, 0, 0);
        variables.Misc[VariableTypeId.LevelRating] = new MiscVariable(VariableTypeId.LevelRating, 4096, 0, 0);
        var database = new GameDatabase([terrain], [], [], variables);
        var map = new LevelMap(4, 4, [.. Enumerable.Repeat(new MapTile(2, PlayerIds.Unowned, 0, 0), 16)]);
        return new GameSimulation(database, WorldState.FromMap(database, map, [PlayerIds.Good, Keeper, Rival]), 7);
    }

    private sealed record Rig(CampaignResultFlow Flow, SessionContext Context, CampaignProfileStore Store, ClassicHudControls Hud,
        GameSimulation Simulation);

    /// <summary>A flow over a profile file written first; it plays <paramref name="level"/> from <paramref name="page"/>.</summary>
    private Rig Start(CampaignProfile profile, string level = "Level1", FrontEndPage page = FrontEndPage.CampaignResults,
        bool resumed = false, GameSimulation? simulation = null)
    {
        CampaignProfileFile.Save(ProfilePath, profile);
        var store = new CampaignProfileStore(import: null, () => new DateTime(2026, 11, 16));
        var context = new SessionContext(level, GameSessionMode.Campaign, page);
        var hud = new ClassicHudControls();
        var flow = new CampaignResultFlow(context, store, () =>
        {
            _profileLoads++;
            store.Load(ProfilePath, GameSessionMode.Campaign, page);
        }, hud) { LevelTicksPerSecond = 4 };
        simulation ??= Simulation();
        flow.Prepare(simulation, Viewer, resumed);
        return new Rig(flow, context, store, hud, simulation);
    }

    private bool Frame(Rig rig, bool path = false, bool space = false, bool escape = false, double now = 0) =>
        rig.Flow.Update(rig.Simulation.World, rig.Simulation.Database,
            new CampaignResultFrame(Viewer, path, now, SpeechPlaying: null, space, escape), _pending);

    private static void End(GameSimulation simulation, byte player, byte status, long tick = 0)
    {
        Assert.True(simulation.World.EndGame(simulation.Database, new PlayerId(player), status));
        simulation.World.Player(new PlayerId(player)).StatusTick = new GameTick(tick);
    }

    private CampaignProfile Saved() => CampaignProfileFile.LoadOrCreate(ProfilePath);

    [Fact]
    public void ALevelNotStartedFromTheMapHasNoResultAndNeverLoadsTheProfile()
    {
        Rig rig = Start(new CampaignProfile(), page: FrontEndPage.None);
        End(rig.Simulation, Keeper, PlayerStatus.Won);

        Assert.False(Frame(rig, space: true, escape: true));
        Assert.False(rig.Flow.LaunchedFromFrontEnd);
        Assert.False(rig.Flow.ResultRecorded);
        Assert.False(rig.Hud.InputSuppressed);
        Assert.Empty(_pending);
        Assert.Equal(0, _profileLoads);
        Assert.Null(rig.Flow.Prompt(rig.Simulation.World, Viewer, pathPlaying: false));
    }

    [Fact]
    public void AFreshStartClearsTheStoredTransferCreatureBeforeTheFirstTurnAndPlaysTheSlot()
    {
        // Type 200 is not in the database, so nothing arrives; the profile clears whatever happens.
        Rig rig = Start(new CampaignProfile { TransferCreatureId = 200, TransferCreatureLevel = 4 });

        Assert.Equal(1, _profileLoads);
        Assert.Equal(new CampaignSlot(1), rig.Context.PlayedSlot);
        Assert.Equal(0, rig.Store.Profile!.TransferCreatureId);
        Assert.Equal(0, Saved().TransferCreatureLevel);
        Assert.Equal(GameTick.Zero, rig.Simulation.World.CurrentTick);
    }

    /// <summary>The stored creature arrives in <see cref="CampaignResultFlow.Prepare"/>, before any turn, drawing its facing
    /// from the simulation's own stream: the world matches a copy of the level where <see cref="TransferCreatureRules.Arrive"/>
    /// was called directly at the same point.</summary>
    [InstallFact]
    public void AFreshStartBringsTheStoredCreatureInBeforeTheFirstTurnFromTheWorldStream()
    {
        var level = CampaignLevelData.Load(Dk2InstallGate.Path, "Level1");
        GameSimulation expected = level.CreateSimulation(1, fogOfWarEnabled: true);
        GameSimulation actual = level.CreateSimulation(1, fogOfWarEnabled: true);
        byte goblin = (byte)actual.Database.CreatureByName("Goblin")!.CreatureId;
        int before = actual.World.OwnedCreatures(Viewer).Count();

        Rig rig = Start(new CampaignProfile { TransferCreatureId = goblin, TransferCreatureLevel = 3 }, simulation: actual);
        CreatureState arrived = TransferCreatureRules.Arrive(expected, Viewer, goblin, 3)!;

        Assert.NotNull(arrived);
        Assert.Equal(GameTick.Zero, actual.World.CurrentTick);
        Assert.Equal(before + 1, actual.World.OwnedCreatures(Viewer).Count());
        CreatureState came = actual.World.CreatureById(arrived.Id)!;
        Assert.Equal((goblin, arrived.Heading, arrived.PosX, arrived.PosY), ((byte)came.CreatureTypeId, came.Heading, came.PosX, came.PosY));
        Assert.Equal(expected.StateHash(), actual.StateHash());
        Assert.Equal(expected.Rng.NextInt(1 << 20), actual.Rng.NextInt(1 << 20));
        Assert.Equal(0, Saved().TransferCreatureId);
        Assert.Equal(0, rig.Store.Profile!.TransferCreatureLevel);
    }

    [Fact]
    public void ARestoredSaveCountsItsCarryAsWrittenAndKeepsTheTransferCreature()
    {
        GameSimulation simulation = Simulation();
        simulation.World.Carry.HiddenLandFound = 2;
        Rig rig = Start(new CampaignProfile { TransferCreatureId = 200 }, resumed: true, simulation: simulation);

        Assert.Equal(2, rig.Context.CarryApplied.HiddenLandFound);
        Assert.Equal(200, rig.Store.Profile!.TransferCreatureId);
    }

    [Fact]
    public void TheCarryIsWrittenBeforeTheStatistics()
    {
        // Level 5 is a map whose hidden land Locate Hidden Land unlocks (SecretLevels[0]).
        Rig rig = Start(new CampaignProfile { LevelNumber = 5 }, level: "Level5");
        rig.Simulation.World.Carry.HiddenLandFound = 1;
        End(rig.Simulation, Keeper, PlayerStatus.Lost);
        // The first profile write of the frame fails, so the frame stops after whichever step wrote first.
        File.Delete(ProfilePath);
        Directory.CreateDirectory(ProfilePath);

        Assert.ThrowsAny<Exception>(() => Frame(rig));
        Assert.Equal(1u, rig.Store.Profile!.SecretLevels[0]);
        Assert.Equal(0u, rig.Store.Profile.PlayerStatisticsValid[Keeper]);
    }

    [Fact]
    public void EachPlayersStatisticsAreRecordedOnceInVerdictOrder()
    {
        Rig rig = Start(new CampaignProfile());
        // The rival's verdict came first, so the viewer's block is written last and its rating is the map's.
        End(rig.Simulation, Keeper, PlayerStatus.Lost, tick: 9);
        End(rig.Simulation, Rival, PlayerStatus.Won, tick: 5);
        rig.Simulation.World.Player(Viewer).Statistics.LandOwned = 900;
        uint keeperRating = LevelStatisticsRules.Block(rig.Simulation.World.Player(Viewer), 4).TotalEvilRating;
        uint rivalRating = LevelStatisticsRules.Block(rig.Simulation.World.Player(new PlayerId(Rival)), 4).TotalEvilRating;
        Assert.NotEqual(keeperRating, rivalRating);

        Frame(rig);
        Assert.Equal(1u, Saved().PlayerStatisticsValid[Keeper]);
        Assert.Equal(1u, Saved().PlayerStatisticsValid[Rival]);
        Assert.Equal(keeperRating, Saved().TotalEvilRating[0]);

        // A later frame writes neither block again.
        rig.Store.Profile = rig.Store.Profile! with { PlayerStatisticsValid = new uint[8], TotalEvilRating = new uint[40] };
        Frame(rig);
        Assert.Equal(0u, rig.Store.Profile.PlayerStatisticsValid[Keeper]);
        Assert.Equal(0u, rig.Store.Profile.PlayerStatisticsValid[Rival]);
    }

    [Fact]
    public void AVerdictOnTheSameTurnIsRecordedInPlayerOrder()
    {
        Rig rig = Start(new CampaignProfile());
        End(rig.Simulation, Rival, PlayerStatus.Won, tick: 5);
        End(rig.Simulation, Keeper, PlayerStatus.Lost, tick: 5);
        rig.Simulation.World.Player(new PlayerId(Rival)).Statistics.LandOwned = 900;
        uint rivalRating = LevelStatisticsRules.Block(rig.Simulation.World.Player(new PlayerId(Rival)), 4).TotalEvilRating;
        Assert.NotEqual(LevelStatisticsRules.Block(rig.Simulation.World.Player(Viewer), 4).TotalEvilRating, rivalRating);

        Frame(rig);
        Assert.Equal(rivalRating, Saved().TotalEvilRating[0]);
    }

    [Fact]
    public void TheResultIsRecordedOnce()
    {
        Rig rig = Start(new CampaignProfile());
        End(rig.Simulation, Keeper, PlayerStatus.Lost);

        Frame(rig);
        Frame(rig);
        Assert.True(rig.Flow.ResultRecorded);
        Assert.Equal(1u, Saved().LevelAttempts[0]);
    }

    [Fact]
    public void TheCarryIsAppliedBeforeTheResultSoALossDropsAStoredCreature()
    {
        Rig rig = Start(new CampaignProfile());
        rig.Simulation.World.Carry = new CampaignCarry { TransferStored = true, TransferCreatureTypeId = 9, TransferCreatureLevel = 2 };
        End(rig.Simulation, Keeper, PlayerStatus.Lost);

        Frame(rig);
        Assert.Equal(0, Saved().TransferCreatureId);
        Assert.Equal(2, Saved().TransferCreatureLevel);
    }

    [Fact]
    public void TheHudTakesTheResultStateEveryFrame()
    {
        Rig rig = Start(new CampaignProfile());
        Frame(rig);
        Assert.False(rig.Hud.InputSuppressed);
        Assert.True(rig.Hud.GuiVisible);

        End(rig.Simulation, Keeper, PlayerStatus.Won);
        Frame(rig);
        Assert.True(rig.Hud.InputSuppressed);
        Assert.False(rig.Hud.GuiVisible);
    }

    [Fact]
    public void TheHudsResultStateIsToldWhetherACameraPathPlays()
    {
        Rig rig = Start(new CampaignProfile());
        // 0x004039A0 lifts a suppression only off a camera path; the verdict has not landed here.
        rig.Hud.UpdateResult(new HudResultState(HasResult: true));
        Assert.True(rig.Hud.InputSuppressed);

        Frame(rig, path: true);
        Assert.True(rig.Hud.InputSuppressed);
        Frame(rig);
        Assert.False(rig.Hud.InputSuppressed);
    }

    [Fact]
    public void WhileACameraPathPlaysTheResultIsRecordedButNoKeyActs()
    {
        Rig rig = Start(new CampaignProfile());
        End(rig.Simulation, Keeper, PlayerStatus.Won);

        Assert.False(Frame(rig, path: true, space: true));
        Assert.False(Frame(rig, path: true, escape: true));
        Assert.Empty(_pending);
        Assert.True(rig.Flow.ResultRecorded);
        Assert.Equal(CampaignLevelStatus.Won, Saved().PlayerLevelStatus[0]);
        Assert.Null(rig.Flow.Prompt(rig.Simulation.World, Viewer, pathPlaying: true));

        Assert.True(Frame(rig, space: true));
    }

    [Fact]
    public void BeforeTheVerdictSpaceDoesNothing()
    {
        Rig rig = Start(new CampaignProfile());
        Assert.False(Frame(rig, space: true, escape: true));
        Assert.False(rig.Flow.ResultRecorded);
        Assert.Empty(_pending);
    }

    [Fact]
    public void EscapeOnAWinRevealsTheMapThenWaitsForTheWinScreenToContinue()
    {
        Rig rig = Start(new CampaignProfile());
        var screen = new WinScreen([], _ => null);
        rig.Flow.WinScreen = screen;
        End(rig.Simulation, Keeper, PlayerStatus.Won);

        Assert.False(Frame(rig, escape: true, now: 100));
        Assert.Equal(new PlayerCommand[] { new RevealMapCommand(Viewer) }, _pending);
        Assert.True(rig.Flow.ContinueRequested);
        Assert.Equal("Continuing...", rig.Flow.Prompt(rig.Simulation.World, Viewer, pathPlaying: false));
        // Space does not leave while the continue is pending.
        Assert.False(Frame(rig, space: true, now: 200));

        // The screen runs (no rewards, so it is done at once) and takes the continue on its 500 ms beat.
        screen.Start(300);
        Assert.False(Frame(rig, now: 300));
        Assert.False(Frame(rig, now: 799));
        Assert.False(Frame(rig, now: 800));
        Assert.Equal(new PlayerCommand[] { new RevealMapCommand(Viewer), new ContinueAfterResultCommand(Viewer) }, _pending);
        Assert.False(rig.Flow.ContinueRequested);
        Assert.False(screen.Started);

        Assert.True(Frame(rig, space: true, now: 900));
    }

    [Fact]
    public void EscapeAfterAFinishedWinScreenContinuesThenRevealsTheMap()
    {
        Rig rig = Start(new CampaignProfile());
        var screen = new WinScreen([], _ => null);
        screen.Start(0);
        rig.Flow.WinScreen = screen;
        End(rig.Simulation, Keeper, PlayerStatus.Won);
        Assert.Equal("Press Space to leave, Escape to keep playing", Prompt(rig, firstFrame: true));

        // The done arm (0x004055E3): action 134 at 0x00405664, then action 75 at 0x00405697.
        Assert.False(Frame(rig, escape: true, space: true, now: 100));
        Assert.Equal(new PlayerCommand[] { new ContinueAfterResultCommand(Viewer), new RevealMapCommand(Viewer) }, _pending);
        Assert.False(rig.Flow.ContinueRequested);
        Assert.False(screen.Started);
    }

    [Fact]
    public void WithoutAWinScreenEscapeContinuesAtOnceAsWithAFinishedOne()
    {
        Rig rig = Start(new CampaignProfile());
        End(rig.Simulation, Keeper, PlayerStatus.Won);

        Assert.False(Frame(rig, escape: true));
        Assert.Equal(new PlayerCommand[] { new ContinueAfterResultCommand(Viewer), new RevealMapCommand(Viewer) }, _pending);
        Assert.False(rig.Flow.ContinueRequested);
        // Nothing is left pending, so Space still leaves while the verdict stands.
        Assert.True(Frame(rig, space: true));
    }

    [Fact]
    public void OnASecretLevelEscapeDoesNothingAndSpaceLeaves()
    {
        var secret = new CampaignSlot(26);
        Rig rig = Start(new CampaignProfile { SecretLevelNumber = 1 }, level: secret.LevelName!);
        Assert.Equal(secret, rig.Context.PlayedSlot);
        End(rig.Simulation, Keeper, PlayerStatus.Won);

        Assert.False(Frame(rig, escape: true));
        Assert.Empty(_pending);
        Assert.Equal("Press Space to leave", Prompt(rig, firstFrame: false));
        Assert.True(Frame(rig, space: true));
    }

    [Fact]
    public void ALossIgnoresEscapeAndSpaceLeaves()
    {
        Rig rig = Start(new CampaignProfile());
        End(rig.Simulation, Keeper, PlayerStatus.Lost);

        Assert.False(Frame(rig, escape: true));
        Assert.Empty(_pending);
        Assert.Equal("Press Space to leave", Prompt(rig, firstFrame: false));
        Assert.True(Frame(rig, space: true));
    }

    [Fact]
    public void AMapThatIsNotThePlayedSlotRecordsNothingButStillLeaves()
    {
        Rig rig = Start(new CampaignProfile(), level: "Level2");
        End(rig.Simulation, Keeper, PlayerStatus.Lost);

        Assert.True(Frame(rig, space: true));
        Assert.Equal(CampaignSlot.None, rig.Context.PlayedSlot);
        Assert.True(rig.Flow.ResultRecorded);
        Assert.Equal(0u, Saved().LevelAttempts[0]);
        Assert.Equal(0u, Saved().PlayerStatisticsValid[Keeper]);
    }

    private string? Prompt(Rig rig, bool firstFrame)
    {
        if (firstFrame)
        {
            Frame(rig);
        }

        return rig.Flow.Prompt(rig.Simulation.World, Viewer, pathPlaying: false);
    }
}
