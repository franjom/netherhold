// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation.Tests;

public class KeyboardSetupTests
{
    [Fact]
    public void RosterUsesNativeOrderAndLocalisedTextIds()
    {
        Assert.Equal(63, KeyboardSetup.Actions.Count);
        Assert.Equal(GameAction.Sniper, KeyboardSetup.Actions[0].Action);
        Assert.Equal(1748u, KeyboardSetup.Actions[0].TextId);
        Assert.Equal(GameAction.MessageAllies, KeyboardSetup.Actions[27].Action);
        Assert.False(KeyboardSetup.IsBuilt(GameAction.MessageAllies));
        Assert.True(KeyboardSetup.IsBuilt(GameAction.MouseRotate));
    }
    [Fact]
    public void RebindingBuiltActionIsLiveAndCancelRestoresIt()
    {
        var options = new OptionsPagesController(new GameSettings()); options.Enter(FrontEndPage.Controls);
        Assert.True(options.BeginKeyCapture(GameAction.Heart));
        Assert.True(options.CaptureKey(0x20, KeyModifiers.None));
        Assert.Equal(0x20, options.Current.Binding(GameAction.Heart).ScanCode);
        options.Cancel(); Assert.Equal(0x23, options.Current.Binding(GameAction.Heart).ScanCode);
    }
    [Fact]
    public void UnsupportedActionsCannotCaptureAndModifierAloneDoesNotFinish()
    {
        var options = new OptionsPagesController(new GameSettings()); options.Enter(FrontEndPage.Controls);
        Assert.False(options.BeginKeyCapture(GameAction.MessageAllies));
        Assert.True(options.BeginKeyCapture(GameAction.Heart));
        Assert.False(options.CaptureKey(0x1D, KeyModifiers.Control));
        Assert.Equal(GameAction.Heart, options.CapturingAction);
    }
    [Fact]
    public void TakingAnotherActionsKeyClearsItsOldBindingAndPersists()
    {
        var options = new OptionsPagesController(new GameSettings()); options.Enter(FrontEndPage.Controls);
        options.BeginKeyCapture(GameAction.Heart); options.CaptureKey(0x19, KeyModifiers.None);
        options.Accept();
        Assert.Equal(0, options.Current.Binding(GameAction.Portal).ScanCode);
        var restored = GameSettingsFile.Read(GameSettingsFile.Write(options.Current));
        Assert.Equal(0, restored.Binding(GameAction.Portal).ScanCode);
        Assert.Equal(0x19, restored.Binding(GameAction.Heart).ScanCode);
    }
    [Theory]
    [InlineData(GameAction.CameraModifier, KeyModifiers.Control | KeyModifiers.Alt, 0x1D, 0x2A)]
    [InlineData(GameAction.CameraModifier, KeyModifiers.Shift | KeyModifiers.Alt, 0x2A, 0x1D)]
    [InlineData(GameAction.FastScroll, KeyModifiers.Control | KeyModifiers.Alt, 0x1D, 0x2A)]
    [InlineData(GameAction.FastScroll, KeyModifiers.Shift | KeyModifiers.Alt, 0x2A, 0x1D)]
    public void CameraModifierCaptureIgnoresAltAndKeepsThePairedKeysOpposite(GameAction action, KeyModifiers modifiers, int expected, int other)
    {
        var options = new OptionsPagesController(new GameSettings()); options.Enter(FrontEndPage.Controls);
        Assert.True(options.BeginKeyCapture(action));
        Assert.True(options.CaptureKey(0x20, modifiers));
        Assert.Equal(expected, options.Current.Binding(action).ScanCode);
        GameAction paired = action == GameAction.CameraModifier ? GameAction.FastScroll : GameAction.CameraModifier;
        Assert.Equal(other, options.Current.Binding(paired).ScanCode);
        Assert.Equal(KeyModifiers.None, options.Current.Binding(action).Modifiers);
    }

}
