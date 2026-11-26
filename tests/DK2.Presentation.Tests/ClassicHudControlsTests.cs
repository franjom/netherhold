// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Media;
using DK2.Simulation.Commands;
namespace DK2.Presentation.Tests;
public sealed class ClassicHudControlsTests
{
    [Fact]
    public void OptionsToggleRetainsTheNativeSilentRequestsAndBlockedTogglesEmitNothing()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ToggleGui);
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.Equal(new uint[] { 0x139, 0x13a }, controls.Sounds.Pending.Select(r => r.Id.Value));
        Assert.All(controls.Sounds.Pending, sound => Assert.Equal(SoundCategory.None, sound.Category));
    }

    [Fact]
    public void RestartNeedsConfirmationAndCancelReturnsToOptions()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.RestartLevel);
        Assert.Equal(ClassicOptionsPage.Confirmation, controls.OptionsPage);
        Assert.Equal(ClassicSessionAction.None, controls.TakeSessionAction());
        controls.Apply(ClassicHudAction.CancelConfirmation);
        Assert.Equal(ClassicOptionsPage.Root, controls.OptionsPage);
        controls.Apply(ClassicHudAction.RestartLevel);
        controls.Apply(ClassicHudAction.Confirm);
        Assert.Equal(ClassicSessionAction.Restart, controls.TakeSessionAction());
        Assert.Equal(ClassicSessionAction.None, controls.TakeSessionAction());
    }

    [Theory]
    [InlineData(ClassicHudAction.ReturnToMenu, ClassicSessionAction.ReturnToMenu)]
    [InlineData(ClassicHudAction.QuitGame, ClassicSessionAction.Quit)]
    public void EndGameOffersExitDestinationsAndCancelKeepsTheExitPage(ClassicHudAction action, ClassicSessionAction expected)
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.EndGame);
        Assert.Equal(ClassicOptionsPage.Exit, controls.OptionsPage);
        controls.Apply(action);
        Assert.Equal(ClassicOptionsPage.Confirmation, controls.OptionsPage);
        controls.Apply(ClassicHudAction.CancelConfirmation);
        Assert.Equal(ClassicOptionsPage.Exit, controls.OptionsPage);
        controls.Apply(action);
        controls.Apply(ClassicHudAction.Confirm);
        Assert.Equal(expected, controls.TakeSessionAction());
    }

    [Fact]
    public void PendingResultRejectsEndAndRestartAndConfirmRequiresADialog()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.Confirm);
        Assert.Equal(ClassicSessionAction.None, controls.TakeSessionAction());
        controls.UpdateResult(new HudResultState(HasResult: true));
        controls.Apply(ClassicHudAction.EndGame);
        controls.Apply(ClassicHudAction.RestartLevel);
        Assert.Equal(ClassicOptionsPage.Root, controls.OptionsPage);
        Assert.Equal(ClassicSessionAction.None, controls.TakeSessionAction());
    }

    [Fact]
    public void OptionsObjectiveKeepsPauseUntilContinueAndBackReturnsToRows()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ShowObjective);
        Assert.True(controls.ObjectiveVisible);
        Assert.True(controls.OptionsVisible);
        controls.Apply(ClassicHudAction.CloseObjective);
        Assert.False(controls.ObjectiveVisible);
        Assert.True(controls.OptionsVisible);
        controls.Apply(ClassicHudAction.ShowObjective);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.ObjectiveVisible);
        Assert.False(controls.OptionsVisible);
    }

    [Fact]
    public void OptionsObjectiveRequiresOptionsAndRejectsWorldHotkeysAndPendingResult()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ShowObjective);
        Assert.False(controls.ObjectiveVisible);
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ShowObjective);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        controls.Apply(ClassicHudAction.ToggleGui);
        Assert.False(controls.FullMapVisible);
        Assert.True(controls.GuiVisible);
        controls.Apply(ClassicHudAction.CloseObjective);
        controls.UpdateResult(new HudResultState(HasResult: true));
        controls.Apply(ClassicHudAction.ShowObjective);
        Assert.False(controls.ObjectiveVisible);
    }

    [Fact]
    public void ScriptedCameraRetainsResultInputSuppressionUntilExplicitRestoration()
    {
        var controls = new ClassicHudControls();
        controls.UpdateResult(new HudResultState(HasResult: true));
        controls.UpdateResult(default, CameraModeId.ScriptedPath);
        Assert.True(controls.InputSuppressed);
        controls.UpdateResult(new HudResultState(Continued: true, RestoreRequested: true), CameraModeId.ScriptedPath);
        Assert.False(controls.InputSuppressed);
    }

    [Fact]
    public void PendingResultHidesHudAndBlocksWorldInputEvenWithOptionsOpen()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.UpdateResult(new HudResultState(HasResult: true));
        Assert.False(controls.Visibility(new(true, true)).ShowNormalHud);
        Assert.False(controls.Visibility(new(true, true)).AllowOrdinaryInput);
        controls.Apply(ClassicHudAction.ToggleGui);
        Assert.False(controls.GuiVisible);
    }

    [Fact]
    public void ClearingResultAloneReleasesInputButDoesNotRestoreGui()
    {
        var controls = new ClassicHudControls();
        controls.UpdateResult(new HudResultState(HasResult: true));
        controls.UpdateResult(default);
        Assert.False(controls.GuiVisible);
        Assert.True(controls.Visibility(new(true, true)).AllowOrdinaryInput);
        controls.UpdateResult(new HudResultState(Continued: true));
        Assert.False(controls.GuiVisible);
        controls.UpdateResult(new HudResultState(Continued: true, RestoreRequested: true));
        Assert.True(controls.GuiVisible);
    }

    [Fact]
    public void ResultRestorationDoesNotOverrideTheCameraGateOrCloseOptions()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.UpdateResult(new HudResultState(HasResult: true));
        controls.UpdateResult(new HudResultState(Continued: true, RestoreRequested: true));
        Assert.True(controls.OptionsVisible);
        Assert.False(controls.Visibility(HudVisibilityRules.ForCamera(new(3), 1)).ShowNormalHud);
        Assert.True(controls.Visibility(HudVisibilityRules.ForCamera(new(3), 0)).ShowNormalHud);
    }

    [Fact]
    public void OptionsRejectsMapAndGuiTogglesUntilClosed()
    {
        var controls=new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        controls.Apply(ClassicHudAction.ToggleGui);
        Assert.True(controls.OptionsVisible);
        Assert.True(controls.GuiVisible);
        Assert.False(controls.FullMapVisible);
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.True(controls.FullMapVisible);
    }
    [Fact]
    public void HiddenGuiStillAllowsWorldInputAndOptionsRestoresGui()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleGui);
        Assert.False(controls.Visibility(new(true, true)).ShowNormalHud);
        Assert.True(controls.Visibility(new(true, true)).AllowOrdinaryInput);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.GuiVisible);
        Assert.True(controls.OptionsVisible);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.OptionsVisible);
    }
    [Theory]
    [InlineData(8,584,ClassicHudAction.SelectQuery)]
    [InlineData(155.2f,584,ClassicHudAction.SelectSell)]
    [InlineData(8,728,ClassicHudAction.ToggleOptions)]
    [InlineData(153.6f,729.6f,ClassicHudAction.ZoomIn)]
    [InlineData(40,600,ClassicHudAction.None)]
    public void CornerRegionsUseNativeHalfOpenBounds(float x,float y,ClassicHudAction expected)
        => Assert.Equal(expected, ClassicHudControlLayout.ActionAt(new Vector2(x,y), false));
    [Fact]
    public void OnlyMagnifierAcceptsSecondaryChannel()
    {
        Assert.Equal(ClassicHudAction.ZoomOut, ClassicHudControlLayout.ActionAt(new(160,740), true));
        Assert.Equal(ClassicHudAction.None, ClassicHudControlLayout.ActionAt(new(16,590), true));
    }
    [Fact]
    public void FullMapSuppressesNormalHudAndOnlyItsToggleIsDispatched()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleFullMap);
        controls.Apply(ClassicHudAction.ToggleGui);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.GuiVisible);
        Assert.False(controls.OptionsVisible);
        Assert.False(controls.Visibility(new(true,true)).ShowNormalHud);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.True(controls.Visibility(new(true,true)).ShowNormalHud);
    }

    [Fact]
    public void TheOptionsMenuShowsOnlyOnItsOwnPagesWhileOpen()
    {
        var controls = new ClassicHudControls();
        Assert.False(controls.OptionsMenuVisible);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.OptionsMenuVisible);
        controls.Apply(ClassicHudAction.RestartLevel);
        Assert.True(controls.OptionsMenuVisible);   // the confirmation page is the menu's own
        controls.Apply(ClassicHudAction.CancelConfirmation);
        controls.Apply(ClassicHudAction.ShowObjective);
        Assert.True(controls.OptionsMenuVisible);   // so is the objective page's Continue and Back
        controls.Apply(ClassicHudAction.BackToOptions);
        controls.Apply(ClassicHudAction.EndGame);
        Assert.Equal(ClassicOptionsPage.Exit, controls.OptionsPage);
        Assert.True(controls.OptionsMenuVisible);   // the exit page
        controls.Apply(ClassicHudAction.ReturnToMenu);
        Assert.Equal((ClassicOptionsPage.Confirmation, ClassicSessionAction.ReturnToMenu), (controls.OptionsPage, controls.Confirmation));
        Assert.True(controls.OptionsMenuVisible);   // and the confirmation reached from it
        foreach (ClassicHudAction other in new[] { ClassicHudAction.OpenSave, ClassicHudAction.OpenLoad, ClassicHudAction.OpenGameOptions })
        {
            controls.Apply(ClassicHudAction.BackToOptions);
            controls.Apply(other);
            Assert.True(controls.OptionsVisible);
            Assert.False(controls.OptionsMenuVisible);
        }
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.OptionsMenuVisible);
    }

    [Fact]
    public void TheSaveBrowserShowsOnlyOnTheSaveAndLoadPagesWhileOpen()
    {
        var controls = new ClassicHudControls();
        Assert.False(controls.SaveLoadVisible);
        controls.Apply(ClassicHudAction.OpenSave);
        Assert.False(controls.SaveLoadVisible);   // the menu is closed
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.SaveLoadVisible);
        controls.Apply(ClassicHudAction.OpenSave);
        Assert.True(controls.SaveLoadVisible);
        Assert.True(controls.EditingText);
        controls.Apply(ClassicHudAction.BackToOptions);
        controls.Apply(ClassicHudAction.OpenLoad);
        Assert.True(controls.SaveLoadVisible);
        Assert.False(controls.EditingText);
        controls.Apply(ClassicHudAction.BackToOptions);
        controls.Apply(ClassicHudAction.OpenGameOptions);
        Assert.False(controls.SaveLoadVisible);
        controls.Apply(ClassicHudAction.BackToOptions);
        controls.Apply(ClassicHudAction.OpenLoad);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.SaveLoadVisible);
    }

    [Fact]
    public void TheSaveBrowserShowsWithTheNormalHudAndSavesOnlyOnTheSavePage()
    {
        var controls = new ClassicHudControls();
        var shown = new HudVisibility(true, true);
        Assert.Equal((false, false), controls.SaveLoadPage(shown));
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.OpenSave);
        Assert.Equal((true, true), controls.SaveLoadPage(shown));
        Assert.Equal((true, true), controls.SaveLoadPage(new HudVisibility(true, false)));   // input does not matter
        Assert.Equal((false, true), controls.SaveLoadPage(new HudVisibility(false, true)));  // a hidden HUD hides it
        controls.Apply(ClassicHudAction.BackToOptions);
        controls.Apply(ClassicHudAction.OpenLoad);
        Assert.Equal((true, false), controls.SaveLoadPage(shown));
        controls.Apply(ClassicHudAction.BackToOptions);
        Assert.Equal((false, false), controls.SaveLoadPage(shown));
    }

    [Fact]
    public void TheOptionsMenuAndTheFullMapAreModalAndHoldPanelInputButNotTheirShowing()
    {
        var controls = new ClassicHudControls();
        var shown = new HudVisibility(true, true);
        Assert.False(controls.Modal);
        Assert.Equal(shown, controls.ModalInput(shown));
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.Modal);
        Assert.Equal(new HudVisibility(true, false), controls.ModalInput(shown));
        Assert.Equal(new HudVisibility(false, false), controls.ModalInput(new HudVisibility(false, true)));
        controls.Apply(ClassicHudAction.ToggleOptions);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.True(controls.Modal);
        Assert.Equal(new HudVisibility(true, false), controls.ModalInput(shown));
    }

    [Fact]
    public void TurnsPauseOnlyWhileTheOptionsMenuIsOpen()
    {
        var controls = new ClassicHudControls();
        Assert.False(controls.TicksPaused);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.False(controls.TicksPaused);   // the full map does not pause
        controls.Apply(ClassicHudAction.ToggleFullMap);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.TicksPaused);
        controls.Apply(ClassicHudAction.OpenGameOptions);
        Assert.True(controls.TicksPaused);   // on every page of it
    }

    [Fact]
    public void TheShortcutContextCarriesTheStateAndTextIsEditedOnlyOnTheSavePage()
    {
        var controls = new ClassicHudControls();
        GameShortcutContext closed = controls.ShortcutContext(new HudVisibility(true, true), allowOptionsKey: true, capturingKey: false);
        Assert.Equal(new GameShortcutContext { OrdinaryInput = true, AllowOptionsKey = true }, closed);
        Assert.False(controls.ShortcutContext(new HudVisibility(true, false), true, false).OrdinaryInput);
        Assert.False(controls.ShortcutContext(new HudVisibility(true, true), allowOptionsKey: false, false).AllowOptionsKey);
        Assert.True(controls.ShortcutContext(new HudVisibility(true, true), true, capturingKey: true).CapturingKey);
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.EditingText);
        controls.Apply(ClassicHudAction.OpenLoad);
        Assert.False(controls.EditingText);
        controls.Apply(ClassicHudAction.BackToOptions);
        controls.Apply(ClassicHudAction.OpenSave);
        Assert.True(controls.EditingText);
        GameShortcutContext saving = controls.ShortcutContext(new HudVisibility(true, true), true, false);
        Assert.True(saving.OptionsVisible && saving.EditingText && !saving.FullMapVisible);
        controls.UpdateResult(new HudResultState(HasResult: true));
        Assert.False(controls.ShortcutContext(new HudVisibility(true, true), true, false).OrdinaryInput);   // input suppressed
    }

    [Fact]
    public void TheFullMapShowsWhileTheCameraShowsTheNormalHudAndHidesTheNormalHud()
    {
        var controls = new ClassicHudControls();
        var shown = new HudVisibility(true, true);
        Assert.False(controls.FullMapShown(shown));
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.True(controls.FullMapShown(shown));
        Assert.True(controls.FullMapShown(new HudVisibility(true, false)));    // input does not matter
        Assert.False(controls.FullMapShown(new HudVisibility(false, true)));   // a scripted view that hides the HUD hides it
        Assert.False(controls.Visibility(shown).ShowNormalHud);               // the panels hide under it
        Assert.True(controls.Modal);
        controls.Apply(ClassicHudAction.ToggleFullMap);
        Assert.False(controls.FullMapShown(shown));
        Assert.True(controls.Visibility(shown).ShowNormalHud);
    }

    [Fact]
    public void TheMenuPageAndTheGameOptionsPagesShowUnderTheNormalHudOnly()
    {
        var controls = new ClassicHudControls();
        var shown = new HudVisibility(true, true);
        Assert.False(controls.OptionsMenuShown(shown));
        Assert.False(controls.GameOptionsShown(shown));
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(controls.OptionsMenuShown(shown));
        Assert.True(controls.OptionsMenuShown(new HudVisibility(true, false)));   // input does not matter
        Assert.False(controls.OptionsMenuShown(new HudVisibility(false, true)));  // a hidden HUD hides it
        Assert.False(controls.GameOptionsShown(shown));
        controls.Apply(ClassicHudAction.OpenGameOptions);
        Assert.False(controls.OptionsMenuShown(shown));
        Assert.True(controls.GameOptionsShown(shown));
        Assert.True(controls.GameOptionsShown(new HudVisibility(true, false)));
        Assert.False(controls.GameOptionsShown(new HudVisibility(false, true)));
        controls.Apply(ClassicHudAction.ToggleOptions);
        Assert.False(controls.GameOptionsShown(shown));
    }

    private static readonly ClassicHudAction[] HostActions =
    {
        ClassicHudAction.NavigateHeart, ClassicHudAction.NavigatePortal, ClassicHudAction.NavigateFight,
        ClassicHudAction.SelectQuery, ClassicHudAction.SelectSell, ClassicHudAction.ZoomIn, ClassicHudAction.ZoomOut,
    };

    [Fact]
    public void AHostActsOnNoActionWhileInputIsSuppressedAndOnNoCameraOrMinimapActionWhileModal()
    {
        var controls = new ClassicHudControls();
        Assert.False(controls.Accepts(ClassicHudAction.None));
        foreach (ClassicHudAction action in HostActions)
        {
            Assert.True(controls.Accepts(action));
        }

        Assert.True(controls.Accepts(ClassicHudAction.ToggleOptions));

        controls.Apply(ClassicHudAction.ToggleOptions);
        foreach (ClassicHudAction action in HostActions)
        {
            Assert.False(controls.Accepts(action));
        }

        Assert.True(controls.Accepts(ClassicHudAction.ToggleOptions));
        Assert.True(controls.Accepts(ClassicHudAction.ToggleFullMap));   // Apply itself refuses it while the menu is open
        controls.Apply(ClassicHudAction.ToggleOptions);

        controls.Apply(ClassicHudAction.ToggleFullMap);
        foreach (ClassicHudAction action in HostActions)
        {
            Assert.False(controls.Accepts(action));
        }

        Assert.True(controls.Accepts(ClassicHudAction.ToggleFullMap));
        controls.Apply(ClassicHudAction.ToggleFullMap);

        controls.UpdateResult(new HudResultState(HasResult: true));
        Assert.False(controls.Accepts(ClassicHudAction.ToggleOptions));
        Assert.False(controls.Accepts(ClassicHudAction.ZoomIn));
    }

    [Fact]
    public void TheMagnifierPostsTheMinimapZoomButtonAndTheToolCornersTheirToolsButtons()
    {
        Assert.True(ClassicHudControlLayout.TryGuiButton(ClassicHudAction.ZoomIn, out GuiButtonTarget zoomIn));
        Assert.True(ClassicHudControlLayout.TryGuiButton(ClassicHudAction.ZoomOut, out GuiButtonTarget zoomOut));
        Assert.Equal(GuiButtonTarget.MinimapZoom, zoomIn);
        Assert.Equal(GuiButtonTarget.MinimapZoom, zoomOut);
        Assert.True(ClassicHudControlLayout.TryGuiButton(ClassicHudAction.SelectQuery, out GuiButtonTarget query));
        Assert.Equal(GuiButtonTarget.Query, query);
        Assert.True(ClassicHudControlLayout.TryGuiButton(ClassicHudAction.SelectSell, out GuiButtonTarget sell));
        Assert.Equal(GuiButtonTarget.Sell, sell);
        foreach (ClassicHudAction other in new[] { ClassicHudAction.None, ClassicHudAction.ToggleOptions, ClassicHudAction.ToggleFullMap,
            ClassicHudAction.NavigateHeart })
        {
            Assert.False(ClassicHudControlLayout.TryGuiButton(other, out _));
        }
    }
}
