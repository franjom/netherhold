// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Menus 45..50 in the possession camera's stacked layout; see message-tabs.md §9.</summary>
public sealed class PossessionHudIndicators
{
    private readonly IBottomPanelArt _art;
    private readonly Func<int, BitmapFont> _font;
    private readonly Func<PlayerId, string> _playerName;
    private int _scroll;
    private bool _forward = true;
    private readonly HudFrame _nativeFrame = new();
    private static readonly string[] Art =
    {
        "GUI\\Windows\\timer-bg", "GUI\\Windows\\score-bg", "GUI\\Windows\\kingofhill-bg",
        "GUI\\Windows\\kills-bg", "GUI\\Windows\\slaps-bg", "GUI\\Icons\\next_room",
    };

    public PossessionHudIndicators(IBottomPanelArt art, Func<int, BitmapFont> font, Func<PlayerId, string> playerName)
    { _art = art; _font = font; _playerName = playerName; }

    public void Build(HudScaleLayout layout, in HudIndicatorSnapshot shown, HudFrame target)
    {
        _nativeFrame.Clear();
        HudFrame frame = _nativeFrame;
        HudScreen screen = layout.Screen;
        float bottomShift = 1920 / layout.Factor - 1920;
        foreach ((HudIndicator indicator, int x, int originalY) in HudIndicatorLayout.Place(shown, 1460, stacked: true))
        {
            float y = originalY + bottomShift;
            bool room = indicator == HudIndicator.NextRoom;
            Quad(frame, Art[(int)indicator - 45], new(x, y, room ? 128 : 256, 128));
            if (room)
            {
                HudImage icon = _art.Icon(new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, shown.NextRoomTypeId));
                if (!icon.IsNone) Quad(frame, icon, new(x + 128, y, 128, 128));
                continue;
            }
            if (indicator != HudIndicator.SlapsRemaining)
                Quad(frame, "GUI\\Windows\\gold-bg-01", new(x + 256, y, 188, 128));
            switch (indicator)
            {
                case HudIndicator.TimeRemaining:
                    foreach ((string digits, int slots) in HudIndicatorLayout.TimeRemainingDigits(shown.TimeLimitTurns, SimulationTiming.OriginalTicksPerGameSecond))
                        Digits(frame, screen, digits, slots, x, y + 40);
                    break;
                case HudIndicator.Score: Digits(frame, screen, shown.ScoreValue.ToString(CultureInfo.InvariantCulture), 10, x, y + 40); break;
                case HudIndicator.HeroesKilled: Digits(frame, screen, shown.HeroesDestroyed.ToString(CultureInfo.InvariantCulture), 10, x, y + 40); break;
                case HudIndicator.SlapsRemaining: Digits(frame, screen, shown.SlapsLeft.ToString(CultureInfo.InvariantCulture), 6, x, y + 40); break;
                case HudIndicator.CurrentHolder: Holder(frame, screen, new PlayerId(shown.HolderPlayerId), x, y); break;
            }
        }
        float xScale = PossessionHud.ContentXScale(layout);
        foreach (HudDraw draw in frame.Draws)
            target.Add(draw with { Rect = draw.Rect with { X = draw.Rect.X * xScale, Width = draw.Rect.Width * xScale },
                Clip = draw.Clip is HudRect clip ? clip with { X = clip.X * xScale, Width = clip.Width * xScale } : null });
    }

    private static void Digits(HudFrame frame, HudScreen screen, string text, int slots, int x, float y)
    {
        int start = HudIndicatorLayout.AlignedStart(text.Length, slots);
        string family = screen.Width >= 640 ? "GUI\\Icons\\i-" : "GUI\\Icons\\lo-";
        for (int i = 0; i < text.Length; i++)
            Quad(frame, text[i] == '-' ? "GUI\\Icons\\I--" : family + text[i], new(x + (start + i) * 40, y, 48, 48));
    }

    private void Holder(HudFrame frame, HudScreen screen, PlayerId player, int x, float y)
    {
        var text = new HudText(_font(screen.Height), _playerName(player), int.MaxValue / 2);
        var size = HudTextPixels.Measure(text);
        int overflow = size.Width - (int)(260 * screen.Height / 1920f);
        if (overflow <= 0) { _scroll = 0; _forward = true; }
        else
        {
            _scroll += _forward ? 1 : -1;
            if (_scroll > overflow) _forward = false;
            else if (_scroll < 0) _forward = true;
        }
        int down = screen.Height == 300 ? 2 : 16;
        var window = new HudRect(x + 160, y + down, 260, 128 - down);
        HudQuads.Clipped(frame, HudImage.Of(text),
            new(window.X - screen.Virtual(_scroll), window.Y, screen.Virtual(size.Width), screen.Virtual(size.Height)), window, HudColor.White);
    }

    private static void Quad(HudFrame frame, HudImage image, HudRect rect) =>
        frame.Add(new(HudDrawKind.Quad, image, rect, HudColor.White));
}
