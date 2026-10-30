// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;
using System.Numerics;

namespace DK2.Presentation.Tests;

public class GameBindingInputTests
{
    private static GameBindingInput Input(GameSettings? settings = null, KeyModifiers modifiers = KeyModifiers.None, params int[] scans)
        => new(settings ?? new GameSettings(), modifiers, s => scans.Contains(s.Value), s => scans.Contains(s.Value));

    [Fact]
    public void ReboundHeartReplacesTheDefaultAndRequiresExactModifiers()
    {
        var settings = new GameSettings { Bindings = new[] { new GameKeyBinding(GameAction.Heart, 0x30, KeyModifiers.Control) } };
        Assert.Equal(ClassicHudAction.None, Input(settings, scans: 0x23).HudShortcut(new()));
        Assert.Equal(ClassicHudAction.None, Input(settings, scans: 0x30).HudShortcut(new()));
        Assert.Equal(ClassicHudAction.NavigateHeart, Input(settings, KeyModifiers.Control, 0x30).HudShortcut(new()));
        Assert.Equal(ClassicHudAction.None, Input(settings, KeyModifiers.Control | KeyModifiers.Shift, 0x30).HudShortcut(new()));
    }
    [Fact]
    public void DirectCameraAndObjectActionsRejectExtraModifiersButScrollCombinesWithCameraModifier()
    {
        var input = Input(modifiers: KeyModifiers.Control, scans: new[] { 0x1d, 0xcb, 0xc7, 0xd3, 0xc9 });
        Assert.False(input.Held(GameAction.ZoomIn, exactModifiers: true));
        Assert.False(input.Held(GameAction.RotateLeft, exactModifiers: true));
        Assert.False(input.Held(GameAction.ObjectsOnly, exactModifiers: true));
        Assert.True(input.Held(GameAction.ScrollLeft));
        Assert.Equal(ClassicCameraKeys.TurnLeft, input.CameraKeys);
    }
    [Fact]
    public void CameraModifierAndFastScrollUseTheirReboundKeys()
    {
        var settings = new GameSettings { Bindings = new[] { new GameKeyBinding(GameAction.CameraModifier, 0x10), new GameKeyBinding(GameAction.FastScroll, 0x11) } };
        Assert.Equal(ClassicCameraKeys.TurnRight, Input(settings, scans: new[] { 0x10, 0xcd }).CameraKeys);
        Assert.Equal(ClassicCameraKeys.Right | ClassicCameraKeys.Fast, Input(settings, scans: new[] { 0x11, 0xcd }).CameraKeys);
    }
    [Fact]
    public void ModalPrecedenceAllowsOnlyFullMapCloseAndRejectsEditingKeys()
    {
        var all = Input(scans: new[] { 1, 0x22, 0x23, 0x32, 0x19, 0x21 });
        Assert.Equal(ClassicHudAction.ToggleFullMap, all.HudShortcut(new() { FullMapVisible = true }));
        Assert.Equal(ClassicHudAction.None, all.HudShortcut(new() { CapturingKey = true }));
        Assert.Equal(ClassicHudAction.None, all.HudShortcut(new() { EditingText = true, OptionsVisible = true }));
        Assert.Equal(ClassicHudAction.None, all.HudShortcut(new() { OrdinaryInput = false }));
        Assert.Equal(ClassicHudAction.ToggleOptions, all.HudShortcut(new() { OptionsVisible = true }));
        Assert.Equal(ClassicHudAction.None, Input(scans: 1).HudShortcut(new() { AllowOptionsKey = false }));
        Assert.Equal(ClassicHudAction.ToggleFullMap, Input().HudShortcut(new() { FullMapVisible = true }, escapePressed: true));
        Assert.Equal(ClassicHudAction.NavigateFight, Input(scans: 0x21).HudShortcut(new()));
    }
    [Fact]
    public void ModifierRolesAcceptEitherPhysicalSideWithoutWeakeningDirectActions()
    {
        var input = Input(modifiers: KeyModifiers.Control | KeyModifiers.Shift, scans: 0xcd);
        Assert.True(input.Held(GameAction.CameraModifier));
        Assert.True(input.Held(GameAction.FastScroll));
        Assert.Equal(ClassicCameraKeys.TurnRight | ClassicCameraKeys.Fast, input.CameraKeys);
    }
    [Fact]
    public void AdapterSensitivityHasAnExplicitNeutralAndScalesOnlyPointerDelta()
    {
        var delta = new Vector2(4, -6);
        Assert.Equal(delta, GameBindingInput.HostPointerDelta(delta, 10));
        Assert.Equal(delta / 2, GameBindingInput.HostPointerDelta(delta, 5));
        Assert.Equal(delta * 2, GameBindingInput.HostPointerDelta(delta, 20));
    }
    [Fact]
    public void IntegratedHudActionValuesAreUniqueAndRetainExistingFirstNine()
    {
        var values = Enum.GetValues<ClassicHudAction>();
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(8, (int)ClassicHudAction.ZoomOut);
        Assert.NotEqual(ClassicHudAction.ShowObjective, ClassicHudAction.NavigatePortal);
    }
}
