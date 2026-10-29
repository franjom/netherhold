// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Settings;

[Flags]
public enum KeyModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, All = 7 }

/// <summary>Native action indices; unnamed index zero is not listed by Keyboard Setup.</summary>
public enum GameAction
{
    Sniper = 1,
    PickLock = 2,
    Fire = 3,
    Creep = 4,
    Run = 5,
    Melee = 6,
    Spell1 = 7,
    Spell2 = 8,
    Spell3 = 9,
    PrimaryAbility = 10,
    SecondaryAbility = 11,
    SelectAlly = 12,
    RemoveAlly = 13,
    ZoomIn = 14,
    ZoomOut = 15,
    ScrollUp = 16,
    ScrollDown = 17,
    ScrollLeft = 18,
    ScrollRight = 19,
    CameraModifier = 20,
    FastScroll = 21,
    RotateLeft = 22,
    RotateRight = 23,
    Options = 24,
    Screenshot = 25,
    ObjectsOnly = 26,
    Broadcast = 27,
    AmbientUp = 28,
    AmbientDown = 29,
    Isometric = 30,
    TopDown = 31,
    Oblique = 32,
    UserCamera1 = 33,
    UserCamera2 = 34,
    UserCamera3 = 35,
    Allies = 36,
    NextFight = 37,
    TogglePanel = 38,
    Heart = 39,
    PlayerInformation = 40,
    FullMap = 41,
    Portal = 42,
    MouseRotate = 43,
    MouseZoom = 44,
    GammaUp = 45,
    GammaDown = 46,
    PitchUp = 47,
    PitchDown = 48,
    RollLeft = 49,
    RollRight = 50,
    YawLeft = 51,
    YawRight = 52,
    PickHighest = 53,
    PickLowest = 54,
    QuickLoad = 55,
    QuickSave = 56,
    Restart = 57,
    MessageAllies = 58,
    MessageAlly1 = 59,
    MessageAlly2 = 60,
    MessageAlly3 = 61,
    MessageAlly4 = 62,
    Action63 = 63,
}

public readonly record struct KeyScanCode(int Value)
{
    public static readonly KeyScanCode None = new(0);
    public static implicit operator KeyScanCode(int value) => new(value);
}

public readonly record struct GameKeyBinding(GameAction Action, int ScanCode, KeyModifiers Modifiers = KeyModifiers.None);

/// <summary>Default DirectInput scan codes read from 0x005604C0; adapter translates to platform keys.</summary>
public static class DefaultKeyBindings
{
    private static readonly int[] Scans = { 0, 0xD2, 0x52, 0x39, 0x9D, 0x36, 2, 3, 4, 5, 6, 7, 8, 0x22,
        0xC7, 0xCF, 0xC8, 0xD0, 0xCB, 0xCD, 0x1D, 0x2A, 0xD3, 0xD1, 1, 0xB7, 0xC9, 0xF, 0xD, 0xC,
        0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x1E, 0x21, 0x22, 0x23, 0x17, 0x32, 0x19, 0x2D, 0x2C,
        0x34, 0x33, 0xC7, 0xCF, 0xD2, 0xD3, 0xC9, 0xD1, 0x34, 0x33, 0x26, 0x1F, 0x13, 0x1E, 2, 3, 4, 5, 0x19 };
    public static GameKeyBinding Get(GameAction action)
    {
        int index = (int)action;
        if (index < 0 || index >= Scans.Length) throw new ArgumentOutOfRangeException(nameof(action));
        KeyModifiers modifiers = index is 13 or 63 or >= 47 and <= 57 ? KeyModifiers.Control
            : index is 45 or 46 ? KeyModifiers.Shift : index >= 58 ? KeyModifiers.Alt : KeyModifiers.None;
        return new(action, Scans[index], modifiers);
    }
}
