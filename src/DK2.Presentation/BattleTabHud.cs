// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>Menu 43, the Battle tab, as a draw list (ADR-0805): each shown button's art at its static place
/// (<see cref="BattleTabView"/>), hit areas over the last frame's shown buttons, the blades' hover text and the
/// presses. Unity's <c>ClassicBattleTab</c> places the same squares as UI Toolkit images; like its images, a button
/// is hit while the HUD shows, a press only while ordinary input is allowed, and only the left and right buttons
/// raise <see cref="Clicked"/> (any button is taken). What a click does is <see cref="BattleTabView.Action"/>.</summary>
public sealed class BattleTabHud
{
    private static readonly BattleTabButton[] Buttons = [BattleTabButton.Battle, BattleTabButton.CallToArms, BattleTabButton.Alarm];
    private readonly BattleTabHoverText _text;
    private readonly bool[] _shown = new bool[Buttons.Length];
    private bool _collapsed, _allowInput;
    private HudScreen _screen = new(2560, 1920);

    /// <param name="text">A <c>Text.str</c> string by id.</param>
    public BattleTabHud(Func<uint, string> text) => _text = new BattleTabHoverText(text);

    /// <summary>A shown button and the mouse button (0 left, 1 right) that pressed it.</summary>
    public event Action<BattleTabButton, int>? Clicked;

    /// <summary>Every image the menu draws, for preloading.</summary>
    public static IEnumerable<HudImage> Images()
    {
        foreach (string art in BattleTabView.AllArt) yield return art;
    }

    /// <summary>Writes the shown buttons into <paramref name="frame"/>; nothing while the normal HUD is hidden.</summary>
    /// <param name="screen">The surface the menu lays out on; it stays flush right (<see cref="BattleTabView.MenuLeft"/>).</param>
    /// <param name="collapsed">Whether the bottom panel is collapsed, which drops the menu with it.</param>
    /// <param name="milliseconds">The real clock, which the frames step on whatever the game speed.</param>
    public void Build(HudScreen screen, HudVisibility visibility, bool collapsed, BattleTabSnapshot shown, long milliseconds,
        HudFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        _screen = screen; _collapsed = collapsed; _allowInput = visibility.AllowOrdinaryInput;
        int phase = BattleTabView.Phase(milliseconds);
        for (int i = 0; i < Buttons.Length; i++)
        {
            BattleTabButton button = Buttons[i];
            _shown[i] = visibility.ShowNormalHud && shown.Shows(button);
            if (_shown[i])
                frame.Add(new HudDraw(HudDrawKind.Quad, BattleTabView.Art(button, phase), BattleTabView.Rect(button, collapsed, screen), HudColor.White));
        }
    }

    /// <summary>The shown button under a point in virtual units, whatever the input gate; <see cref="BattleTabButton.None"/>
    /// for none.</summary>
    public BattleTabButton ButtonAt(float x, float y)
    {
        for (int i = 0; i < Buttons.Length; i++)
            if (_shown[i] && BattleTabView.Rect(Buttons[i], _collapsed, _screen).Contains(x, y)) return Buttons[i];
        return BattleTabButton.None;
    }

    /// <summary>Whether the point is the menu's while it takes input, so a host keeps it from the world.</summary>
    public bool ContainsPointer(float x, float y) => _allowInput && ButtonAt(x, y) != BattleTabButton.None;

    /// <summary>The status line and name for the button under the point (<see cref="BattleTabHoverText"/>).</summary>
    public HoverTextSnapshot HoverText(float x, float y) => _text.For(ButtonAt(x, y));

    /// <summary>A mouse press at a point in virtual units; true when the menu took it.</summary>
    /// <param name="mouse">0 left, 1 right, 2 middle.</param>
    public bool PointerDown(float x, float y, int mouse)
    {
        if (!ContainsPointer(x, y)) return false;
        if (mouse == 0 || mouse == 1) Clicked?.Invoke(ButtonAt(x, y), mouse);
        return true;
    }
}
