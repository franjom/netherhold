// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
namespace DK2.Presentation.Tests;
public sealed class ClassicHudControlsTests
{
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
}
