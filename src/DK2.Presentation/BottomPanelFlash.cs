// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>What a FLASH_BUTTON target flashes on the bottom panel this frame: a tab, an item slot,
/// or nothing, and whether this is the dark half of the second.</summary>
public readonly record struct BottomPanelFlashState(BottomPanelCategory? Tab, BottomPanelChoiceKey? Slot, bool Dark)
{
    public static BottomPanelFlashState None => default;
}

/// <summary>The bottom panel's scripted flash (docs/research/tab-alerts.md): one global target,
/// set only by trigger action 9; tabs (<c>0x00412260</c>) and slots (<c>0x00412FC0</c>) darken to
/// <see cref="DarkTint"/> on the odd half-second of the real clock and wear four sparkles.</summary>
public static class BottomPanelFlash
{
    /// <summary>The odd phase's tint, <c>0xFF323232</c>.</summary>
    public const byte DarkTint = 0x32;

    private const byte CategoryHud = 0, CategoryRoom = 1, CategoryCreature = 2, CategoryDoor = 3,
        CategoryTrap = 4, CategorySpell = 5;

    /// <summary>Which tab or slot flashes. A tab target (category 0, button F8..FB) flashes its tab even
    /// with its panel open; an item flashes its slot while its panel is open, else its tab; a creature
    /// target flashes the Creatures tab while that panel is closed (no slot flash was found).
    /// <paramref name="open"/> holds every panel showing (two with a pinned tab), none while collapsed.</summary>
    public static BottomPanelFlashState Resolve(HudFlashTarget? target, BottomPanelOpenSet open, long realMilliseconds)
    {
        if (target is not HudFlashTarget flash) return BottomPanelFlashState.None;
        bool dark = ((realMilliseconds / 500) & 1) != 0;
        switch (flash.Category)
        {
            case CategoryHud:
                BottomPanelCategory? tab = flash.Button switch
                {
                    0xF8 => BottomPanelCategory.Creatures, 0xF9 => BottomPanelCategory.Workshop,
                    0xFA => BottomPanelCategory.Spells, 0xFB => BottomPanelCategory.Rooms, _ => null,
                };
                return tab is null ? BottomPanelFlashState.None : new BottomPanelFlashState(tab, null, dark);
            case CategoryCreature:
                return open.Contains(BottomPanelCategory.Creatures)
                    ? BottomPanelFlashState.None : new BottomPanelFlashState(BottomPanelCategory.Creatures, null, dark);
            case CategoryRoom: return Item(BottomPanelChoiceKind.Room, BottomPanelCategory.Rooms, flash.Button, open, dark);
            case CategorySpell: return Item(BottomPanelChoiceKind.Spell, BottomPanelCategory.Spells, flash.Button, open, dark);
            case CategoryDoor: return Item(BottomPanelChoiceKind.Door, BottomPanelCategory.Workshop, flash.Button, open, dark);
            case CategoryTrap: return Item(BottomPanelChoiceKind.Trap, BottomPanelCategory.Workshop, flash.Button, open, dark);
            default: return BottomPanelFlashState.None;
        }
    }

    private static BottomPanelFlashState Item(BottomPanelChoiceKind kind, BottomPanelCategory panel, byte id,
        BottomPanelOpenSet open, bool dark) => open.Contains(panel)
        ? new BottomPanelFlashState(null, new BottomPanelChoiceKey(kind, id), dark)
        : new BottomPanelFlashState(panel, null, dark);

    /// <summary><c>0x00426F30</c> and the slot clicks: the flash ends once the flashed item is the
    /// active tool (room tool with that room, door, trap or spell choice, or the Sell tool for button
    /// FF); the Creatures tab target (F8) ends when its panel shows (<c>0x00416FD0</c>). The room id is
    /// read as the room type, LIKELY (tab-alerts.md).</summary>
    public static bool ClearedByUse(HudFlashTarget target, PlayerToolMode tool, RoomTypeId room,
        BottomPanelChoiceKey? choice, BottomPanelOpenSet open)
    {
        switch (target.Category)
        {
            case CategoryHud:
                return target.Button == 0xFF ? tool == PlayerToolMode.SellRoom
                    : target.Button == 0xF8 && open.Contains(BottomPanelCategory.Creatures);
            case CategoryRoom: return tool == PlayerToolMode.BuildRoom && (byte)room == target.Button;
            case CategorySpell: return choice == new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, target.Button);
            case CategoryDoor: return choice == new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, target.Button);
            case CategoryTrap: return choice == new BottomPanelChoiceKey(BottomPanelChoiceKind.Trap, target.Button);
            default: return false;
        }
    }
}

/// <summary>The flash's four sparkles. <c>0x004033F0</c> starts each at a random frame below 14 and
/// random coordinates below 0xAC1 across and 0x9E9 down; <c>0x00419DF0</c> then raises each frame
/// byte by one every rendered HUD frame, and past 15 wraps it to 0 with new coordinates below 0xB7F
/// and 0xB23. All draws are byte-truncated and come from the interface's own DK2 generator, never the
/// simulation's. A widget draws sparkle i as <c>GSparkle&lt;frame&gt;</c>, a square of
/// <see cref="SizeFor"/> units, at <c>(left + X % width - half, top + Y % height - half)</c>.</summary>
public sealed class BottomPanelSparkles
{
    public const int Count = 4, Frames = 16, Size = 64, LargeSize = 88;

    /// <summary>The sparkle's side for a widget whose <c>+0x3C</c> is <paramref name="widgetBank"/>:
    /// 88 units on a 6 (264-unit slots, <see cref="BottomPanelGrid.WidgetBank"/>), else 64 (tabs carry
    /// 1..4, 128-unit slots 5).</summary>
    public static int SizeFor(int widgetBank) => widgetBank == 6 ? LargeSize : Size;
    private readonly byte[] _frame = new byte[Count], _x = new byte[Count], _y = new byte[Count];
    private readonly Dk2Lcg _random;

    /// <summary>The interface seed's value at reset is unread, so it is a parameter.</summary>
    public BottomPanelSparkles(uint seed = Dk2Lcg.LevelStartSeed)
    {
        _random = new Dk2Lcg(seed);
        for (int i = 0; i < Count; i++)
        {
            _frame[i] = (byte)_random.NextInt(0xE);
            _x[i] = (byte)_random.NextInt(0xAC1);
            _y[i] = (byte)_random.NextInt(0x9E9);
        }
    }

    public int Frame(int index) => _frame[index];
    public int X(int index) => _x[index];
    public int Y(int index) => _y[index];

    /// <summary>One rendered HUD frame.</summary>
    public void Advance()
    {
        for (int i = 0; i < Count; i++)
        {
            if (++_frame[i] < Frames) continue;
            _frame[i] = 0;
            _x[i] = (byte)_random.NextInt(0xB7F);
            _y[i] = (byte)_random.NextInt(0xB23);
        }
    }
}
