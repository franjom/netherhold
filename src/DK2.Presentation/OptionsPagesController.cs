// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation;

public enum OptionsApplicationMode { FrontEnd, InGame }

/// <summary>Front-end page transactions and immediate in-game settings application.</summary>
public sealed class OptionsPagesController
{
    private GameSettings _entry;
    private OptionsApplicationMode _applicationMode;
    public OptionsPagesController(GameSettings settings) { Current = Pending = _entry = settings.Clamped(); }
    public GameSettings Current { get; private set; }
    public GameSettings Pending { get; private set; }
    public FrontEndPage Page { get; private set; }
    public event Action<GameSettings>? Applied;
    public event Action<GameSettings>? SaveRequested;
    public GameAction? CapturingAction { get; private set; }
    public bool BeginKeyCapture(GameAction action)
    {
        if (Page != FrontEndPage.Controls || !KeyboardSetup.IsBuilt(action)) return false;
        CapturingAction = action; return true;
    }
    public bool CaptureKey(KeyScanCode key, KeyModifiers modifiers)
    {
        int scanCode = key.Value;
        if (CapturingAction is not GameAction action || scanCode <= 0 || scanCode >= 256) return false;
        if ((modifiers & ~KeyModifiers.All) != 0) return false;
        if (action is GameAction.ScrollUp or GameAction.ScrollDown or GameAction.ScrollLeft or GameAction.ScrollRight
            && modifiers != KeyModifiers.None) return false;
        if (action is GameAction.CameraModifier or GameAction.FastScroll)
        {
            KeyModifiers pairModifiers = modifiers & (KeyModifiers.Control | KeyModifiers.Shift);
            if (pairModifiers is not KeyModifiers.Control and not KeyModifiers.Shift) return false;
            GameAction other = action == GameAction.CameraModifier ? GameAction.FastScroll : GameAction.CameraModifier;
            int scan = pairModifiers == KeyModifiers.Control ? 0x1D : 0x2A;
            Edit(s => s with { Bindings = s.Bindings.Where(b => b.Action != action && b.Action != other)
                .Concat(new[] { new GameKeyBinding(action, scan), new GameKeyBinding(other, scan == 0x1D ? 0x2A : 0x1D) }).ToArray() });
        }
        else
        {
            if (scanCode is 0x2A or 0x36 or 0x1D or 0x9D or 0x38 or 0xB8 || modifiers == KeyModifiers.All) return false;
            Edit(s =>
            {
                var bindings = s.Bindings.Where(b => b.Action != action).ToList();
                foreach (KeyboardAction row in KeyboardSetup.Actions)
                {
                    GameKeyBinding current = s.Binding(row.Action);
                    if (row.Action != action && current.ScanCode == scanCode && current.Modifiers == modifiers)
                    { bindings.RemoveAll(b => b.Action == row.Action); bindings.Add(new(row.Action, 0)); }
                }
                bindings.Add(new(action, scanCode, modifiers)); return s with { Bindings = bindings };
            });
        }
        CapturingAction = null; return true;
    }
    public void CancelKeyCapture() => CapturingAction = null;
    public void Enter(FrontEndPage page, OptionsApplicationMode applicationMode = OptionsApplicationMode.FrontEnd)
    {
        if (Page == page && _applicationMode == applicationMode) return;
        _applicationMode = applicationMode;
        CapturingAction = null; Page = page; _entry = Current.Clamped(); Pending = Current;
    }
    public void Edit(Func<GameSettings, GameSettings> edit)
    {
        Pending = edit(Pending).Clamped();
        if (_applicationMode == OptionsApplicationMode.InGame) { Apply(Pending); SaveRequested?.Invoke(Current); }
        else if (Page == FrontEndPage.Sound) { Apply(Pending); SaveRequested?.Invoke(Current); }
        else if (Page == FrontEndPage.Controls)
            Apply(Current with { MouseSensitivity = Pending.MouseSensitivity, Bindings = Pending.Bindings });
    }
    public void Accept()
    {
        CapturingAction = null; Apply(Pending); SaveRequested?.Invoke(Current); Page = FrontEndPage.None;
    }
    public void Cancel()
    {
        if (Page == FrontEndPage.Sound)
            Apply(Current with { MasterVolume = _entry.MasterVolume, SpeechVolume = _entry.SpeechVolume,
                MusicVolume = _entry.MusicVolume, SoundEffectVolume = _entry.SoundEffectVolume,
                Headphones = _entry.Headphones, QSound = _entry.QSound,
                EnvironmentalEffects = _entry.EnvironmentalEffects, SoundQuality = _entry.SoundQuality });
        else if (Page == FrontEndPage.Controls) Apply(_entry);
        if (Page == FrontEndPage.Sound || Page == FrontEndPage.Controls) SaveRequested?.Invoke(Current);
        CapturingAction = null; Pending = Current; Page = FrontEndPage.None;
    }
    private void Apply(GameSettings settings) { Current = settings; Applied?.Invoke(Current); }
    public bool Activate(FrontEndWidget widget)
    {
        ushort id = widget.Id.Value;
        if (id is 129 or 146 or 159) { Accept(); return true; }
        if (id is 130 or 147 or 160) { Cancel(); return true; }
        if (Range(id).Maximum > 1 && id != 133) return Supports(id);
        return SetWidget(id, id == 133 ? Value(id) % 3 + 1 : Value(id) == 0 ? 1 : 0);
    }
    public bool Supports(ushort id) => id is 126 or 133 or 139 or 141 or 143 or 144
        or 676 or 677 or 678 or 154 or 156 or 158;
    public (int Minimum, int Maximum) Range(ushort id) => id switch
    {
        900 => (50, 200), 126 => (15, 60), 128 => (0, 3), 133 => (1, 3),
        139 or 141 or 143 or 144 => (0, 100), 154 => (1, 16), 156 or 158 => (5, 20), _ => (0, 1),
    };
    public int Value(ushort id) => id switch
    {
        900 => Pending.UiScale, 116 => Pending.WaterTranslucency ? 1 : 0, 118 => Pending.EnvironmentMapping ? 1 : 0,
        126 => Pending.Gamma, 128 => Pending.ShadowLevel, 133 => Pending.SoundQuality,
        135 => Pending.Headphones ? 1 : 0, 139 => Pending.MasterVolume, 141 => Pending.SpeechVolume,
        143 => Pending.MusicVolume, 144 => Pending.SoundEffectVolume, 137 => Pending.QSound ? 1 : 0,
        674 => Pending.EnvironmentalEffects ? 1 : 0, 676 => Pending.MusicSwitch ? 1 : 0,
        677 => Pending.SfxSwitch ? 1 : 0, 678 => Pending.SpeechSwitch ? 1 : 0,
        152 => Pending.InvertMouse ? 1 : 0, 154 => Pending.GameSpeed, 156 => Pending.MouseSensitivity,
        158 => Pending.ScrollSpeed, _ => 0,
    };
    public bool SetWidget(ushort id, int value)
    {
        if (!Supports(id)) return false;
        (int lo, int hi) = Range(id); value = Math.Clamp(value, lo, hi);
        if (Value(id) == value) return true;
        Edit(s => id switch
        {
            900 => s with { UiScale = value }, 116 => s with { WaterTranslucency = value != 0 }, 118 => s with { EnvironmentMapping = value != 0 },
            126 => s with { Gamma = value }, 128 => s with { ShadowLevel = value },
            133 => s with { SoundQuality = value }, 135 => s with { Headphones = value != 0 },
            139 => s with { MasterVolume = value }, 141 => s with { SpeechVolume = value },
            143 => s with { MusicVolume = value }, 144 => s with { SoundEffectVolume = value },
            137 => s with { QSound = value != 0 }, 674 => s with { EnvironmentalEffects = value != 0 },
            676 => s with { MusicSwitch = value != 0 }, 677 => s with { SfxSwitch = value != 0 },
            678 => s with { SpeechSwitch = value != 0 }, 152 => s with { InvertMouse = value != 0 },
            154 => s with { GameSpeed = value }, 156 => s with { MouseSensitivity = value },
            158 => s with { ScrollSpeed = value }, _ => s,
        });
        return true;
    }
}
