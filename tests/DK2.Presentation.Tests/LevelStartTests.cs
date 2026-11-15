// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The level-start and frame-pacing rules both engine adapters share, moved out of Unity's DungeonRunner
/// (migration plan step 1b): the panel's opening state, the stored-special click, tick pacing and the level load.</summary>
public sealed class LevelStartTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;

    private sealed class Art : IBottomPanelArt
    {
        public string? Icon(BottomPanelChoiceKey key) => "icon " + key.Value;
        public string Text(uint id) => "text " + id;
    }

    private static BottomPanelView View() =>
        new(new GameDatabase([], [], [], new GlobalVariables()), Keeper, (_, name) => name);

    [Fact]
    public void ThePanelStartsWithCreaturesOpenAndNothingClicked()
    {
        var hud = new BottomPanelHud(new Art(), View());
        var buttons = new List<GuiButtonTarget>();
        hud.GuiButton += buttons.Add;
        hud.OpenAtLevelStart();
        Assert.True(hud.OpenPanel.Contains(BottomPanelCategory.Creatures));
        Assert.Empty(buttons);
        Assert.Empty(hud.Sounds.Pending);

        hud.SelectCategory(BottomPanelCategory.Rooms);   // a real click posts its action and keeps its sound
        Assert.Single(buttons);
    }

    [Fact]
    public void AStoredSpecialPostsAction88OrReturnsThePanelToTheHand()
    {
        var input = new PlayerInputTranslator();
        BottomPanelView panel = View();
        panel.SelectTool(PlayerToolMode.CreateImp);

        ActivateSpecialCommand direct = Assert.IsType<ActivateSpecialCommand>(input.ChooseStoredSpecial(Keeper, 7, 124, panel));
        Assert.Equal(7, direct.ObjectId);
        Assert.Equal(PlayerToolMode.CreateImp, panel.Selection.Tool);

        Assert.Null(input.ChooseStoredSpecial(Keeper, 8, SpecialTargeting.KillCreatures, panel));
        Assert.Equal(new SpecialTargeting(8, SpecialTargeting.KillCreatures), input.Targeting);
        Assert.Equal(PlayerToolMode.Hand, panel.Selection.Tool);
        Assert.Throws<ArgumentNullException>(() => input.ChooseStoredSpecial(Keeper, 7, 124, null!));
    }

    [Fact]
    public void TicksAccrueWithSpeedAndAreTakenWholeUpToTheCap()
    {
        var pacer = new TickPacer();
        Assert.Equal(1.0, pacer.Accrue(0.25, 1f, 4));          // 4 ticks a second at normal speed
        Assert.Equal(1, pacer.Take(2));
        Assert.Equal(0.0, pacer.Debt);

        Assert.Equal(1.0, pacer.Accrue(0.125, 1f, 8));          // game speed 8 doubles it
        Assert.Equal(1.5, pacer.Accrue(0.375, 1f, 4), 6);
        Assert.Equal(2, pacer.Take(2));                         // 2.5 owed, capped at 2
        Assert.Equal(0.5, pacer.Debt, 6);
        Assert.Equal(0, pacer.Take(2));                         // a fraction waits for the next frame
        pacer.Accrue(10, 1f, 4);
        Assert.Equal(1, pacer.Take(0));                         // a cap below one still runs a tick
    }

    [Fact]
    public void OnlyTheFirstTickOfAFrameCarriesItsCommands()
    {
        var pending = new List<PlayerCommand> { new GuiButtonCommand(Keeper, GuiButtonTarget.ResizePanel) };
        Assert.Null(TickPacer.FirstInput(0, pending).Commands);   // no tick this frame: the commands wait
        InputFrame first = TickPacer.FirstInput(2, pending);
        Assert.Equal(pending, first.All);
        pending.Clear();
        Assert.Single(first.All);                                 // a copy, not the caller's list
        Assert.Equal(first, TickPacer.InputFor(0, first));
        Assert.Null(TickPacer.InputFor(1, first).Commands);
        Assert.Null(TickPacer.FirstInput(1, pending).Commands);
    }

    [InstallFact]
    public void ACampaignLevelLoadsItsRecordsAndBuildsAFreshSimulationEachTime()
    {
        CampaignLevelData level = CampaignLevelData.Load(Dk2InstallGate.Path, "Level1");
        Assert.Equal("Level1", level.Name);
        Assert.NotEmpty(level.Players);
        Assert.NotEmpty(level.Things);
        GameSimulation first = level.CreateSimulation(1, fogOfWarEnabled: true);
        GameSimulation second = level.CreateSimulation(1, fogOfWarEnabled: true);
        Assert.NotSame(first.World, second.World);
        for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame; i++) { first.Tick(); second.Tick(); }
        Assert.Equal(first.StateHash(), second.StateHash());
        Assert.Equal((long)CampaignLevelData.TicksBeforeFirstFrame, first.World.CurrentTick.Value);
    }

    [Fact]
    public void ALevelLoadNeedsAnInstallAndAName()
    {
        Assert.Throws<ArgumentNullException>(() => CampaignLevelData.Load(null!, "Level1"));
        Assert.Throws<ArgumentNullException>(() => CampaignLevelData.Load("x", null!));
    }
}
