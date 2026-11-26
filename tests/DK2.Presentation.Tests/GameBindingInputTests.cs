// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;
using System.Numerics;

namespace DK2.Presentation.Tests;

public class GameBindingInputTests
{
    [Theory]
    [InlineData(GameAction.Options, true)]
    [InlineData(GameAction.TogglePanel, true)]
    [InlineData(GameAction.Options, false)]
    [InlineData(GameAction.TogglePanel, false)]
    public void FlyInAndFlyOutRefuseOptionsAndGuiToggle(GameAction action, bool possessing)
    {
        var settings = new GameSettings { Bindings = [new GameKeyBinding(action, 0x30)] };
        // Entry keeps possession; return releases it, but both timed moves still hold the camera.
        GameShortcutContext context = PossessionHud.ShortcutContext(new(), possessionCameraHeld: true,
            possessing: possessing, firstPersonCameraSettled: false);
        Assert.Equal(ClassicHudAction.None, Input(settings, scans: 0x30).HudShortcut(context));
        Assert.Equal(ClassicHudAction.None, Input().HudShortcut(context, escapePressed: true));
    }

    [Theory]
    [InlineData(GameAction.Options, ClassicHudAction.ToggleOptions)]
    [InlineData(GameAction.TogglePanel, ClassicHudAction.ToggleGui)]
    [InlineData(GameAction.Heart, ClassicHudAction.None)]
    [InlineData(GameAction.FullMap, ClassicHudAction.None)]
    [InlineData(GameAction.Portal, ClassicHudAction.None)]
    [InlineData(GameAction.NextFight, ClassicHudAction.None)]
    public void PossessionAllowsOptionsAndGuiButNotOrdinaryNavigation(GameAction action, ClassicHudAction expected)
    {
        var settings = new GameSettings { Bindings = [new GameKeyBinding(action, 0x30)] };
        GameShortcutContext context = PossessionHud.ShortcutContext(new(), possessionCameraHeld: true,
            possessing: true, firstPersonCameraSettled: true);
        Assert.Equal(expected, Input(settings, scans: 0x30).HudShortcut(context));
    }

    [Theory]
    [InlineData(15, 0.5)]
    [InlineData(30, 1)]
    [InlineData(60, 2)]
    public void NativeGammaSliderConvertsToTheRampDivisor(int slider, double gamma) =>
        Assert.Equal(gamma, DisplaySettings.RampGamma(slider));

    [Fact]
    public void NoLivePossessedBodyKeepsShortcutsBlockedDuringTheDeathOrReturnView()
    {
        GameShortcutContext context = PossessionHud.ShortcutContext(new(), possessionCameraHeld: true, possessing: false);
        Assert.Equal(ClassicHudAction.None, Input(scans: 1).HudShortcut(context));
    }
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
        GameBindingInput input = Input(modifiers: KeyModifiers.Control, scans: new[] { 0x1d, 0xcb, 0xc7, 0xd3, 0xc9 });
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
        GameBindingInput all = Input(scans: new[] { 1, 0x22, 0x23, 0x32, 0x19, 0x21 });
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
        GameBindingInput input = Input(modifiers: KeyModifiers.Control | KeyModifiers.Shift, scans: 0xcd);
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
        ClassicHudAction[] values = Enum.GetValues<ClassicHudAction>();
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(8, (int)ClassicHudAction.ZoomOut);
        Assert.NotEqual(ClassicHudAction.ShowObjective, ClassicHudAction.NavigatePortal);
    }

    [Fact]
    public void ItAnswersTheHeldModifiersAndAnyKeyPressedThisFrame()
    {
        var keys = new GameBindingInput(new GameSettings(), KeyModifiers.Shift | KeyModifiers.Alt, _ => true, s => s.Value == 0x30);
        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Alt, keys.Modifiers);
        Assert.True(keys.KeyPressed(new KeyScanCode(0x30)));
        Assert.False(keys.KeyPressed(new KeyScanCode(0x31)));
    }
}
