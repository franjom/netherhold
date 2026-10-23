// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The six indicator menus 0x00427BA0 shows above the bottom panel, by menu id
/// (docs/research/message-tabs.md §9).</summary>
public enum HudIndicator
{
    TimeRemaining = 45,
    Score = 46,
    CurrentHolder = 47,
    HeroesKilled = 48,
    SlapsRemaining = 49,
    NextRoom = 50,
}

/// <summary>One frame's indicators: which show (0x00427BA0's tests) and the values the built ones draw.</summary>
public readonly record struct HudIndicatorSnapshot(
    bool TimeRemaining, int TimeLimitTurns, bool Score, int ScoreValue, bool HeroesKilled, bool NextRoom)
{
    /// <summary>Interface <c>+0x10C2</c>: any indicator shows, so menu40 sits 168 above the strip, not 40.</summary>
    public bool Any => TimeRemaining || Score || HeroesKilled || NextRoom;

    public bool Shows(HudIndicator indicator) => indicator switch
    {
        HudIndicator.TimeRemaining => TimeRemaining,
        HudIndicator.Score => Score,
        HudIndicator.HeroesKilled => HeroesKilled,
        HudIndicator.NextRoom => NextRoom,
        _ => false,
    };

    /// <summary>The tests 0x00427BA0 makes, on what our simulation holds. Current Holder (world
    /// <c>+0x72B6</c>, writer unread) and Slaps Remaining (<c>+0x72B8</c>, -1 until action 50, which
    /// is not built) never show.</summary>
    public static HudIndicatorSnapshot Capture(WorldState world, LevelDef? level)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        return Capture(world.Triggers, level);
    }

    public static HudIndicatorSnapshot Capture(TriggerState triggers, LevelDef? level)
    {
        if (triggers is null) throw new ArgumentNullException(nameof(triggers));
        byte nextRoom = triggers.SpecialInfoNextRoomTypeId;
        return new HudIndicatorSnapshot(
            TimeRemaining: triggers.LevelTimeLimit != 0,
            TimeLimitTurns: triggers.LevelTimeLimit,
            Score: triggers.SpecialValueWritten,
            ScoreValue: triggers.SpecialValue,
            // LEVEL flag 0x80 becomes global 0x006F2957 bit 5 (0x0051E7F4).
            HeroesKilled: level is not null && (level.Flags & 0x80) != 0,
            NextRoom: nextRoom != 0 && nextRoom < 0x1A);
    }
}

/// <summary>Where 0x00427BA0 puts each shown indicator, and the menu40 rectangle 0x00423F50 sets,
/// in 2560x1920 virtual units.</summary>
public static class HudIndicatorLayout
{
    public const int Left = 20, FirstTop = 1332, RowHeight = 128;
    /// <summary>The indicator menus' widths (static records 0x00693C70.., 72 bytes apart).</summary>
    public static int Width(HudIndicator indicator) => indicator >= HudIndicator.SlapsRemaining ? 256 : 444;

    private static readonly HudIndicator[] Order =
    {
        HudIndicator.TimeRemaining, HudIndicator.Score, HudIndicator.CurrentHolder,
        HudIndicator.HeroesKilled, HudIndicator.SlapsRemaining, HudIndicator.NextRoom,
    };

    /// <summary>Shown indicators in menu order with their top-left. Packed left to right from x 20;
    /// the first sits at y 1332 whatever the panel, later ones at panel top - 128 (the same row
    /// while the panel is expanded). In camera class 1 (<c>0x0044D870</c>, modes 1/2/10/11) they
    /// stack upward from 1332 instead, 1204 next, 128 a step.</summary>
    public static IReadOnlyList<(HudIndicator Indicator, int X, int Y)> Place(
        in HudIndicatorSnapshot shown, int panelTop, bool stacked = false)
    {
        var placed = new List<(HudIndicator, int, int)>(Order.Length);
        int x = Left, y = FirstTop;
        foreach (HudIndicator indicator in Order)
        {
            if (!shown.Shows(indicator)) continue;
            placed.Add((indicator, x, y));
            if (stacked) y -= RowHeight;
            else { x += Width(indicator); y = panelTop - RowHeight; }
        }
        return placed;
    }

    public const int PanelLeft = 1020, PanelWidth = 1200, TextBoxHeight = 288, OverflowHeight = 440;

    /// <summary>Menu40 in message mode (0x00423F50): 4 * the text height in quarter units + 172 when
    /// that fits 288 strictly, else 440; its bottom 168 above the strip while an indicator shows,
    /// else 40. <paramref name="textHeight"/> is in virtual units, read as the quarter units times 4.</summary>
    public static (int X, int Y, int Width, int Height) MessagePanel(int panelTop, int textHeight, bool indicators)
    {
        int height = textHeight < TextBoxHeight ? textHeight + 172 : OverflowHeight;
        return (PanelLeft, panelTop - height - (indicators ? 168 : 40), PanelWidth, height);
    }

    /// <summary>0x00417EA0 around a menu's screen rectangle: 0x00411D00's nine text_box pieces frame
    /// the rectangle inset by 10 pixels, 16 pixels outside it, so the frame overhangs by 6. The
    /// pixels are DK2's device pixels; <paramref name="pixel"/> is our size of one.</summary>
    public static (float X, float Y, float Width, float Height) FrameInner(float x, float y, float width, float height, float pixel)
    {
        float inset = 10 * pixel;
        return (x + inset, y + inset, Math.Max(0, width - 2 * inset), Math.Max(0, height - 2 * inset));
    }

    public const int FrameBorder = 16, FrameMiddleTile = 32;

    /// <summary>Time Remaining's digits (0x00427230): seconds = turns rounded up over turns a second;
    /// each part is right-aligned to its slot of 0x00415650 (40 units each), the leading zeroes of the
    /// minutes and the seconds' tens left out until a larger part is non-zero.</summary>
    public static IReadOnlyList<(string Digits, int Slots)> TimeRemainingDigits(int turns, int turnsPerSecond)
    {
        if (turnsPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(turnsPerSecond));
        uint seconds = (uint)((long)(uint)(turns - 1 + turnsPerSecond) / turnsPerSecond);
        uint minutes = seconds / 60, rest = seconds % 60;
        var parts = new List<(string, int)>(4) { (Digit(rest % 10), 10) };
        if (rest / 10 != 0 || minutes != 0) parts.Add((Digit(rest / 10), 9));
        if (minutes % 10 != 0 || minutes / 10 != 0) parts.Add((Digit(minutes % 10), 7));
        if (minutes / 10 != 0) parts.Add(((minutes / 10).ToString(System.Globalization.CultureInfo.InvariantCulture), 6));
        return parts;
    }

    private static string Digit(uint value) => ((char)('0' + value)).ToString();

    /// <summary>0x00415650 with right alignment: the left edge, in advances, of a run of
    /// <paramref name="length"/> characters right-aligned to <paramref name="slots"/>.</summary>
    public static int AlignedStart(int length, int slots) => Math.Max(0, slots - length);
}
