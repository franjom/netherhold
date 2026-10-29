// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Settings;

/// <summary>Settings ranges: front-end-menus.md section 6. UiScale is our percentage (50..200).
/// Defaults are HYPOTHESIS pending the registry initialization path; 0x0055B980 restores saved values, not defaults.</summary>
public sealed record GameSettings
{
    public int Gamma { get; init; } = 30;
    public int ShadowLevel { get; init; } = 3;
    public int SoundQuality { get; init; } = 3;
    public int MasterVolume { get; init; } = 100;
    public int SpeechVolume { get; init; } = 100;
    public int MusicVolume { get; init; } = 100;
    public int SoundEffectVolume { get; init; } = 100;
    public int MouseSensitivity { get; init; } = 10;
    public int GameSpeed { get; init; } = 4;
    public int ScrollSpeed { get; init; } = 10;
    public int UiScale { get; init; } = 100;
    public bool WaterTranslucency { get; init; } = true;
    public bool EnvironmentMapping { get; init; } = true;
    public bool Headphones { get; init; } = false;
    public bool SpeechSwitch { get; init; } = true;
    public bool MusicSwitch { get; init; } = true;
    public bool SfxSwitch { get; init; } = true;
    public bool QSound { get; init; } = false;
    public bool EnvironmentalEffects { get; init; } = true;
    public bool InvertMouse { get; init; } = false;
    public IReadOnlyList<GameKeyBinding> Bindings { get; init; } = Array.Empty<GameKeyBinding>();

    public GameKeyBinding Binding(GameAction action)
    {
        for (int i = Bindings.Count - 1; i >= 0; i--)
            if (Bindings[i].Action == action) return Bindings[i];
        return DefaultKeyBindings.Get(action);
    }

    public GameSettings Clamped() => this with
    {
        Gamma = Math.Clamp(Gamma, 15, 60),
        ShadowLevel = Math.Clamp(ShadowLevel, 0, 3),
        SoundQuality = Math.Clamp(SoundQuality, 1, 3),
        MasterVolume = Math.Clamp(MasterVolume, 0, 100),
        SpeechVolume = Math.Clamp(SpeechVolume, 0, 100),
        MusicVolume = Math.Clamp(MusicVolume, 0, 100),
        SoundEffectVolume = Math.Clamp(SoundEffectVolume, 0, 100),
        MouseSensitivity = Math.Clamp(MouseSensitivity, 5, 20),
        GameSpeed = Math.Clamp(GameSpeed, 1, 16),
        ScrollSpeed = Math.Clamp(ScrollSpeed, 5, 20),
        UiScale = Math.Clamp(UiScale, 50, 200),
        Bindings = Array.AsReadOnly((Bindings ?? Array.Empty<GameKeyBinding>())
            .Where(b => (int)b.Action >= 0 && (int)b.Action < 64 && b.ScanCode >= 0 && b.ScanCode < 256
                && (b.Modifiers & ~KeyModifiers.All) == 0).GroupBy(b => b.Action).Select(g => g.Last()).ToArray()),
    };
}
