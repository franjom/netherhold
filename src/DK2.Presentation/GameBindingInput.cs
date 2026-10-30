// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Settings;

namespace DK2.Presentation;

public readonly record struct GameShortcutContext
{
    public GameShortcutContext() { }
    public bool OrdinaryInput { get; init; } = true;
    public bool AllowOptionsKey { get; init; } = true;
    public bool OptionsVisible { get; init; }
    public bool FullMapVisible { get; init; }
    public bool EditingText { get; init; }
    public bool CapturingKey { get; init; }
}

/// <summary>Host binding resolution, independent of the keyboard platform and simulation.</summary>
public readonly struct GameBindingInput(GameSettings settings, KeyModifiers modifiers,
    Func<KeyScanCode, bool> held, Func<KeyScanCode, bool> pressed)
{
    public bool Held(GameAction action, bool exactModifiers = false) => Matches(action, held, exactModifiers);
    public bool Pressed(GameAction action) => Matches(action, pressed, exact: true);
    private bool Matches(GameAction action, Func<KeyScanCode, bool> read, bool exact)
    {
        if (!KeyboardSetup.IsBuilt(action)) return false;
        GameKeyBinding binding = settings.Binding(action);
        bool active = read(binding.ScanCode);
        if (action is GameAction.CameraModifier or GameAction.FastScroll)
        {
            KeyModifiers role = binding.ScanCode switch { 0x1d or 0x9d => KeyModifiers.Control, 0x2a or 0x36 => KeyModifiers.Shift, _ => KeyModifiers.None };
            if (role != KeyModifiers.None) active = (modifiers & role) != 0;
        }
        return binding.ScanCode != 0 && (exact ? modifiers == binding.Modifiers : (modifiers & binding.Modifiers) == binding.Modifiers) && active;
    }
    public ClassicHudAction HudShortcut(GameShortcutContext context, bool escapePressed = false)
    {
        if (!context.OrdinaryInput || context.CapturingKey || context.EditingText) return ClassicHudAction.None;
        if (context.FullMapVisible) return escapePressed || Pressed(GameAction.FullMap) ? ClassicHudAction.ToggleFullMap : ClassicHudAction.None;
        if (context.AllowOptionsKey && Pressed(GameAction.Options)) return ClassicHudAction.ToggleOptions;
        if (context.OptionsVisible) return ClassicHudAction.None;
        if (Pressed(GameAction.FullMap)) return ClassicHudAction.ToggleFullMap;
        if (Pressed(GameAction.TogglePanel)) return ClassicHudAction.ToggleGui;
        if (Pressed(GameAction.Heart)) return ClassicHudAction.NavigateHeart;
        if (Pressed(GameAction.Portal)) return ClassicHudAction.NavigatePortal;
        if (Pressed(GameAction.NextFight)) return ClassicHudAction.NavigateFight;
        return ClassicHudAction.None;
    }
    public ClassicCameraKeys CameraKeys
    {
        get
        {
            ClassicCameraKeys keys = ClassicCameraKeys.None;
            bool modifier = Held(GameAction.CameraModifier);
            if (Held(GameAction.RotateLeft, true) || modifier && Held(GameAction.ScrollLeft)) keys |= ClassicCameraKeys.TurnLeft;
            if (Held(GameAction.RotateRight, true) || modifier && Held(GameAction.ScrollRight)) keys |= ClassicCameraKeys.TurnRight;
            if (!modifier)
            {
                if (Held(GameAction.ScrollLeft)) keys |= ClassicCameraKeys.Left;
                if (Held(GameAction.ScrollRight)) keys |= ClassicCameraKeys.Right;
                if (Held(GameAction.ScrollUp)) keys |= ClassicCameraKeys.Up;
                if (Held(GameAction.ScrollDown)) keys |= ClassicCameraKeys.Down;
            }
            if (Held(GameAction.FastScroll)) keys |= ClassicCameraKeys.Fast;
            return keys;
        }
    }
    public bool ZoomIn => Held(GameAction.ZoomIn, true) || Held(GameAction.CameraModifier) && Held(GameAction.ScrollUp);
    public bool ZoomOut => Held(GameAction.ZoomOut, true) || Held(GameAction.CameraModifier) && Held(GameAction.ScrollDown);

    // Modern host mapping only: the native sensitivity consumer remains unresolved (Q687).
    public static Vector2 HostPointerDelta(Vector2 delta, int sensitivity) => delta * (Math.Clamp(sensitivity, 5, 20) / 10f);
}
