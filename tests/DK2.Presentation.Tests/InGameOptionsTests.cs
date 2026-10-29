// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation.Tests;

public class InGameOptionsTests
{
    private static (ClassicHudControls Hud, OptionsPagesController Settings, InGameOptionsController Model) Open()
    {
        var hud = new ClassicHudControls(); hud.Apply(ClassicHudAction.ToggleOptions);
        var settings = new OptionsPagesController(new GameSettings());
        var model = new InGameOptionsController(hud, settings); model.Open();
        return (hud, settings, model);
    }
    [Fact]
    public void ChildPagesKeepOptionsPauseAndBackReturnsThroughNativeParents()
    {
        var (hud, settings, model) = Open();
        Assert.True(hud.GameOptionsVisible); Assert.Equal(InGameOptionsPage.Root, model.Page);
        Assert.True(model.Navigate(InGameOptionsPage.Controls));
        Assert.True(model.Navigate(InGameOptionsPage.Keyboard));
        model.Back(); Assert.Equal(InGameOptionsPage.Controls, model.Page); Assert.True(hud.OptionsVisible);
        model.Back(); Assert.Equal(InGameOptionsPage.Root, model.Page); Assert.True(hud.OptionsVisible);
        model.Back(); Assert.Equal(ClassicOptionsPage.Root, hud.OptionsPage); Assert.True(hud.OptionsVisible);
    }
    [Fact]
    public void InGameGammaAndGameSpeedApplyLiveAndBackRetainsTheirValues()
    {
        var (hud, settings, model) = Open();
        model.Navigate(InGameOptionsPage.Graphics); settings.Edit(s => s with { Gamma = 45 });
        Assert.Equal(45, settings.Current.Gamma); model.Back();
        Assert.Equal(45, settings.Current.Gamma); model.Navigate(InGameOptionsPage.Controls);
        settings.Edit(s => s with { GameSpeed = 9 }); Assert.Equal(9, settings.Current.GameSpeed);
        model.Continue(); Assert.False(hud.OptionsVisible); Assert.Equal(9, settings.Current.GameSpeed);
    }
    [Fact]
    public void BackCancelsPendingCaptureButKeepsAnAcceptedBinding()
    {
        var (hud, settings, model) = Open(); model.Navigate(InGameOptionsPage.Controls); model.Navigate(InGameOptionsPage.Keyboard);
        Assert.True(settings.BeginKeyCapture(GameAction.Heart)); model.Back();
        Assert.Null(settings.CapturingAction); Assert.Equal(0x23, settings.Current.Binding(GameAction.Heart).ScanCode);
        model.Navigate(InGameOptionsPage.Keyboard); settings.BeginKeyCapture(GameAction.Heart); settings.CaptureKey(0x20, KeyModifiers.None);
        model.Back(); model.Continue(); Assert.Equal(0x20, settings.Current.Binding(GameAction.Heart).ScanCode);
    }
    [Fact]
    public void NativeCatalogueRetainsPagesAndInGameContinueBackLayout()
    {
        Assert.Equal(8, ClassicGameOptionsLayout.Pages.Count);
        var root = ClassicGameOptionsLayout.ForPage(InGameOptionsPage.Root);
        Assert.Contains(root, w => w.TextId == 98 && w.Destination == InGameOptionsPage.Controls);
        Assert.Contains(root, w => w.TextId == 1485 && w.Destination == InGameOptionsPage.UserCameras);
        var gamma = ClassicGameOptionsLayout.ForPage(InGameOptionsPage.Graphics).Single(w => w.Setting.Value == 126);
        Assert.Equal(new FrontEndRect(584, 168, 376, 25.6f), gamma.Bounds);
        Assert.Contains(root, w => w.TextId == 142 && w.Bounds.Y == 512);
    }
    [Fact]
    public void WorldAndPanelActionsStaySuppressedForTheWholeEditingFamily()
    {
        var (hud, settings, model) = Open(); model.Navigate(InGameOptionsPage.Controls);
        foreach (ClassicHudAction action in new[] { ClassicHudAction.ToggleGui, ClassicHudAction.ToggleFullMap,
            ClassicHudAction.NavigateHeart, ClassicHudAction.SelectSell, ClassicHudAction.ZoomIn }) hud.Apply(action);
        Assert.True(hud.GameOptionsVisible); Assert.True(hud.GuiVisible); Assert.False(hud.FullMapVisible);
        Assert.False(model.Navigate(InGameOptionsPage.UserCameras));
    }
}
